using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public enum AiTeacherConversationExchangeStatus
{
    Pending,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// Uma troca completa da Professora Local. A tentativa do aluno, a pergunta e a
/// resposta validada são preservadas como histórico; nada desta estrutura concede
/// memória ativa ao modelo nem autoridade sobre calendário ou StudyPlan.
/// </summary>
public sealed record AiTeacherConversationExchange
{
    public Guid ExchangeId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public AiTeacherConversationExchangeStatus Status { get; init; }
    public string Question { get; init; } = "";
    public string StudentAttempt { get; init; } = "";
    public AiTeacherRequestMode Mode { get; init; } = AiTeacherRequestMode.Explain;
    public AiTeacherExplanationStyle ExplanationStyle { get; init; } = AiTeacherExplanationStyle.StepByStep;
    public AiTeacherAnswer? Answer { get; init; }
    public AiTeacherGroundingMetadata? Grounding { get; init; }
    public AiTeacherKnowledgeDisclosure? Knowledge { get; init; }
}

public sealed record AiTeacherConversationSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid ConversationId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public AiTeacherLessonContext LessonContext { get; init; } = new();
    public IReadOnlyList<AiTeacherConversationExchange> Exchanges { get; init; } = Array.Empty<AiTeacherConversationExchange>();
}

public sealed record AiTeacherConversationSummary
{
    public Guid ConversationId { get; init; }
    public string ContentId { get; init; } = "";
    public string ContentTitle { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public int ExchangeCount { get; init; }
    public int CompletedExchangeCount { get; init; }
}

public sealed record AiTeacherConversationBeginResult
{
    public Guid ConversationId { get; init; }
    public Guid ExchangeId { get; init; }
    public AiTeacherConversationSnapshot Conversation { get; init; } = new();
}

public interface IAiTeacherConversationStore
{
    string RootDirectory { get; }

    /// <summary>
    /// Guid.Empty inicia uma nova conversa. Um ID existente continua somente a
    /// conversa que tenha exatamente o mesmo contexto pedagógico congelado.
    /// </summary>
    Task<AiTeacherConversationBeginResult> BeginExchangeAsync(
        Guid conversationId,
        AiTeacherRequest request,
        CancellationToken cancellationToken = default);

    Task<AiTeacherConversationSnapshot> CompleteExchangeAsync(
        Guid conversationId,
        Guid exchangeId,
        AiTeacherGroundedAnswer result,
        CancellationToken cancellationToken = default);

    Task<AiTeacherConversationSnapshot> MarkExchangeAsync(
        Guid conversationId,
        Guid exchangeId,
        AiTeacherConversationExchangeStatus status,
        CancellationToken cancellationToken = default);

    Task<AiTeacherConversationSnapshot> LoadAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiTeacherConversationSummary>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Histórico local e passivo da Professora Local. Cada conversa ocupa seu próprio
/// arquivo em %LOCALAPPDATA%/Rota/AI/teacher/conversations. O store nunca entrega
/// histórico ao LLM: blocos posteriores podem criar memória/resumos explicitamente.
/// </summary>
public sealed class AiTeacherConversationStore : IAiTeacherConversationStore, IDisposable
{
    public const int MaximumExchangesPerConversation = 256;
    public const int MaximumConversationFiles = 4_096;
    public const long MaximumConversationFileBytes = 16L * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<Guid> _newId;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private bool _disposed;

    public AiTeacherConversationStore(
        string? rootDirectory = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<Guid>? newId = null)
    {
        var selected = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "AI",
            "teacher",
            "conversations");
        if (string.IsNullOrWhiteSpace(selected) || !Path.IsPathFullyQualified(selected))
            throw new AiContractValidationException("O diretório do histórico da Professora Local precisa ser absoluto.");

        RootDirectory = Path.GetFullPath(selected);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _newId = newId ?? Guid.NewGuid;
    }

    public string RootDirectory { get; }

