namespace Rota.Desktop.LocalAI;

/// <summary>
/// Adaptador estritamente pedagógico entre uma aula verificada e a Professora
/// Local. Ele congela o contexto selecionado pelo Rota, não conhece o calendário
/// e não oferece operações de planejamento ou escrita.
/// </summary>
public sealed class AiTeacherLessonController
{
    private readonly IAiTeacherService _teacherService;

    public AiTeacherLessonController(
        IAiTeacherService teacherService,
        AiTeacherLessonContext lessonContext)
    {
        _teacherService = teacherService ?? throw new ArgumentNullException(nameof(teacherService));
        LessonContext = CopyAndValidate(lessonContext);
    }

    /// <summary>
    /// Fotografia imutável da aula aberta. A pergunta pode variar, mas a fonte
    /// usada pela professora permanece a mesma até a pessoa selecionar outra aula.
    /// </summary>
    public AiTeacherLessonContext LessonContext { get; }

    public async Task<AiTeacherGroundedAnswer> ExplainAsync(
        string question,
        AiTeacherExplanationStyle explanationStyle,
        CancellationToken cancellationToken = default)
    {
        var request = new AiTeacherRequest
        {
            Question = (question ?? string.Empty).Trim(),
            ExplanationStyle = explanationStyle,
            LessonContext = LessonContext
        };
        AiTeacherContractValidator.ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _teacherService
            .ExplainWithGroundingAsync(request, cancellationToken)
            .ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(result);
        AiTeacherContractValidator.ValidateAnswer(result.Answer, AiTeacherManual.Current, request);
        AiTeacherGroundingMetadataFactory.Validate(result.Grounding, LessonContext);
        AiTeacherKnowledgeDisclosureFactory.Validate(result.Knowledge, LessonContext);
        return result;
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
