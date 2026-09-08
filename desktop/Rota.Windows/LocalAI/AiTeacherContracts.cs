namespace Rota.Desktop.LocalAI;

/// <summary>
/// Pedido pedagógico isolado do fluxo de planejamento. O texto do aluno e todo
/// conteúdo recuperado são dados para a aula, nunca autoridade operacional.
/// </summary>
public sealed record AiTeacherRequest
{
    public string Question { get; init; } = "";
    public string StudentAttempt { get; init; } = "";
    public AiTeacherRequestMode Mode { get; init; } = AiTeacherRequestMode.Explain;
    public AiTeacherExplanationStyle ExplanationStyle { get; init; } = AiTeacherExplanationStyle.StepByStep;
    public AiTeacherLessonContext? LessonContext { get; init; }
    public AiTeacherConversationContinuity? Continuity { get; init; }
}

/// <summary>
/// Contexto pedagógico mínimo derivado de um guia do Rota. Não possui questões,
/// alternativas, gabaritos, estado do calendário, caminhos de arquivos ou banco.
/// </summary>
public sealed record AiTeacherLessonContext
{
    public const int CurrentSchemaVersion = 1;
    public const string LearningPackageSource = "learning_package";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string SourceKind { get; init; } = LearningPackageSource;
    public string ContentId { get; init; } = "";
    public string ContentTitle { get; init; } = "";
    public string ContentSummary { get; init; } = "";
    public List<AiTeacherPrerequisiteContext> Prerequisites { get; init; } = new();
    public bool HasMorePrerequisites { get; init; }
    public AiTeacherTheoryContext? Theory { get; init; }
}

public sealed record AiTeacherPrerequisiteContext
{
    public string ContentId { get; init; } = "";
    public string Title { get; init; } = "";
}

public sealed record AiTeacherTheoryContext
{
    public string MaterialId { get; init; } = "";
    public string Title { get; init; } = "";
    public string LearningGoal { get; init; } = "";
    public List<AiTeacherTheorySectionContext> Sections { get; init; } = new();
    public bool HasMoreSections { get; init; }
}

public sealed record AiTeacherTheorySectionContext
{
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public int Position { get; init; }
}

/// <summary>
/// Resposta pedagógica estruturada. Metadados de manual, estilo e contexto são
/// preenchidos pelo Rota após a geração, e não aceitos como afirmações do modelo.
/// Possíveis dúvidas implícitas continuam vinculadas à pergunta atual e não formam
/// um perfil persistente do aluno. CorrectionFeedback só existe em correção guiada.
/// </summary>
public sealed record AiTeacherAnswer
{
    public string Title { get; init; } = "";
    public string Introduction { get; init; } = "";
    public IReadOnlyList<AiTeacherStep> Steps { get; init; } = Array.Empty<AiTeacherStep>();
    public string Recap { get; init; } = "";
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AiTeacherHiddenDoubt> HiddenDoubts { get; init; } = Array.Empty<AiTeacherHiddenDoubt>();
    public AiTeacherCorrectionFeedback? CorrectionFeedback { get; init; }
    public AiTeacherRequestMode Mode { get; init; } = AiTeacherRequestMode.Explain;
    public AiTeacherExplanationStyle ExplanationStyle { get; init; } = AiTeacherExplanationStyle.StepByStep;
    public string ManualId { get; init; } = "";
    public string ManualVersion { get; init; } = "";
    public string ManualFingerprintSha256 { get; init; } = "";
    public bool UsedLessonContext { get; init; }
    public string ContentId { get; init; } = "";
}

public sealed record AiTeacherStep
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Explanation { get; init; } = "";
}

/// <summary>
/// Hipótese pedagógica estritamente local à pergunta atual. QuestionSignal precisa
/// ser um trecho literal da pergunta enviada; quando há referência a pré-requisito,
/// o ID precisa existir no contexto verificado fornecido pelo Rota.
/// </summary>
public sealed record AiTeacherHiddenDoubt
{
    public AiTeacherHiddenDoubtKind Kind { get; init; }
    public string Summary { get; init; } = "";
    public string QuestionSignal { get; init; } = "";
    public string Reason { get; init; } = "";
    public string PrerequisiteContentId { get; init; } = "";
    public int AddressedInStep { get; init; }
}