    public async Task<AiTeacherConversationBeginResult> BeginExchangeAsync(
        Guid conversationId,
        AiTeacherRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        AiTeacherContractValidator.ValidateRequest(request);
        var context = request.LessonContext ??
            throw new AiContractValidationException("O histórico da Professora Local exige uma aula interna verificada.");
        AiTeacherContractValidator.ValidateLessonContext(context);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var isNew = conversationId == Guid.Empty;
            ConversationStoreState state;
            string path;
            if (isNew)
            {
                (state, path) = NewConversation(context);
            }
            else
            {
                state = await LoadStateWithRecoveryAsync(conversationId, cancellationToken).ConfigureAwait(false);
                if (!ContextEquals(state.LessonContext, context))
                    throw new AiContractValidationException("Uma conversa da Professora Local não pode trocar de conteúdo ou fonte pedagógica.");
                path = ConversationPath(conversationId);
                RecoverInterruptedExchanges(state);
            }

            if (state.Exchanges.Count >= MaximumExchangesPerConversation)
                throw new AiContractValidationException("Esta conversa atingiu o limite seguro de trocas. Inicie uma nova conversa para continuar.");
            if (state.Exchanges.Any(item => item.Status == AiTeacherConversationExchangeStatus.Pending))
                throw new AiContractValidationException("A conversa já possui uma pergunta em andamento.");

            var now = StableNow(state.UpdatedAtUtc);
            var exchangeId = NextExchangeId(state);
            state.Exchanges.Add(new AiTeacherConversationExchange
            {
                ExchangeId = exchangeId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Status = AiTeacherConversationExchangeStatus.Pending,
                Question = request.Question.Trim(),
                StudentAttempt = request.StudentAttempt.Trim(),
                Mode = request.Mode,
                ExplanationStyle = request.ExplanationStyle
            });
            state.UpdatedAtUtc = now;

            if (isNew)
                await WriteNewAsync(path, state, cancellationToken).ConfigureAwait(false);
            else
                await WriteAtomicallyAsync(path, state, cancellationToken).ConfigureAwait(false);

            return new AiTeacherConversationBeginResult
            {
                ConversationId = state.ConversationId,
                ExchangeId = exchangeId,
                Conversation = Snapshot(state)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiTeacherConversationSnapshot> CompleteExchangeAsync(
        Guid conversationId,
        Guid exchangeId,
        AiTeacherGroundedAnswer result,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIds(conversationId, exchangeId);
        ArgumentNullException.ThrowIfNull(result);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(conversationId, cancellationToken).ConfigureAwait(false);
            var index = FindExchangeIndex(state, exchangeId);
            var exchange = state.Exchanges[index];
            if (exchange.Status == AiTeacherConversationExchangeStatus.Completed)
                return Snapshot(state);
            if (exchange.Status != AiTeacherConversationExchangeStatus.Pending)
                throw new AiContractValidationException("A pergunta da conversa já foi encerrada sem resposta.");

            var request = RequestFor(state, exchange);
            AiTeacherContractValidator.ValidateAnswer(result.Answer, AiTeacherManual.Current, request);
            AiTeacherGroundingMetadataFactory.Validate(result.Grounding, state.LessonContext);
            AiTeacherKnowledgeDisclosureFactory.Validate(result.Knowledge, state.LessonContext);

            var now = StableNow(state.UpdatedAtUtc);
            state.Exchanges[index] = exchange with
            {
                Status = AiTeacherConversationExchangeStatus.Completed,
                UpdatedAtUtc = now,
                Answer = CopyAnswer(result.Answer),
                Grounding = CopyGrounding(result.Grounding),
                Knowledge = result.Knowledge with { }
            };
            state.UpdatedAtUtc = now;
            await WriteAtomicallyAsync(ConversationPath(conversationId), state, cancellationToken).ConfigureAwait(false);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiTeacherConversationSnapshot> MarkExchangeAsync(
        Guid conversationId,
        Guid exchangeId,
        AiTeacherConversationExchangeStatus status,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIds(conversationId, exchangeId);
        if (status is not AiTeacherConversationExchangeStatus.Cancelled and not AiTeacherConversationExchangeStatus.Failed)
            throw new AiContractValidationException("Somente perguntas canceladas ou com falha podem ser encerradas sem resposta.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(conversationId, cancellationToken).ConfigureAwait(false);
            var index = FindExchangeIndex(state, exchangeId);
            var exchange = state.Exchanges[index];
            if (exchange.Status == status) return Snapshot(state);
            if (exchange.Status != AiTeacherConversationExchangeStatus.Pending)
                throw new AiContractValidationException("A pergunta da conversa já foi encerrada.");

            var now = StableNow(state.UpdatedAtUtc);
            state.Exchanges[index] = exchange with
            {
                Status = status,
                UpdatedAtUtc = now,
                Answer = null,
                Grounding = null,
                Knowledge = null
            };
            state.UpdatedAtUtc = now;
            await WriteAtomicallyAsync(ConversationPath(conversationId), state, cancellationToken).ConfigureAwait(false);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiTeacherConversationSnapshot> LoadAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (conversationId == Guid.Empty)
            throw new AiContractValidationException("A conversa precisa de um ID válido.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateWithRecoveryAsync(conversationId, cancellationToken).ConfigureAwait(false);
            if (RecoverInterruptedExchanges(state))
            {
                state.UpdatedAtUtc = StableNow(state.UpdatedAtUtc);
                await WriteAtomicallyAsync(ConversationPath(conversationId), state, cancellationToken).ConfigureAwait(false);
            }
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AiTeacherConversationSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(RootDirectory)) return Array.Empty<AiTeacherConversationSummary>();
            var files = Directory.EnumerateFiles(RootDirectory, "*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count > MaximumConversationFiles)
                throw new InvalidDataException("O diretório de histórico possui conversas demais para uma leitura segura.");

            var summaries = new List<AiTeacherConversationSummary>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) || id == Guid.Empty)
                    throw new InvalidDataException("O histórico contém um arquivo de conversa com nome inválido.");

                var state = await LoadStateWithRecoveryAsync(id, cancellationToken).ConfigureAwait(false);
                if (RecoverInterruptedExchanges(state))
                {
                    state.UpdatedAtUtc = StableNow(state.UpdatedAtUtc);
                    await WriteAtomicallyAsync(ConversationPath(id), state, cancellationToken).ConfigureAwait(false);
                }
                summaries.Add(new AiTeacherConversationSummary
                {
                    ConversationId = state.ConversationId,
                    ContentId = state.LessonContext.ContentId,
                    ContentTitle = state.LessonContext.ContentTitle,
                    CreatedAtUtc = state.CreatedAtUtc,
                    UpdatedAtUtc = state.UpdatedAtUtc,
                    ExchangeCount = state.Exchanges.Count,
                    CompletedExchangeCount = state.Exchanges.Count(item => item.Status == AiTeacherConversationExchangeStatus.Completed)
                });
            }

            return summaries
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.ConversationId)
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private (ConversationStoreState State, string Path) NewConversation(AiTeacherLessonContext context)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var id = _newId();
            if (id == Guid.Empty) continue;
            var path = ConversationPath(id);
            if (File.Exists(path) || File.Exists(path + ".bak")) continue;
            var now = StableNow(default);
            return (new ConversationStoreState
            {
                ConversationId = id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LessonContext = CopyContext(context)
            }, path);
        }
        throw new AiContractValidationException("O Rota não conseguiu criar um ID único para a conversa local.");
    }

