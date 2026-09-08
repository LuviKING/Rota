using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherLessonControllerTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Lesson teacher binds each question to a frozen verified lesson", BindsQuestionToFrozenLesson),
        ("Lesson teacher rejects an invalid question before calling a service", RejectsInvalidQuestionBeforeService),
        ("Lesson teacher rejects forged grounding before presentation", RejectsForgedGrounding),
        ("Lesson teacher rejects forged knowledge status before presentation", RejectsForgedKnowledge),
        ("Lesson teacher honors cancellation before a local request", HonorsCancellation)
    };

    private static void BindsQuestionToFrozenLesson()
    {
        var source = Context();
        var service = new RecordingTeacherService(source);
        var controller = new AiTeacherLessonController(service, source);
        var theory = source.Theory ?? throw new InvalidOperationException("Expected theory context.");
        theory.Sections[0] = theory.Sections[0] with { Title = "Texto adulterado" };

        var result = controller.ExplainAsync("  Explique razão.  ", AiTeacherExplanationStyle.Visual)
            .GetAwaiter().GetResult();

        Require(result.Answer.Title == "Explicação segura");
        var request = service.Request ?? throw new InvalidOperationException("Expected teacher request.");
        Require(request.Question == "Explique razão.");
        Require(request.ExplanationStyle == AiTeacherExplanationStyle.Visual);
        var requestContext = request.LessonContext ?? throw new InvalidOperationException("Expected lesson context.");
        Require(requestContext.ContentId == "razao");
        Require(requestContext.Theory!.Sections[0].Title == "Conceito");
        Require(!ReferenceEquals(requestContext, source));
    }

    private static void RejectsInvalidQuestionBeforeService()
    {
        var context = Context();
        var service = new RecordingTeacherService(context);
        var controller = new AiTeacherLessonController(service, context);

        Expect<AiContractValidationException>(() => controller.ExplainAsync("   ", AiTeacherExplanationStyle.Simple)
            .GetAwaiter().GetResult());
        Require(service.CallCount == 0);
    }

    private static void RejectsForgedGrounding()
    {
        var context = Context() with { Theory = null };
        var service = new RecordingTeacherService(context)
        {
            ResultOverride = Answer(context) with
            {
                Grounding = AiTeacherGroundingMetadataFactory.Create(Context())
            }
        };
        var controller = new AiTeacherLessonController(service, context);

        Expect<AiContractValidationException>(() => controller.ExplainAsync("Explique razão.", AiTeacherExplanationStyle.StepByStep)
            .GetAwaiter().GetResult());
    }

    private static void RejectsForgedKnowledge()
    {
        var context = Context();
        var service = new RecordingTeacherService(context)
        {
            ResultOverride = Answer(context) with
            {
                Knowledge = AiTeacherKnowledgeDisclosureFactory.Create(context with { Theory = null })
            }
        };
        var controller = new AiTeacherLessonController(service, context);

        Expect<AiContractValidationException>(() => controller.ExplainAsync("Explique razão.", AiTeacherExplanationStyle.StepByStep)
            .GetAwaiter().GetResult());
    }

    private static void HonorsCancellation()
    {
        var context = Context();
        var service = new RecordingTeacherService(context);
        var controller = new AiTeacherLessonController(service, context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Expect<OperationCanceledException>(() => controller.ExplainAsync(
            "Explique razão.",
            AiTeacherExplanationStyle.StepByStep,
            cancellation.Token).GetAwaiter().GetResult());
        Require(service.CallCount == 0);
    }

    private static AiTeacherGroundedAnswer Answer(AiTeacherLessonContext context) => new()
    {
        Answer = new AiTeacherAnswer
        {
            Title = "Explicação segura",
            Introduction = "Vamos usar somente o material desta aula.",
            Steps = new[]
            {
                new AiTeacherStep { Number = 1, Title = "Compare", Explanation = "Razão compara quantidades." }
            },
            Recap = "Compare as quantidades pelo material da aula.",
            ManualId = AiTeacherManual.Current.ManualId,
            ManualVersion = AiTeacherManual.Current.ManualVersion,
            ManualFingerprintSha256 = AiTeacherManual.Current.FingerprintSha256,
            UsedLessonContext = true,
            ContentId = context.ContentId
        },
        Grounding = AiTeacherGroundingMetadataFactory.Create(context),
        Knowledge = AiTeacherKnowledgeDisclosureFactory.Create(context)
    };

    private static AiTeacherLessonContext Context() => new()
    {
        ContentId = "razao",
        ContentTitle = "Razão",
        ContentSummary = "Comparação entre duas quantidades.",
        Prerequisites = new()
        {
            new AiTeacherPrerequisiteContext { ContentId = "fracoes", Title = "Frações" }
        },
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-razao",
            Title = "Razão no pacote",
            LearningGoal = "Compreender razão como comparação.",
            Sections = new()
            {
                new AiTeacherTheorySectionContext
                {
                    Kind = LearningTheorySectionKinds.Explanation,
                    Title = "Conceito",
                    Body = "Uma razão compara duas quantidades por meio de uma divisão.",
                    Position = 1
                }
            }
        }
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher lesson controller assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class RecordingTeacherService : IAiTeacherService
    {
        private readonly AiTeacherLessonContext _context;

        public RecordingTeacherService(AiTeacherLessonContext context) => _context = context;

        public int CallCount { get; private set; }
        public AiTeacherRequest? Request { get; private set; }
        public AiTeacherGroundedAnswer? ResultOverride { get; init; }

        public Task<AiTeacherAnswer> ExplainAsync(
            AiTeacherRequest request,
            CancellationToken cancellationToken = default) =>
            ExplainWithGroundingAsync(request, cancellationToken).ContinueWith(
                task => task.GetAwaiter().GetResult().Answer,
                cancellationToken);

        public Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
            AiTeacherRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Request = request;
            var result = ResultOverride ?? Answer(_context);
            return Task.FromResult(result with
            {
                Answer = result.Answer with
                {
                    Mode = request.Mode,
                    ExplanationStyle = request.ExplanationStyle
                }
            });
        }
    }
}