public interface IAiTeacherBackend
{
    Task<AiTeacherAnswer> ExplainAsync(
        AiTeacherRequest request,
        AiConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public interface IAiTeacherService
{
    Task<AiTeacherAnswer> ExplainAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Caminho destinado à interface pedagógica. Além do texto da professora,
    /// devolve a proveniência e o limite de conhecimento determinados pelo Rota.
    /// </summary>
    Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Validação determinística das fronteiras da professora. Esta camada existe para
/// que o modelo nunca seja a autoridade sobre tamanho, estrutura ou proveniência.
/// </summary>
public static class AiTeacherContractValidator
{
    public const int MaximumQuestionCharacters = 4_000;
    public const int MaximumStudentAttemptCharacters = 4_000;
    public const int MaximumPrerequisites = 12;
    public const int MaximumTheorySections = 12;
    public const int MaximumLessonContextCharacters = 20_000;
    public const int MaximumAnswerCharacters = 20_000;
    public const int MaximumSteps = 8;
    public const int MaximumLimitations = 8;
    public const int MaximumHiddenDoubts = 3;
    public const int MaximumHiddenDoubtSummaryCharacters = 200;
    public const int MaximumHiddenDoubtSignalCharacters = 240;
    public const int MaximumHiddenDoubtReasonCharacters = 600;
    public const int MaximumCorrectionFeedbackCharacters = 1_200;

    public static void ValidateRequest(AiTeacherRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateText(request.Question, "pergunta da aula", MaximumQuestionCharacters, allowEmpty: false, allowLineBreaks: true);
        ValidateRequestMode(request.Mode);
        ValidateExplanationStyle(request.ExplanationStyle);
        ValidateText(request.StudentAttempt, "tentativa do aluno", MaximumStudentAttemptCharacters, allowEmpty: true, allowLineBreaks: true);

        if (request.Mode == AiTeacherRequestMode.GuidedCorrection && string.IsNullOrWhiteSpace(request.StudentAttempt))
            throw new AiContractValidationException("A correção guiada exige uma tentativa do aluno.");
        if (request.Mode == AiTeacherRequestMode.Explain && request.StudentAttempt.Length > 0)
            throw new AiContractValidationException("Uma explicação comum não pode carregar uma tentativa de correção.");

        if (request.LessonContext is not null)
            ValidateLessonContext(request.LessonContext);
        if (request.Continuity is not null)
            AiTeacherConversationContinuityFactory.Validate(request.Continuity);
    }

    public static void ValidateLessonContext(AiTeacherLessonContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SchemaVersion != AiTeacherLessonContext.CurrentSchemaVersion)
            throw new AiContractValidationException("A versão do contexto pedagógico não é suportada.");
        if (!string.Equals(context.SourceKind, AiTeacherLessonContext.LearningPackageSource, StringComparison.Ordinal))
            throw new AiContractValidationException("A origem do contexto pedagógico não é reconhecida.");
        ValidateId(context.ContentId, "conteúdo da aula");
        ValidateText(context.ContentTitle, "título do conteúdo", 160, allowEmpty: false);
        ValidateText(context.ContentSummary, "resumo do conteúdo", 2_000, allowEmpty: true);

        if (context.Prerequisites is null || context.Prerequisites.Count > MaximumPrerequisites)
            throw new AiContractValidationException("O contexto pedagógico possui pré-requisitos demais.");
        var prerequisiteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prerequisite in context.Prerequisites)
        {
            if (prerequisite is null)
                throw new AiContractValidationException("O contexto pedagógico contém um pré-requisito nulo.");
            ValidateId(prerequisite.ContentId, "pré-requisito");
            if (prerequisite.ContentId == context.ContentId || !prerequisiteIds.Add(prerequisite.ContentId))
                throw new AiContractValidationException("O contexto pedagógico possui pré-requisitos inválidos ou duplicados.");
            ValidateText(prerequisite.Title, "título do pré-requisito", 160, allowEmpty: false);
        }

        if (context.Theory is not null)
            ValidateTheory(context.Theory);

        var characters = context.ContentId.Length + context.ContentTitle.Length + context.ContentSummary.Length +
            context.Prerequisites.Sum(item => item.ContentId.Length + item.Title.Length);
        if (context.Theory is { } theory)
        {
            characters += theory.MaterialId.Length + theory.Title.Length + theory.LearningGoal.Length;
            characters += theory.Sections.Sum(section => section.Kind.Length + section.Title.Length + section.Body.Length);
        }
        if (characters > MaximumLessonContextCharacters)
            throw new AiContractValidationException("O contexto pedagógico excede o limite seguro da professora local.");
    }

    public static void ValidateAnswer(
        AiTeacherAnswer answer,
        AiTeacherManual manual,
        AiTeacherRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(manual);
        if (request is not null)
            ValidateRequest(request);

        ValidateText(answer.Title, "título da explicação", 160, allowEmpty: false, allowLineBreaks: true);
        ValidateText(answer.Introduction, "introdução da explicação", 1_200, allowEmpty: false, allowLineBreaks: true);
        ValidateText(answer.Recap, "resumo da explicação", 1_500, allowEmpty: false, allowLineBreaks: true);
        ValidateRequestMode(answer.Mode);
        ValidateExplanationStyle(answer.ExplanationStyle);

        if (answer.Steps is null || answer.Steps.Count is < 1 or > MaximumSteps)
            throw new AiContractValidationException("A explicação precisa conter entre um e oito passos.");
        for (var index = 0; index < answer.Steps.Count; index++)
        {
            var step = answer.Steps[index] ?? throw new AiContractValidationException("A explicação contém um passo nulo.");
            if (step.Number != index + 1)
                throw new AiContractValidationException("Os passos da explicação precisam ser numerados continuamente a partir de um.");
            ValidateText(step.Title, "título do passo", 120, allowEmpty: false, allowLineBreaks: true);
            ValidateText(step.Explanation, "texto do passo", 3_000, allowEmpty: false, allowLineBreaks: true);
        }

        if (answer.Limitations is null || answer.Limitations.Count > MaximumLimitations)
            throw new AiContractValidationException("A explicação contém limitações demais.");
        foreach (var limitation in answer.Limitations)
            ValidateText(limitation, "limitação da explicação", 800, allowEmpty: false, allowLineBreaks: true);

        ValidateHiddenDoubts(answer, request);
        ValidateCorrectionFeedback(answer, request);

        if (!string.Equals(answer.ManualId, manual.ManualId, StringComparison.Ordinal) ||
            !string.Equals(answer.ManualVersion, manual.ManualVersion, StringComparison.Ordinal) ||
            !string.Equals(answer.ManualFingerprintSha256, manual.FingerprintSha256, StringComparison.Ordinal))
        {
            throw new AiContractValidationException("A explicação não está vinculada ao manual pedagógico canônico.");
        }
        if (answer.UsedLessonContext)
            ValidateId(answer.ContentId, "conteúdo usado pela professora");
        else if (answer.ContentId.Length != 0)
            throw new AiContractValidationException("Uma explicação sem contexto não pode declarar um conteúdo de origem.");

        if (request is not null)
        {
            if (answer.Mode != request.Mode)
                throw new AiContractValidationException("O modo registrado na resposta não corresponde ao pedido validado pelo Rota.");
            if (answer.ExplanationStyle != request.ExplanationStyle)
                throw new AiContractValidationException("O estilo registrado na resposta não corresponde ao pedido validado pelo Rota.");

            var expectedContext = request.LessonContext;
            if (answer.UsedLessonContext != (expectedContext is not null))
                throw new AiContractValidationException("A proveniência pedagógica da resposta não corresponde ao contexto do pedido.");
            if (expectedContext is not null && !string.Equals(answer.ContentId, expectedContext.ContentId, StringComparison.Ordinal))
                throw new AiContractValidationException("O conteúdo registrado na resposta não corresponde ao contexto pedagógico usado.");
        }

        var characters = answer.Title.Length + answer.Introduction.Length + answer.Recap.Length +
            answer.Steps.Sum(step => step.Title.Length + step.Explanation.Length) +
            answer.Limitations.Sum(item => item.Length) +
            answer.HiddenDoubts.Sum(item =>
                item.Summary.Length + item.QuestionSignal.Length + item.Reason.Length + item.PrerequisiteContentId.Length);
        if (answer.CorrectionFeedback is { } correction)
        {
            characters += correction.WhatIsWorking.Length + correction.FirstIssue.Length +
                correction.Hint.Length + correction.NextAction.Length;
        }
        if (characters > MaximumAnswerCharacters)
            throw new AiContractValidationException("A explicação excede o limite seguro de tamanho.");
    }

    private static void ValidateHiddenDoubts(AiTeacherAnswer answer, AiTeacherRequest? request)
    {
        if (answer.HiddenDoubts is null || answer.HiddenDoubts.Count > MaximumHiddenDoubts)
            throw new AiContractValidationException("A explicação contém dúvidas implícitas demais.");
        if (answer.HiddenDoubts.Count == 0)
            return;
        if (request is null)
            throw new AiContractValidationException("Dúvidas implícitas só podem ser validadas contra a pergunta que as originou.");

        var normalizedQuestion = NormalizeNewLines(request.Question);
        var summaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doubt in answer.HiddenDoubts)
        {
            if (doubt is null)
                throw new AiContractValidationException("A explicação contém uma dúvida implícita nula.");
            if (!AiTeacherHiddenDoubtKinds.IsSupported(doubt.Kind))
                throw new AiContractValidationException("A explicação contém uma categoria de dúvida implícita não suportada.");

            ValidateText(doubt.Summary, "resumo da dúvida implícita", MaximumHiddenDoubtSummaryCharacters, allowEmpty: false, allowLineBreaks: true);
            ValidateText(doubt.QuestionSignal, "sinal da pergunta", MaximumHiddenDoubtSignalCharacters, allowEmpty: false, allowLineBreaks: true);
            ValidateText(doubt.Reason, "justificativa da dúvida implícita", MaximumHiddenDoubtReasonCharacters, allowEmpty: false, allowLineBreaks: true);
            ValidateText(doubt.PrerequisiteContentId, "pré-requisito da dúvida implícita", 96, allowEmpty: true);

            if (!normalizedQuestion.Contains(NormalizeNewLines(doubt.QuestionSignal), StringComparison.OrdinalIgnoreCase))
                throw new AiContractValidationException("Uma dúvida implícita não está ancorada em um trecho literal da pergunta do aluno.");
            if (doubt.AddressedInStep < 1 || doubt.AddressedInStep > answer.Steps.Count)
                throw new AiContractValidationException("Uma dúvida implícita aponta para um passo inexistente da explicação.");
            if (!summaries.Add(doubt.Summary.Trim()))
                throw new AiContractValidationException("A explicação contém dúvidas implícitas duplicadas.");

            if (doubt.PrerequisiteContentId.Length > 0)
            {
                ValidateId(doubt.PrerequisiteContentId, "pré-requisito da dúvida implícita");
                var belongsToContext = request.LessonContext?.Prerequisites.Any(item =>
                    string.Equals(item.ContentId, doubt.PrerequisiteContentId, StringComparison.Ordinal)) == true;
                if (!belongsToContext)
                    throw new AiContractValidationException("Uma dúvida implícita referencia um pré-requisito que não foi fornecido pelo Rota.");
            }
        }
    }

