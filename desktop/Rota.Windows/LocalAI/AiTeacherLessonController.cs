namespace Rota.Desktop.LocalAI;

public sealed record AiTeacherLessonTurnResult
{
    public Guid ConversationId { get; init; }
    public Guid ExchangeId { get; init; }
    public AiTeacherGroundedAnswer GroundedAnswer { get; init; } = new();
    public AiTeacherConversationSnapshot? Conversation { get; init; }
    public string SummaryWarning { get; init; } = "";
    public string SubjectMemoryWarning { get; init; } = "";
}

/// <summary>
/// Adaptador estritamente pedagógico entre uma aula verificada e a Professora
/// Local. Histórico, resumo e memória por matéria continuam passivos. Quando uma
/// conversa idêntica é retomada, somente um recap limitado da última resposta
/// validada pode acompanhar a pergunta, sempre como dado não confiável.
/// </summary>
public sealed class AiTeacherLessonController
{
    private readonly IAiTeacherService _teacherService;
    private readonly IAiTeacherConversationStore? _conversationStore;
    private readonly IAiTeacherLessonSummaryService? _summaryService;
    private readonly AiTeacherSubjectBinding? _subjectBinding;
    private readonly IAiTeacherSubjectMemoryService? _subjectMemoryService;

    public AiTeacherLessonController(
        IAiTeacherService teacherService,
        AiTeacherLessonContext lessonContext,
        IAiTeacherConversationStore? conversationStore = null,
        IAiTeacherLessonSummaryService? summaryService = null,
        AiTeacherSubjectBinding? subjectBinding = null,
        IAiTeacherSubjectMemoryService? subjectMemoryService = null)
    {
        _teacherService = teacherService ?? throw new ArgumentNullException(nameof(teacherService));
        _conversationStore = conversationStore;
        _summaryService = summaryService;
        _subjectBinding = subjectBinding is null ? null : AiTeacherSubjectBindingFactory.Copy(subjectBinding);
        _subjectMemoryService = subjectMemoryService;
        if (_summaryService is not null && _conversationStore is null)
            throw new AiContractValidationException("O resumo automático exige um histórico local da Professora.");
        if (_subjectMemoryService is not null && _conversationStore is null)
            throw new AiContractValidationException("A memória por matéria exige um histórico local da Professora.");
        if (_subjectMemoryService is not null && _subjectBinding is null)
            throw new AiContractValidationException("A memória por matéria exige uma matéria verificada.");
        LessonContext = CopyAndValidate(lessonContext);
        if (_subjectBinding is not null)
            AiTeacherSubjectBindingFactory.Validate(_subjectBinding, LessonContext);
    }

    public AiTeacherLessonContext LessonContext { get; }

    public async Task<AiTeacherGroundedAnswer> ExplainAsync(
        string question,
        AiTeacherExplanationStyle explanationStyle,
        CancellationToken cancellationToken = default)
    {
        var turn = await AskAsync(
                Guid.Empty,
                question,
                studentAttempt: "",
                AiTeacherRequestMode.Explain,
                explanationStyle,
                cancellationToken)
            .ConfigureAwait(false);
        return turn.GroundedAnswer;
    }

    /// <summary>
    /// Executa uma troca da conversa atual. Guid.Empty começa uma nova conversa.
    /// A requisição contém pergunta/tentativa/modo/estilo e contexto congelado. Em
    /// uma conversa retomada, pode carregar somente um recap limitado da resposta
    /// anterior — nunca perguntas/tentativas antigas, memória por matéria ou texto
    /// livre de histórico.
    /// </summary>
    public async Task<AiTeacherLessonTurnResult> AskAsync(
        Guid conversationId,
        string question,
        string studentAttempt,
        AiTeacherRequestMode mode,
        AiTeacherExplanationStyle explanationStyle,
        CancellationToken cancellationToken = default)
    {
        AiTeacherConversationContinuity? continuity = null;
        if (_conversationStore is not null && conversationId != Guid.Empty)
        {
            var previous = await _conversationStore.LoadAsync(conversationId, cancellationToken)
                .ConfigureAwait(false);
            continuity = AiTeacherConversationContinuityFactory.Create(previous, LessonContext);
        }

        var request = new AiTeacherRequest
        {
            Question = (question ?? string.Empty).Trim(),
            StudentAttempt = (studentAttempt ?? string.Empty).Trim(),
            Mode = mode,
            ExplanationStyle = explanationStyle,
            LessonContext = LessonContext,
            Continuity = continuity
        };
        AiTeacherContractValidator.ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_conversationStore is null)
        {
            var unpersisted = await AskTeacherAsync(request, cancellationToken).ConfigureAwait(false);
            return new AiTeacherLessonTurnResult { GroundedAnswer = unpersisted };
        }

