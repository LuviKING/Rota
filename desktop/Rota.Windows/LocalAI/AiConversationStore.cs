using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public sealed class AiConversationStore : IAiConversationStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumTurns = 200;
    private const int MaximumTurnTextLength = 8_000;
    private const long MaximumFileBytes = 2 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<Guid> _newId;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private bool _disposed;

    public AiConversationStore(
        string? storePath = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<Guid>? newId = null)
    {
        StorePath = Path.GetFullPath(storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "AI",
            "conversation.json"));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _newId = newId ?? Guid.NewGuid;
    }

    public string StorePath { get; }
    public string StoreDirectory => Path.GetDirectoryName(StorePath)!;
    public string LastLoadWarning { get; private set; } = "";

    public async Task<AiConversationSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Snapshot(await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiConversationSnapshot> ReconcileAsync(
        IReadOnlyList<AiStoredProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(proposals);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            var linked = proposals
                .Where(proposal => proposal.RequestTurnId != Guid.Empty)
                .GroupBy(proposal => proposal.RequestTurnId)
                .ToDictionary(group => group.Key, group => group.Single());
            var changed = false;
            foreach (var pending in state.Turns.Where(turn =>
                         turn.Role == AiConversationRole.User &&
                         turn.Status == AiConversationTurnStatus.Pending).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (linked.TryGetValue(pending.RequestId, out var proposal))
                    CompleteState(state, pending.RequestId, proposal);
                else
                    ReplaceTurn(state, pending with { Status = AiConversationTurnStatus.Failed });
                changed = true;
            }
            if (changed)
            {
                state.UpdatedAtUtc = Now();
                await WriteAtomicallyAsync(state, cancellationToken).ConfigureAwait(false);
            }
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiConversationSnapshot> BeginRequestAsync(
        string text,
        AiProposalKind kind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(kind))
            throw new AiContractValidationException("O tipo do pedido da conversa é inválido.");
        var normalized = ValidateAndNormalizeText(text);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            if (state.Turns.Any(turn => turn.Role == AiConversationRole.User &&
                                        turn.Status == AiConversationTurnStatus.Pending))
            {
                throw new AiContractValidationException("A conversa já possui um pedido local em andamento.");
            }
            TrimForNewRequest(state);
            var now = Now();
            var requestId = NextId(state);
            state.Turns.Add(new AiConversationTurn
            {
                RequestId = requestId,
                Role = AiConversationRole.User,
                Status = AiConversationTurnStatus.Pending,
                ProposalKind = kind,
                CreatedAtUtc = now,
                Text = normalized
            });
            state.UpdatedAtUtc = now;
            await WriteAtomicallyAsync(state, cancellationToken).ConfigureAwait(false);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiConversationSnapshot> CompleteRequestAsync(
        Guid requestId,
        AiStoredProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requestId == Guid.Empty)
            throw new AiContractValidationException("O pedido da conversa precisa de um ID válido.");
        ArgumentNullException.ThrowIfNull(proposal);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            if (!CompleteState(state, requestId, proposal)) return Snapshot(state);
            state.UpdatedAtUtc = Now();
            await WriteAtomicallyAsync(state, cancellationToken).ConfigureAwait(false);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiConversationSnapshot> MarkRequestAsync(
        Guid requestId,
        AiConversationTurnStatus status,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requestId == Guid.Empty)
            throw new AiContractValidationException("O pedido da conversa precisa de um ID válido.");
        if (status is not AiConversationTurnStatus.Cancelled and not AiConversationTurnStatus.Failed)
            throw new AiContractValidationException("Somente um pedido cancelado ou com falha pode ser encerrado sem proposta.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
            var user = FindUser(state, requestId);
            if (user.Status == status) return Snapshot(state);
            if (user.Status != AiConversationTurnStatus.Pending)
                throw new AiContractValidationException("O pedido da conversa já foi encerrado.");
            ReplaceTurn(state, user with { Status = status });
            state.UpdatedAtUtc = Now();
            await WriteAtomicallyAsync(state, cancellationToken).ConfigureAwait(false);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool CompleteState(ConversationStoreState state, Guid requestId, AiStoredProposal proposal)
    {
        if (proposal.RequestTurnId != requestId)
            throw new AiContractValidationException("A proposta não corresponde ao pedido registrado na conversa.");
        AiContractValidator.ValidateProposal(proposal.Proposal);
        AiContractValidator.ValidatePreview(proposal.Preview);
        var user = FindUser(state, requestId);
        var existingAssistant = state.Turns.SingleOrDefault(turn =>
            turn.RequestId == requestId && turn.Role == AiConversationRole.Assistant);
        if (existingAssistant is not null)
        {
            if (existingAssistant.ProposalId != proposal.Proposal.Id)
                throw new InvalidDataException("A conversa vincula o pedido a uma proposta diferente.");
            return false;
        }
        if (user.Status != AiConversationTurnStatus.Pending)
            throw new AiContractValidationException("O pedido da conversa já foi encerrado sem proposta.");

        ReplaceTurn(state, user with { Status = AiConversationTurnStatus.Completed });
        state.Turns.Add(new AiConversationTurn
        {
            RequestId = requestId,
            Role = AiConversationRole.Assistant,
            Status = AiConversationTurnStatus.Completed,
            ProposalKind = proposal.Proposal.Kind,
            ProposalId = proposal.Proposal.Id,
            CreatedAtUtc = proposal.UpdatedAtUtc,
            Text = AssistantText(proposal)
        });
        return true;
    }

    private static AiConversationTurn FindUser(ConversationStoreState state, Guid requestId) =>
        state.Turns.SingleOrDefault(turn =>
            turn.RequestId == requestId && turn.Role == AiConversationRole.User)
        ?? throw new KeyNotFoundException("O pedido não existe na conversa local.");

    private static void ReplaceTurn(ConversationStoreState state, AiConversationTurn replacement)
    {
        var index = state.Turns.FindIndex(turn =>
            turn.RequestId == replacement.RequestId && turn.Role == replacement.Role);
        if (index < 0) throw new InvalidDataException("O turno da conversa não foi encontrado.");
        state.Turns[index] = replacement;
    }

    private static string AssistantText(AiStoredProposal proposal)
    {
        var summary = proposal.Proposal.Summary.Trim();
        var preview = proposal.Preview.Message.Trim();
        return string.Equals(summary, preview, StringComparison.Ordinal)
            ? summary
            : summary + Environment.NewLine + preview;
    }

    private static void TrimForNewRequest(ConversationStoreState state)
    {
        while (state.Turns.Count > MaximumTurns - 2)
        {
            var oldestRequest = state.Turns[0].RequestId;
            state.Turns.RemoveAll(turn => turn.RequestId == oldestRequest);
        }
    }

    private async Task<ConversationStoreState> LoadStateWithRecoveryAsync(CancellationToken cancellationToken)
    {
        LastLoadWarning = "";
        if (!File.Exists(StorePath))
        {
            var fresh = NewState();
            await WriteAtomicallyAsync(fresh, cancellationToken).ConfigureAwait(false);
            return fresh;
        }
        try
        {
            return await ReadAsync(StorePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsInvalidStoreError(ex))
        {
            var corruptPath = PreserveInvalidFile(StorePath, "conversation.corrupt");
            var backupPath = StorePath + ".bak";
            try
            {
                if (File.Exists(backupPath))
                {
                    var recovered = await ReadAsync(backupPath, cancellationToken).ConfigureAwait(false);
                    await WriteAtomicallyAsync(recovered, cancellationToken).ConfigureAwait(false);
                    LastLoadWarning =
                        $"A conversa principal estava inválida e foi preservada como {Path.GetFileName(corruptPath)}. " +
                        "O Rota recuperou a última cópia íntegra.";
                    return recovered;
                }
            }
            catch (Exception backupError) when (IsInvalidStoreError(backupError))
            {
                PreserveInvalidFile(backupPath, "conversation.backup-corrupt");
            }

            var fresh = NewState();
            await WriteAtomicallyAsync(fresh, cancellationToken).ConfigureAwait(false);
            LastLoadWarning =
                $"A conversa local inválida foi preservada como {Path.GetFileName(corruptPath)}. " +
                "Não havia uma cópia íntegra; o Rota iniciou uma conversa vazia.";
            return fresh;
        }
    }

    private async Task<ConversationStoreState> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumFileBytes)
            throw new InvalidDataException("A conversa local excede o limite seguro de 2 MB.");
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        EnsureNoDuplicateProperties(document.RootElement);
        var state = JsonSerializer.Deserialize<ConversationStoreState>(json, _jsonOptions)
            ?? throw new InvalidDataException("A conversa local está vazia.");
        ValidateState(state);
        return state;
    }

    private async Task WriteAtomicallyAsync(ConversationStoreState state, CancellationToken cancellationToken)
    {
        ValidateState(state);
        Directory.CreateDirectory(StoreDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
        if (bytes.LongLength > MaximumFileBytes)
            throw new IOException("A conversa local excedeu o limite seguro de 2 MB.");
        var temporary = Path.Combine(StoreDirectory, $".{Path.GetFileName(StorePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(StorePath)) File.Move(temporary, StorePath);
            else File.Replace(temporary, StorePath, StorePath + ".bak", ignoreMetadataErrors: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private ConversationStoreState NewState()
    {
        var id = _newId();
        if (id == Guid.Empty)
            throw new AiContractValidationException("O gerador local não criou um ID válido para a conversa.");
        return new ConversationStoreState
        {
            ConversationId = id,
            UpdatedAtUtc = Now()
        };
    }

    private Guid NextId(ConversationStoreState state)
    {
        var id = _newId();
        if (id == Guid.Empty || id == state.ConversationId || state.Turns.Any(turn => turn.RequestId == id))
            throw new AiContractValidationException("O gerador local não criou um ID único para o pedido.");
        return id;
    }

    private DateTimeOffset Now()
    {
        var timestamp = _utcNow();
        if (timestamp == default || timestamp.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("O relógio local não forneceu um timestamp UTC válido.");
        return timestamp;
    }

    private static string ValidateAndNormalizeText(string? text)
    {
        var normalized = (text ?? "").Trim();
        if (normalized.Length is < 1 or > MaximumTurnTextLength)
            throw new AiContractValidationException("A mensagem da conversa precisa ter entre 1 e 8000 caracteres.");
        foreach (var character in normalized)
        {
            if (!char.IsControl(character) || character is '\r' or '\n' or '\t') continue;
            throw new AiContractValidationException("A mensagem da conversa contém caractere de controle não permitido.");
        }
        return normalized;
    }

    private static AiConversationSnapshot Snapshot(ConversationStoreState state) => new()
    {
        ConversationId = state.ConversationId,
        UpdatedAtUtc = state.UpdatedAtUtc,
        Turns = state.Turns.ToList()
    };

    private static void ValidateState(ConversationStoreState state)
    {
        if (state.SchemaVersion != CurrentSchemaVersion || state.ConversationId == Guid.Empty ||
            state.UpdatedAtUtc == default || state.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            state.Turns is null || state.Turns.Count > MaximumTurns)
        {
            throw new InvalidDataException("A estrutura da conversa local é inválida.");
        }

        foreach (var group in state.Turns.GroupBy(turn => turn.RequestId))
        {
            if (group.Key == Guid.Empty || group.Count(turn => turn.Role == AiConversationRole.User) != 1 ||
                group.Count(turn => turn.Role == AiConversationRole.Assistant) > 1)
            {
                throw new InvalidDataException("A conversa contém pedidos duplicados ou incompletos.");
            }
            var user = group.Single(turn => turn.Role == AiConversationRole.User);
            var assistant = group.SingleOrDefault(turn => turn.Role == AiConversationRole.Assistant);
            ValidateTurn(user);
            if (assistant is not null) ValidateTurn(assistant);
            if (user.ProposalId != Guid.Empty ||
                (assistant is null) != (user.Status != AiConversationTurnStatus.Completed) ||
                (assistant is not null && (assistant.Status != AiConversationTurnStatus.Completed ||
                                           assistant.ProposalId == Guid.Empty ||
                                           assistant.ProposalKind != user.ProposalKind)))
            {
                throw new InvalidDataException("Um pedido e sua resposta estão inconsistentes na conversa local.");
            }
        }
    }

    private static void ValidateTurn(AiConversationTurn turn)
    {
        if (!Enum.IsDefined(turn.Role) || !Enum.IsDefined(turn.Status) || !Enum.IsDefined(turn.ProposalKind) ||
            turn.CreatedAtUtc == default || turn.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("A conversa contém um turno inválido.");
        }
        ValidateAndNormalizeText(turn.Text);
    }

    private string PreserveInvalidFile(string path, string prefix)
    {
        if (!File.Exists(path)) return "";
        Directory.CreateDirectory(StoreDirectory);
        var preserved = Path.Combine(StoreDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
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
                    throw new InvalidDataException("A conversa local contém propriedades JSON duplicadas.");
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private sealed record ConversationStoreState
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;
        public Guid ConversationId { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public List<AiConversationTurn> Turns { get; init; } = new();
    }
}