    private static void ValidateCorrectionFeedback(AiTeacherAnswer answer, AiTeacherRequest? request)
    {
        if (answer.Mode == AiTeacherRequestMode.Explain)
        {
            if (answer.CorrectionFeedback is not null)
                throw new AiContractValidationException("Uma explicação comum não pode declarar metadados de correção guiada.");
            return;
        }

        if (answer.Mode != AiTeacherRequestMode.GuidedCorrection)
            throw new AiContractValidationException("O modo de correção da resposta não é suportado.");
        if (request is null)
            throw new AiContractValidationException("Uma correção guiada só pode ser validada contra a tentativa que a originou.");
        if (request.Mode != AiTeacherRequestMode.GuidedCorrection)
            throw new AiContractValidationException("A resposta de correção guiada não corresponde ao pedido original.");

        var correction = answer.CorrectionFeedback ??
            throw new AiContractValidationException("A correção guiada precisa conter feedback estruturado.");
        ValidateText(correction.WhatIsWorking, "ponto correto da tentativa", MaximumCorrectionFeedbackCharacters, allowEmpty: false, allowLineBreaks: true);
        ValidateText(correction.FirstIssue, "primeiro ponto a corrigir", MaximumCorrectionFeedbackCharacters, allowEmpty: false, allowLineBreaks: true);
        ValidateText(correction.Hint, "dica da correção", MaximumCorrectionFeedbackCharacters, allowEmpty: false, allowLineBreaks: true);
        ValidateText(correction.NextAction, "próxima ação da correção", MaximumCorrectionFeedbackCharacters, allowEmpty: false, allowLineBreaks: true);
        if (!correction.RequiresStudentRetry)
            throw new AiContractValidationException("A correção guiada precisa terminar solicitando uma nova tentativa do aluno.");
        if (correction.FinalAnswerDisclosed)
            throw new AiContractValidationException("A correção guiada não pode declarar que entregou a resposta final.");
    }