        var begin = await _conversationStore
            .BeginExchangeAsync(conversationId, request, cancellationToken)
            .ConfigureAwait(false);
        var bindingWarning = await TryEnsureSubjectBindingAsync(begin.Conversation, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var result = await AskTeacherAsync(request, cancellationToken).ConfigureAwait(false);
            var completed = await _conversationStore
                .CompleteExchangeAsync(begin.ConversationId, begin.ExchangeId, result, CancellationToken.None)
                .ConfigureAwait(false);
            var summaryWarning = await TryRefreshSummaryAsync(completed).ConfigureAwait(false);
            var memoryWarning = await TryRefreshSubjectMemoryAsync(completed).ConfigureAwait(false);
            if (memoryWarning.Length == 0) memoryWarning = bindingWarning;
            return new AiTeacherLessonTurnResult
            {
                ConversationId = begin.ConversationId,
                ExchangeId = begin.ExchangeId,
                GroundedAnswer = result,
                Conversation = completed,
                SummaryWarning = summaryWarning,
                SubjectMemoryWarning = memoryWarning
            };
        }
        catch (OperationCanceledException)
        {
            await TryMarkAsync(
                    begin.ConversationId,
                    begin.ExchangeId,
                    AiTeacherConversationExchangeStatus.Cancelled)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TryMarkAsync(
                    begin.ConversationId,
                    begin.ExchangeId,
                    AiTeacherConversationExchangeStatus.Failed)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<AiTeacherGroundedAnswer> AskTeacherAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _teacherService
            .ExplainWithGroundingAsync(request, cancellationToken)
            .ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(result);
        AiTeacherContractValidator.ValidateAnswer(result.Answer, AiTeacherManual.Current, request);
        AiTeacherGroundingMetadataFactory.Validate(result.Grounding, LessonContext);
        AiTeacherKnowledgeDisclosureFactory.Validate(result.Knowledge, LessonContext);
        return result;
    }

    private async Task TryMarkAsync(
        Guid conversationId,
        Guid exchangeId,
        AiTeacherConversationExchangeStatus status)
    {
        if (_conversationStore is null) return;
        try
        {
            var conversation = await _conversationStore
                .MarkExchangeAsync(conversationId, exchangeId, status, CancellationToken.None)
                .ConfigureAwait(false);
            await TryRefreshSummaryAsync(conversation).ConfigureAwait(false);
            await TryRefreshSubjectMemoryAsync(conversation).ConfigureAwait(false);
        }
        catch
        {
            // A exceção original da geração é mais importante. Pending será
            // recuperado como Failed e os sidecars podem ser reconstruídos depois.
        }
    }

    private async Task<string> TryRefreshSummaryAsync(AiTeacherConversationSnapshot conversation)
    {
        if (_summaryService is null) return "";
        try
        {
            await _summaryService.RefreshAsync(conversation, CancellationToken.None).ConfigureAwait(false);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or AiContractValidationException)
        {
            return "A conversa foi salva, mas o resumo automático não pôde ser atualizado agora.";
        }
    }

    private async Task<string> TryEnsureSubjectBindingAsync(
        AiTeacherConversationSnapshot conversation,
        CancellationToken cancellationToken)
    {
        if (_subjectMemoryService is null || _subjectBinding is null) return "";
        try
        {
            await _subjectMemoryService.EnsureBindingAsync(conversation, _subjectBinding, cancellationToken)
                .ConfigureAwait(false);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return "A conversa continua segura, mas o vínculo da memória por matéria não pôde ser salvo agora.";
        }
    }

    private async Task<string> TryRefreshSubjectMemoryAsync(AiTeacherConversationSnapshot conversation)
    {
        if (_subjectMemoryService is null || _subjectBinding is null) return "";
        try
        {
            await _subjectMemoryService.RefreshAsync(conversation, _subjectBinding, CancellationToken.None)
                .ConfigureAwait(false);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
                                   KeyNotFoundException or AiContractValidationException)
        {
            return "A conversa foi salva, mas a memória por matéria não pôde ser atualizada agora.";
        }
    }

    private static AiTeacherLessonContext CopyAndValidate(AiTeacherLessonContext source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new AiTeacherLessonContext
        {
            SchemaVersion = source.SchemaVersion,
            SourceKind = source.SourceKind,
            ContentId = source.ContentId,
            ContentTitle = source.ContentTitle,
            ContentSummary = source.ContentSummary,
            HasMorePrerequisites = source.HasMorePrerequisites,
            Prerequisites = (source.Prerequisites ?? new List<AiTeacherPrerequisiteContext>())
                .Select(item => new AiTeacherPrerequisiteContext
                {
                    ContentId = item?.ContentId ?? string.Empty,
                    Title = item?.Title ?? string.Empty
                })
                .ToList(),
            Theory = source.Theory is null ? null : new AiTeacherTheoryContext
            {
                MaterialId = source.Theory.MaterialId,
                Title = source.Theory.Title,
                LearningGoal = source.Theory.LearningGoal,
                HasMoreSections = source.Theory.HasMoreSections,
                Sections = (source.Theory.Sections ?? new List<AiTeacherTheorySectionContext>())
                    .Select(item => new AiTeacherTheorySectionContext
                    {
                        Kind = item?.Kind ?? string.Empty,
                        Title = item?.Title ?? string.Empty,
                        Body = item?.Body ?? string.Empty,
                        Position = item?.Position ?? 0
                    })
                    .ToList()
            }
        };
        AiTeacherContractValidator.ValidateLessonContext(copy);
        return copy;
    }
}