    private async Task<ConversationStoreState> LoadStateWithRecoveryAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var path = ConversationPath(conversationId);
        if (!File.Exists(path))
            throw new KeyNotFoundException("A conversa da Professora Local não foi encontrada.");

        try
        {
            return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsInvalidHistoryError(ex))
        {
            var corruptPath = PreserveInvalidFile(path);
            var backup = path + ".bak";
            if (File.Exists(backup))
            {
                try
                {
                    var recovered = await ReadAsync(backup, cancellationToken).ConfigureAwait(false);
                    await WriteNewAsync(path, recovered, cancellationToken).ConfigureAwait(false);
                    return recovered;
                }
                catch (Exception backupError) when (IsInvalidHistoryError(backupError))
                {
                    PreserveInvalidFile(backup);
                }
            }

            throw new InvalidDataException(
                $"O histórico da conversa estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. Não havia cópia íntegra para recuperação.",
                ex);
        }
    }

    private async Task<ConversationStoreState> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is < 2 or > MaximumConversationFileBytes)
            throw new InvalidDataException("O arquivo da conversa possui tamanho inválido.");

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        EnsureNoDuplicateProperties(document.RootElement);
        var state = JsonSerializer.Deserialize<ConversationStoreState>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo da conversa está vazio.");
        ValidateState(state, validateCurrentManual: false);
        return state;
    }

    private async Task WriteNewAsync(
        string path,
        ConversationStoreState state,
        CancellationToken cancellationToken)
    {
        // Cada resposta nova já foi validada contra o manual atual antes de entrar
        // no estado. Ao regravar o arquivo, valide respostas antigas como arquivo
        // histórico para que uma futura versão do manual não torne o passado ilegível.
        ValidateState(state, validateCurrentManual: false);
        Directory.CreateDirectory(RootDirectory);
        var bytes = Serialize(state);
        var temporary = TemporaryPath(path);
        try
        {
            await WriteTemporaryAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private async Task WriteAtomicallyAsync(
        string path,
        ConversationStoreState state,
        CancellationToken cancellationToken)
    {
        // A validação arquivística preserva compatibilidade entre versões do manual;
        // CompleteExchangeAsync continua sendo o gate estrito do conteúdo recém-gerado.
        ValidateState(state, validateCurrentManual: false);
        Directory.CreateDirectory(RootDirectory);
        var bytes = Serialize(state);
        var temporary = TemporaryPath(path);
        try
        {
            await WriteTemporaryAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) File.Move(temporary, path);
            else File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private byte[] Serialize(ConversationStoreState state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
        if (bytes.LongLength > MaximumConversationFileBytes)
            throw new IOException("A conversa excedeu o limite seguro de armazenamento local sem que histórico antigo fosse removido.");
        return bytes;
    }

    private static async Task WriteTemporaryAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateState(ConversationStoreState state, bool validateCurrentManual)
    {
        if (state.SchemaVersion != AiTeacherConversationSnapshot.CurrentSchemaVersion ||
            state.ConversationId == Guid.Empty ||
            state.CreatedAtUtc == default || state.CreatedAtUtc.Offset != TimeSpan.Zero ||
            state.UpdatedAtUtc == default || state.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            state.UpdatedAtUtc < state.CreatedAtUtc ||
            state.LessonContext is null ||
            state.Exchanges is null || state.Exchanges.Count > MaximumExchangesPerConversation)
        {
            throw new InvalidDataException("A estrutura do histórico da Professora Local é inválida.");
        }

        AiTeacherContractValidator.ValidateLessonContext(state.LessonContext);
        var ids = new HashSet<Guid>();
        var previousTimestamp = state.CreatedAtUtc;
        foreach (var exchange in state.Exchanges)
        {
            if (exchange is null || exchange.ExchangeId == Guid.Empty || !ids.Add(exchange.ExchangeId) ||
                exchange.CreatedAtUtc == default || exchange.CreatedAtUtc.Offset != TimeSpan.Zero ||
                exchange.UpdatedAtUtc == default || exchange.UpdatedAtUtc.Offset != TimeSpan.Zero ||
                exchange.UpdatedAtUtc < exchange.CreatedAtUtc || exchange.CreatedAtUtc < previousTimestamp ||
                !Enum.IsDefined(exchange.Status))
            {
                throw new InvalidDataException("O histórico contém uma troca inválida ou fora de ordem.");
            }

            var request = RequestFor(state, exchange);
            AiTeacherContractValidator.ValidateRequest(request);
            var hasResult = exchange.Answer is not null || exchange.Grounding is not null || exchange.Knowledge is not null;
            if (exchange.Status == AiTeacherConversationExchangeStatus.Completed)
            {
                if (exchange.Answer is null || exchange.Grounding is null || exchange.Knowledge is null)
                    throw new InvalidDataException("Uma resposta concluída está incompleta no histórico.");
                ValidateArchivedAnswer(exchange.Answer, request, validateCurrentManual);
                ValidateArchivedGrounding(exchange.Grounding, state.LessonContext, validateCurrentManual);
                ValidateArchivedKnowledge(exchange.Knowledge, state.LessonContext, validateCurrentManual);
            }
            else if (hasResult)
            {
                throw new InvalidDataException("Uma pergunta não concluída não pode carregar uma resposta arquivada.");
            }

            previousTimestamp = exchange.UpdatedAtUtc;
        }
        if (state.Exchanges.Count > 0 && state.UpdatedAtUtc < state.Exchanges[^1].UpdatedAtUtc)
            throw new InvalidDataException("A data do histórico é anterior à última troca registrada.");
    }

    private static void ValidateArchivedAnswer(
        AiTeacherAnswer answer,
        AiTeacherRequest request,
        bool validateCurrentManual)
    {
        if (validateCurrentManual)
        {
            AiTeacherContractValidator.ValidateAnswer(answer, AiTeacherManual.Current, request);
            return;
        }

        if (!string.Equals(answer.ManualId, AiTeacherManual.ExpectedManualId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(answer.ManualVersion) ||
            answer.ManualVersion.Length > 32 ||
            answer.ManualFingerprintSha256.Length != 64 ||
            !answer.ManualFingerprintSha256.All(Uri.IsHexDigit) ||
            answer.Mode != request.Mode || answer.ExplanationStyle != request.ExplanationStyle ||
            !answer.UsedLessonContext ||
            !string.Equals(answer.ContentId, request.LessonContext!.ContentId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A resposta arquivada não preserva sua proveniência pedagógica.");
        }

        ValidateArchivedText(answer.Title, 160, false);
        ValidateArchivedText(answer.Introduction, 1_200, false);
        ValidateArchivedText(answer.Recap, 1_500, false);
        if (answer.Steps is null || answer.Steps.Count is < 1 or > AiTeacherContractValidator.MaximumSteps ||
            answer.Limitations is null || answer.Limitations.Count > AiTeacherContractValidator.MaximumLimitations ||
            answer.HiddenDoubts is null || answer.HiddenDoubts.Count > AiTeacherContractValidator.MaximumHiddenDoubts)
            throw new InvalidDataException("A resposta arquivada possui estrutura pedagógica inválida.");

        for (var index = 0; index < answer.Steps.Count; index++)
        {
            var step = answer.Steps[index];
            if (step is null || step.Number != index + 1)
                throw new InvalidDataException("A resposta arquivada possui passos inválidos.");
            ValidateArchivedText(step.Title, 120, false);
            ValidateArchivedText(step.Explanation, 3_000, false);
        }
        foreach (var limitation in answer.Limitations) ValidateArchivedText(limitation, 800, false);
        foreach (var doubt in answer.HiddenDoubts)
        {
            if (doubt is null || !AiTeacherHiddenDoubtKinds.IsSupported(doubt.Kind) ||
                doubt.AddressedInStep < 1 || doubt.AddressedInStep > answer.Steps.Count)
                throw new InvalidDataException("A resposta arquivada possui uma dúvida implícita inválida.");
            ValidateArchivedText(doubt.Summary, AiTeacherContractValidator.MaximumHiddenDoubtSummaryCharacters, false);
            ValidateArchivedText(doubt.QuestionSignal, AiTeacherContractValidator.MaximumHiddenDoubtSignalCharacters, false);
            ValidateArchivedText(doubt.Reason, AiTeacherContractValidator.MaximumHiddenDoubtReasonCharacters, false);
            if (!request.Question.Contains(doubt.QuestionSignal, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A dúvida implícita arquivada perdeu sua âncora na pergunta.");
            if (doubt.PrerequisiteContentId.Length > 0 &&
                request.LessonContext.Prerequisites.All(item => item.ContentId != doubt.PrerequisiteContentId))
                throw new InvalidDataException("A dúvida implícita arquivada referencia uma fonte inexistente.");
        }

        if (answer.Mode == AiTeacherRequestMode.Explain && answer.CorrectionFeedback is not null)
            throw new InvalidDataException("Uma explicação arquivada contém feedback de correção indevido.");
        if (answer.Mode == AiTeacherRequestMode.GuidedCorrection)
        {
            var feedback = answer.CorrectionFeedback ??
                throw new InvalidDataException("Uma correção arquivada está sem feedback estruturado.");
            ValidateArchivedText(feedback.WhatIsWorking, AiTeacherContractValidator.MaximumCorrectionFeedbackCharacters, false);
            ValidateArchivedText(feedback.FirstIssue, AiTeacherContractValidator.MaximumCorrectionFeedbackCharacters, false);
            ValidateArchivedText(feedback.Hint, AiTeacherContractValidator.MaximumCorrectionFeedbackCharacters, false);
            ValidateArchivedText(feedback.NextAction, AiTeacherContractValidator.MaximumCorrectionFeedbackCharacters, false);
            if (!feedback.RequiresStudentRetry || feedback.FinalAnswerDisclosed)
                throw new InvalidDataException("Uma correção arquivada perdeu a proteção da resposta final.");
        }
    }

    private static void ValidateArchivedGrounding(
        AiTeacherGroundingMetadata grounding,
        AiTeacherLessonContext context,
        bool validateCurrent)
    {
        if (validateCurrent)
        {
            AiTeacherGroundingMetadataFactory.Validate(grounding, context);
            return;
        }
        if (grounding.Sources is null || grounding.Sources.Count is < 1 or > AiTeacherGroundingMetadataFactory.MaximumSources ||
            !Enum.IsDefined(grounding.Confidence) || string.IsNullOrWhiteSpace(grounding.ConfidenceReason) || grounding.ConfidenceReason.Length > 1_000)
            throw new InvalidDataException("A proveniência arquivada da Professora Local é inválida.");

        var allowed = new HashSet<string>(StringComparer.Ordinal) { context.ContentId };
        foreach (var prerequisite in context.Prerequisites) allowed.Add(prerequisite.ContentId);
        if (context.Theory is not null) allowed.Add(context.Theory.MaterialId);
        foreach (var source in grounding.Sources)
        {
            if (source is null || !Enum.IsDefined(source.Kind) || !allowed.Contains(source.Id) ||
                string.IsNullOrWhiteSpace(source.Title) || source.Title.Length > 240)
                throw new InvalidDataException("O histórico contém uma fonte pedagógica arquivada inválida.");
        }
    }

    private static void ValidateArchivedKnowledge(
        AiTeacherKnowledgeDisclosure knowledge,
        AiTeacherLessonContext context,
        bool validateCurrent)
    {
        if (validateCurrent)
        {
            AiTeacherKnowledgeDisclosureFactory.Validate(knowledge, context);
            return;
        }
        if (!Enum.IsDefined(knowledge.Status) || string.IsNullOrWhiteSpace(knowledge.Reason) || knowledge.Reason.Length > 1_500)
            throw new InvalidDataException("O estado de conhecimento arquivado é inválido.");
        if (knowledge.CanAnswerSubstantively != (knowledge.Status == AiTeacherKnowledgeStatus.Supported))
            throw new InvalidDataException("O estado de conhecimento arquivado é contraditório.");
    }

    private static void ValidateArchivedText(string? value, int maximum, bool allowEmpty)
    {
        if (value is null || value.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
            value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            throw new InvalidDataException("O histórico contém texto pedagógico inválido.");
    }

    private static AiTeacherRequest RequestFor(ConversationStoreState state, AiTeacherConversationExchange exchange) => new()
    {
        Question = exchange.Question,
        StudentAttempt = exchange.StudentAttempt,
        Mode = exchange.Mode,
        ExplanationStyle = exchange.ExplanationStyle,
        LessonContext = state.LessonContext
    };

    private static int FindExchangeIndex(ConversationStoreState state, Guid exchangeId)
    {
        var index = state.Exchanges.FindIndex(item => item.ExchangeId == exchangeId);
        if (index < 0) throw new KeyNotFoundException("A pergunta não existe nesta conversa local.");
        return index;
    }

    private Guid NextExchangeId(ConversationStoreState state)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var id = _newId();
            if (id != Guid.Empty && id != state.ConversationId && state.Exchanges.All(item => item.ExchangeId != id)) return id;
        }
        throw new AiContractValidationException("O Rota não conseguiu criar um ID único para a pergunta da conversa.");
    }

    private static void ValidateIds(Guid conversationId, Guid exchangeId)
    {
        if (conversationId == Guid.Empty || exchangeId == Guid.Empty)
            throw new AiContractValidationException("A conversa e a pergunta precisam de IDs válidos.");
    }

    private bool RecoverInterruptedExchanges(ConversationStoreState state)
    {
        var changed = false;
        for (var index = 0; index < state.Exchanges.Count; index++)
        {
            var item = state.Exchanges[index];
            if (item.Status != AiTeacherConversationExchangeStatus.Pending) continue;
            state.Exchanges[index] = item with
            {
                Status = AiTeacherConversationExchangeStatus.Failed,
                UpdatedAtUtc = StableNow(item.UpdatedAtUtc)
            };
            changed = true;
        }
        return changed;
    }

    private DateTimeOffset StableNow(DateTimeOffset floor)
    {
        var now = _utcNow();
        if (now == default || now.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("O relógio local não forneceu um timestamp UTC válido.");
        return floor == default || now >= floor ? now : floor;
    }

    private string ConversationPath(Guid id) => Path.Combine(RootDirectory, id.ToString("D") + ".json");

    private string TemporaryPath(string path) => Path.Combine(
        RootDirectory,
        $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static string PreserveInvalidFile(string path)
    {
        if (!File.Exists(path)) return path;
        var preserved = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        File.Move(path, preserved);
        return preserved;
    }

    private static bool IsInvalidHistoryError(Exception exception) =>
        exception is JsonException or DecoderFallbackException or InvalidDataException or AiContractValidationException;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidDataException($"O histórico contém a propriedade JSON duplicada '{property.Name}'.");
                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
                break;
        }
    }

    private static bool ContextEquals(AiTeacherLessonContext left, AiTeacherLessonContext right)
    {
        if (left.SchemaVersion != right.SchemaVersion || left.SourceKind != right.SourceKind ||
            left.ContentId != right.ContentId || left.ContentTitle != right.ContentTitle ||
            left.ContentSummary != right.ContentSummary || left.HasMorePrerequisites != right.HasMorePrerequisites ||
            left.Prerequisites.Count != right.Prerequisites.Count)
            return false;
        if (!left.Prerequisites.SequenceEqual(right.Prerequisites)) return false;
        if ((left.Theory is null) != (right.Theory is null)) return false;
        if (left.Theory is null) return true;
        var a = left.Theory;
        var b = right.Theory!;
        return a.MaterialId == b.MaterialId && a.Title == b.Title && a.LearningGoal == b.LearningGoal &&
               a.HasMoreSections == b.HasMoreSections && a.Sections.SequenceEqual(b.Sections);
    }

    private static AiTeacherLessonContext CopyContext(AiTeacherLessonContext source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        SourceKind = source.SourceKind,
        ContentId = source.ContentId,
        ContentTitle = source.ContentTitle,
        ContentSummary = source.ContentSummary,
        HasMorePrerequisites = source.HasMorePrerequisites,
        Prerequisites = source.Prerequisites.Select(item => item with { }).ToList(),
        Theory = source.Theory is null ? null : source.Theory with
        {
            Sections = source.Theory.Sections.Select(item => item with { }).ToList()
        }
    };

    private static AiTeacherAnswer CopyAnswer(AiTeacherAnswer source) => source with
    {
        Steps = source.Steps.Select(item => item with { }).ToList().AsReadOnly(),
        Limitations = source.Limitations.ToList().AsReadOnly(),
        HiddenDoubts = source.HiddenDoubts.Select(item => item with { }).ToList().AsReadOnly(),
        CorrectionFeedback = source.CorrectionFeedback is null ? null : source.CorrectionFeedback with { }
    };

    private static AiTeacherGroundingMetadata CopyGrounding(AiTeacherGroundingMetadata source) => source with
    {
        Sources = source.Sources.Select(item => item with { }).ToList().AsReadOnly()
    };

    private static AiTeacherConversationSnapshot Snapshot(ConversationStoreState state) => new()
    {
        SchemaVersion = state.SchemaVersion,
        ConversationId = state.ConversationId,
        CreatedAtUtc = state.CreatedAtUtc,
        UpdatedAtUtc = state.UpdatedAtUtc,
        LessonContext = CopyContext(state.LessonContext),
        Exchanges = state.Exchanges.Select(CopyExchange).ToList().AsReadOnly()
    };

    private static AiTeacherConversationExchange CopyExchange(AiTeacherConversationExchange source) => source with
    {
        Answer = source.Answer is null ? null : CopyAnswer(source.Answer),
        Grounding = source.Grounding is null ? null : CopyGrounding(source.Grounding),
        Knowledge = source.Knowledge is null ? null : source.Knowledge with { }
    };

    private sealed class ConversationStoreState
    {
        public int SchemaVersion { get; set; } = AiTeacherConversationSnapshot.CurrentSchemaVersion;
        public Guid ConversationId { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public AiTeacherLessonContext LessonContext { get; set; } = new();
        public List<AiTeacherConversationExchange> Exchanges { get; set; } = new();
    }
}