    private static void ValidateTheory(AiTeacherTheoryContext theory)
    {
        ValidateId(theory.MaterialId, "material teórico");
        ValidateText(theory.Title, "título do material teórico", 160, allowEmpty: false);
        ValidateText(theory.LearningGoal, "objetivo de aprendizagem", 600, allowEmpty: false);
        if (theory.Sections is null || theory.Sections.Count is < 1 or > MaximumTheorySections)
            throw new AiContractValidationException("O contexto teórico precisa conter entre uma e doze seções.");

        var positions = new HashSet<int>();
        foreach (var section in theory.Sections)
        {
            if (section is null || !LearningTheorySectionKinds.IsValid(section.Kind))
                throw new AiContractValidationException("O contexto teórico possui um tipo de seção inválido.");
            if (section.Position is < 1 or > 30 || !positions.Add(section.Position))
                throw new AiContractValidationException("O contexto teórico possui posições inválidas ou duplicadas.");
            ValidateText(section.Title, "título da seção teórica", 160, allowEmpty: false);
            ValidateText(section.Body, "texto da seção teórica", 6_000, allowEmpty: false);
        }
    }

    private static void ValidateRequestMode(AiTeacherRequestMode mode)
    {
        if (!AiTeacherRequestModes.IsSupported(mode))
            throw new AiContractValidationException("O modo pedagógico não é suportado pela professora local.");
    }

    private static void ValidateExplanationStyle(AiTeacherExplanationStyle style)
    {
        if (!AiTeacherExplanationStyles.IsSupported(style))
            throw new AiContractValidationException("O estilo de explicação não é suportado pela professora local.");
    }

    private static void ValidateId(string? value, string field)
    {
        if (!LearningCatalogIds.IsValid(value))
            throw new AiContractValidationException($"O identificador de {field} é inválido.");
    }

    private static void ValidateText(
        string? value,
        string field,
        int maximum,
        bool allowEmpty,
        bool allowLineBreaks = false)
    {
        if (value is null || value.Length > maximum ||
            (!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
            value.Any(character => char.IsControl(character) &&
                !(allowLineBreaks && character is '\r' or '\n' or '\t')))
        {
            throw new AiContractValidationException($"O campo {field} é inválido.");
        }
    }

    private static string NormalizeNewLines(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
}
