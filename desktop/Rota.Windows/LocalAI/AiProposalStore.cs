using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public sealed class AiProposalStore : IAiProposalStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumRecords = 100;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public AiProposalStore(string? storePath = null, Func<DateTimeOffset>? utcNow = null)
    {
        StorePath = Path.GetFullPath(storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "AI",
            "proposals.json"));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string StorePath { get; }
    public string StoreDirectory => Path.GetDirectoryName(StorePath)!;
    public string LastLoadWarning { get; private set; } = "";

    public async Task<IReadOnlyList<AiStoredProposal>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            return state.Records
                .OrderByDescending(record => record.UpdatedAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiStoredProposal> SavePreviewAsync(
        AiProposal proposal,
        AiProposalPreview preview,
        CancellationToken cancellationToken = default) =>
        await SavePreviewAsync(proposal, preview, Guid.Empty, cancellationToken).ConfigureAwait(false);

    public async Task<AiStoredProposal> SavePreviewAsync(
        AiProposal proposal,
        AiProposalPreview preview,
        Guid requestTurnId,
        CancellationToken cancellationToken)
    {
        ValidatePair(proposal, preview);
        if (proposal.Status != AiProposalStatus.Pending)
            throw new AiContractValidationException("Somente uma proposta nova e pendente pode ser registrada.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            if (state.Records.Any(record => record.Proposal.Id == proposal.Id))
                throw new AiContractValidationException("A proposta já existe no histórico local.");
            if (state.Records.Count >= MaximumRecords)
            {
                var removable = state.Records.FirstOrDefault(record =>
                    record.Proposal.Status is AiProposalStatus.Failed or
                        AiProposalStatus.Rejected or AiProposalStatus.Undone);
                if (removable is null)
                {
                    throw new AiContractValidationException(
                        "O histórico local atingiu o limite de 100 propostas ativas ou aplicadas.");
                }
                state.Records.Remove(removable);
            }

            var stored = new AiStoredProposal
            {
                Proposal = proposal with
                {
                    Status = preview.CanProceed ? AiProposalStatus.Validated : AiProposalStatus.Failed
                },
                Preview = preview,
                UpdatedAtUtc = Now(),
                RequestTurnId = requestTurnId
            };
            ValidateRecord(stored);
            state.Records.Add(stored);
            await WriteAtomicallyAsync(state, cancellationToken).ConfigureAwait(false);
            return stored;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AiStoredProposal> AcceptAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        TransitionAsync(proposalId, AiProposalStatus.Accepted, cancellationToken);

    public Task<AiStoredProposal> MarkAppliedAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        TransitionAsync(proposalId, AiProposalStatus.Applied, cancellationToken);

    public Task<AiStoredProposal> MarkUndoneAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        TransitionAsync(proposalId, AiProposalStatus.Undone, cancellationToken);

    public Task<AiStoredProposal> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        TransitionAsync(proposalId, AiProposalStatus.Rejected, cancellationToken);

    private async Task<AiStoredProposal> TransitionAsync(
        Guid proposalId,
        AiProposalStatus target,
        CancellationToken cancellationToken)
    {
        if (proposalId == Guid.Empty)
            throw new AiContractValidationException("A proposta precisa de um ID válido.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Records.FindIndex(record => record.Proposal.Id == proposalId);
            if (index < 0)
                throw new KeyNotFoundException("A proposta não existe no histórico local.");
            var current = state.Records[index];
            if (current.Proposal.Status == target) return current;
            if (!CanTransition(current.Proposal.Status, target))
                throw new AiContractValidationException(
                    $"A proposta não pode passar de {current.Proposal.Status} para {target}.");

            var updated = current with
            {
                Proposal = current.Proposal with { Status = target },
                UpdatedAtUtc = Now()
            };
            ValidateRecord(updated);
            state.Records[index] = updated;
            await WriteAtomicallyAsync(state, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool CanTransition(AiProposalStatus current, AiProposalStatus target) =>
        (current, target) switch
        {
            (AiProposalStatus.Validated, AiProposalStatus.Accepted) => true,
            (AiProposalStatus.Validated, AiProposalStatus.Applied) => true,
            (AiProposalStatus.Validated, AiProposalStatus.Rejected) => true,
            (AiProposalStatus.Accepted, AiProposalStatus.Applied) => true,
            (AiProposalStatus.Accepted, AiProposalStatus.Rejected) => true,
            (AiProposalStatus.Applied, AiProposalStatus.Undone) => true,
            (AiProposalStatus.Undone, AiProposalStatus.Applied) => true,
            _ => false
        };

    private async Task<ProposalStoreState> LoadStateWithRecoveryAsync(CancellationToken cancellationToken)
    {
        LastLoadWarning = "";
        if (!File.Exists(StorePath))
        {
            var fresh = new ProposalStoreState();
            await WriteAtomicallyAsync(fresh, cancellationToken).ConfigureAwait(false);
            return fresh;
        }
        try
        {
            return await ReadAsync(StorePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsInvalidStoreError(ex))
        {
            var corruptPath = PreserveInvalidFile(StorePath, "proposals.corrupt");
            var backupPath = StorePath + ".bak";
            try
            {
                if (File.Exists(backupPath))
                {
                    var recovered = await ReadAsync(backupPath, cancellationToken).ConfigureAwait(false);
                    await WriteAtomicallyAsync(recovered, cancellationToken).ConfigureAwait(false);
                    LastLoadWarning =
                        $"O histórico principal da IA estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. " +
                        "O Rota recuperou a última cópia íntegra.";
                    return recovered;
                }
            }
            catch (Exception backupError) when (IsInvalidStoreError(backupError))
            {
                PreserveInvalidFile(backupPath, "proposals.backup-corrupt");
            }

            var fresh = new ProposalStoreState();
            await WriteAtomicallyAsync(fresh, cancellationToken).ConfigureAwait(false);
            LastLoadWarning =
                $"O histórico local da IA estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. " +
                "Não havia uma cópia íntegra; o Rota iniciou um histórico vazio.";
            return fresh;
        }
    }

    private async Task<ProposalStoreState> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumFileBytes)
            throw new InvalidDataException("O histórico da IA excede o limite seguro de 8 MB.");
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        EnsureNoDuplicateProperties(document.RootElement);
        var state = JsonSerializer.Deserialize<ProposalStoreState>(json, _jsonOptions)
            ?? throw new InvalidDataException("O histórico da IA está vazio.");
        ValidateState(state);
        return state;
    }

    private async Task WriteAtomicallyAsync(ProposalStoreState state, CancellationToken cancellationToken)
    {
        ValidateState(state);
        Directory.CreateDirectory(StoreDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
        if (bytes.LongLength > MaximumFileBytes)
            throw new IOException("O histórico da IA excedeu o limite seguro de 8 MB.");
        var tempPath = Path.Combine(
            StoreDirectory,
            $".{Path.GetFileName(StorePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(StorePath))
                File.Move(tempPath, StorePath);
            else
                File.Replace(tempPath, StorePath, StorePath + ".bak", ignoreMetadataErrors: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static void ValidateState(ProposalStoreState state)
    {
        if (state.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Versão de histórico da IA não suportada: {state.SchemaVersion}.");
        if (state.Records is null || state.Records.Count > MaximumRecords)
            throw new InvalidDataException("O histórico da IA contém registros demais ou está incompleto.");
        var ids = new HashSet<Guid>();
        var requestIds = new HashSet<Guid>();
        foreach (var record in state.Records)
        {
            if (record is null || !ids.Add(record.Proposal.Id))
                throw new InvalidDataException("O histórico da IA contém propostas nulas ou duplicadas.");
            if (record.RequestTurnId != Guid.Empty && !requestIds.Add(record.RequestTurnId))
                throw new InvalidDataException("O histórico da IA contém vínculos de conversa duplicados.");
            ValidateRecord(record);
        }
    }

    private static void ValidateRecord(AiStoredProposal record)
    {
        AiContractValidator.ValidateProposal(record.Proposal);
        AiContractValidator.ValidatePreview(record.Preview);
        if (record.Proposal.Id != record.Preview.ProposalId || record.Proposal.Kind != record.Preview.Kind)
            throw new AiContractValidationException("A proposta e sua prévia não correspondem.");
        if (record.UpdatedAtUtc == default || record.UpdatedAtUtc.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("O registro da proposta precisa de um timestamp UTC.");
        if (record.Preview.State == AiProposalPreviewState.Ready &&
            record.Proposal.Status is not AiProposalStatus.Validated and not AiProposalStatus.Accepted and
                not AiProposalStatus.Applied and not AiProposalStatus.Undone and not AiProposalStatus.Rejected)
        {
            throw new AiContractValidationException("O estado da proposta não corresponde à prévia aprovada.");
        }
        if (record.Preview.State == AiProposalPreviewState.Blocked && record.Proposal.Status != AiProposalStatus.Failed)
            throw new AiContractValidationException("Uma prévia bloqueada precisa permanecer como falha.");
    }

    private static void ValidatePair(AiProposal proposal, AiProposalPreview preview)
    {
        AiContractValidator.ValidateProposal(proposal);
        AiContractValidator.ValidatePreview(preview);
        if (proposal.Id != preview.ProposalId || proposal.Kind != preview.Kind)
            throw new AiContractValidationException("A proposta e sua prévia não correspondem.");
    }

    private DateTimeOffset Now()
    {
        var timestamp = _utcNow();
        if (timestamp == default || timestamp.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("O relógio local não forneceu um timestamp UTC válido.");
        return timestamp;
    }

    private string PreserveInvalidFile(string path, string prefix)
    {
        if (!File.Exists(path)) return "";
        Directory.CreateDirectory(StoreDirectory);
        var preserved = Path.Combine(
            StoreDirectory,
            $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Move(path, preserved);
        return preserved;
    }

    private static bool IsInvalidStoreError(Exception exception) => exception is
        JsonException or InvalidDataException or DecoderFallbackException or
        NotSupportedException or AiContractValidationException;

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("O histórico da IA contém propriedades JSON duplicadas.");
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record ProposalStoreState
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;
        public List<AiStoredProposal> Records { get; init; } = new();
    }
}
