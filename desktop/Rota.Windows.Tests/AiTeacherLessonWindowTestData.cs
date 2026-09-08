using Rota.Desktop.LocalAI;

internal sealed class WindowNoOpTeacherService : IAiTeacherService
{
    public Task<AiTeacherAnswer> ExplainAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AiTeacherAnswer>(
            new AiInferenceException("A janela de teste não executa inferência local."));

    public Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AiTeacherGroundedAnswer>(
            new AiInferenceException("A janela de teste não executa inferência local."));
}

internal static class AiTeacherLessonWindowTestData
{
    public static AiTeacherLessonContext CreateContext() => new()
    {
        ContentId = "proporcionalidade",
        ContentTitle = "Razão e proporção",
        ContentSummary = "Comparação entre duas quantidades.",
        Prerequisites = new()
        {
            new AiTeacherPrerequisiteContext { ContentId = "operacoes", Title = "Operações básicas" }
        },
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-proporcionalidade",
            Title = "Razão e proporção no pacote",
            LearningGoal = "Resolver situações simples de proporcionalidade.",
            Sections = new()
            {
                new AiTeacherTheorySectionContext
                {
                    Kind = "explanation",
                    Title = "Ideia central",
                    Body = "Razões equivalentes mantêm a mesma relação.",
                    Position = 1
                },
                new AiTeacherTheorySectionContext
                {
                    Kind = "worked_example",
                    Title = "Exemplo",
                    Body = "Se 2 cadernos custam 10 reais, 4 cadernos custam 20 reais.",
                    Position = 2
                }
            }
        }
    };
}
