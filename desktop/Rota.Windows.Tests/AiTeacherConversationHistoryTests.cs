using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherConversationHistoryTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher conversation history persists a complete validated exchange", PersistsCompleteExchange),
        ("Teacher conversation history preserves guided correction attempt and answer safety", PersistsGuidedCorrection),
        ("Teacher conversation history continues only the exact frozen lesson", ContinuesOnlyExactLesson),
        ("Teacher conversation history lists conversations without dropping old exchanges", ListsWithoutDroppingHistory),
        ("Teacher conversation history records cancelled and failed exchanges without fabricated answers", RecordsTerminalFailures),
        ("Teacher conversation history recovers interrupted pending exchanges as failed", RecoversInterruptedExchange),
        ("Teacher conversation controller passes only bounded prior recap for a resumed lesson", ControllerPassesBoundedContinuity),
        ("Teacher conversation history rejects duplicate JSON and recovers the last valid backup", RecoversFromDuplicateJson)
    };

    private static void PersistsCompleteExchange()
    {
        WithStore((store, root) =>
        {
            var context = Context("razao", "Razão");
            var request = Request(context, "Por que uma razão compara duas quantidades?");
            var begin = store.BeginExchangeAsync(Guid.Empty, request).GetAwaiter().GetResult();
            var grounded = Grounded(request);
            var completed = store.CompleteExchangeAsync(begin.ConversationId, begin.ExchangeId, grounded)
                .GetAwaiter().GetResult();

            Require(completed.Exchanges.Count == 1);
            var exchange = completed.Exchanges.Single();
            Require(exchange.Status == AiTeacherConversationExchangeStatus.Completed);
            Require(exchange.Question == request.Question);
            Require(exchange.StudentAttempt.Length == 0);
            Require(exchange.Answer is not null && exchange.Answer.Title == grounded.Answer.Title);
            Require(exchange.Grounding is not null && exchange.Grounding.Sources.SequenceEqual(grounded.Grounding.Sources));
            Require(exchange.Knowledge == grounded.Knowledge);

            var file = Path.Combine(root, begin.ConversationId.ToString("D") + ".json");
            Require(File.Exists(file));

            using var reopened = new AiTeacherConversationStore(root, () => Timestamp, () => Guid.NewGuid());
            var loaded = reopened.LoadAsync(begin.ConversationId).GetAwaiter().GetResult();
            Require(loaded.ConversationId == begin.ConversationId);
            Require(loaded.LessonContext.ContentId == context.ContentId);
            Require(loaded.Exchanges.Count == 1);
            Require(loaded.Exchanges[0].Answer?.Recap == grounded.Answer.Recap);
        });
    }

    private static void PersistsGuidedCorrection()
    {
        WithStore((store, _) =>
        {
            var context = Context("equacao", "Equação do primeiro grau");
            var request = new AiTeacherRequest
            {
                Question = "Na equação 3x = 12, qual operação devo revisar?",
                StudentAttempt = "Eu tentei somar 3 nos dois lados.",
                Mode = AiTeacherRequestMode.GuidedCorrection,
                ExplanationStyle = AiTeacherExplanationStyle.Detailed,
                LessonContext = context
            };
            var begin = store.BeginExchangeAsync(Guid.Empty, request).GetAwaiter().GetResult();
            var completed = store.CompleteExchangeAsync(
                    begin.ConversationId,
                    begin.ExchangeId,
                    Grounded(request))
                .GetAwaiter().GetResult();

            var exchange = completed.Exchanges.Single();
            Require(exchange.StudentAttempt == request.StudentAttempt);
            Require(exchange.Mode == AiTeacherRequestMode.GuidedCorrection);
            Require(exchange.ExplanationStyle == AiTeacherExplanationStyle.Detailed);
            Require(exchange.Answer?.CorrectionFeedback is { RequiresStudentRetry: true, FinalAnswerDisclosed: false });
        });
    }

    private static void ContinuesOnlyExactLesson()
    {
        WithStore((store, _) =>
        {
            var original = Context("razao", "Razão");
            var first = store.BeginExchangeAsync(Guid.Empty, Request(original, "Explique razão."))
                .GetAwaiter().GetResult();
            store.MarkExchangeAsync(
                    first.ConversationId,
                    first.ExchangeId,
                    AiTeacherConversationExchangeStatus.Failed)
                .GetAwaiter().GetResult();

            var second = store.BeginExchangeAsync(
                    first.ConversationId,
                    Request(original, "Dê outra explicação sobre razão."))
                .GetAwaiter().GetResult();
            Require(second.ConversationId == first.ConversationId);
            Require(second.Conversation.Exchanges.Count == 2);
            store.MarkExchangeAsync(
                    second.ConversationId,
                    second.ExchangeId,
                    AiTeacherConversationExchangeStatus.Cancelled)
                .GetAwaiter().GetResult();

            Expect<AiContractValidationException>(() => store.BeginExchangeAsync(
                    first.ConversationId,
                    Request(Context("proporcao", "Proporção"), "Explique proporção."))
                .GetAwaiter().GetResult());
        });
    }

    private static void ListsWithoutDroppingHistory()
    {
        var ids = new Queue<Guid>(new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("11111111-1111-1111-1111-111111111112"),
            Guid.Parse("11111111-1111-1111-1111-111111111113"),
            Guid.Parse("22222222-2222-2222-2222-222222222221"),
            Guid.Parse("22222222-2222-2222-2222-222222222222")
        });

        WithStore((store, _) =>
        {
            var context = Context("razao", "Razão");
            var firstRequest = Request(context, "Pergunta um.");
            var first = store.BeginExchangeAsync(Guid.Empty, firstRequest).GetAwaiter().GetResult();
            store.CompleteExchangeAsync(first.ConversationId, first.ExchangeId, Grounded(firstRequest))
                .GetAwaiter().GetResult();
            var secondRequest = Request(context, "Pergunta dois.");
            var second = store.BeginExchangeAsync(first.ConversationId, secondRequest)
                .GetAwaiter().GetResult();
            store.CompleteExchangeAsync(second.ConversationId, second.ExchangeId, Grounded(secondRequest))
                .GetAwaiter().GetResult();

            var otherContext = Context("fracoes", "Frações");
            var other = store.BeginExchangeAsync(Guid.Empty, Request(otherContext, "Pergunta três."))
                .GetAwaiter().GetResult();
            store.MarkExchangeAsync(other.ConversationId, other.ExchangeId, AiTeacherConversationExchangeStatus.Failed)
                .GetAwaiter().GetResult();

            var summaries = store.ListAsync().GetAwaiter().GetResult();
            Require(summaries.Count == 2);
            var ratio = summaries.Single(item => item.ConversationId == first.ConversationId);
            Require(ratio.ExchangeCount == 2);
            Require(ratio.CompletedExchangeCount == 2);
            Require(ratio.ContentId == "razao");
            Require(store.LoadAsync(first.ConversationId).GetAwaiter().GetResult().Exchanges.Count == 2);
        }, ids);
    }

    private static void RecordsTerminalFailures()
    {
        WithStore((store, _) =>
        {
            var context = Context("razao", "Razão");
            var cancelled = store.BeginExchangeAsync(Guid.Empty, Request(context, "Pergunta cancelada."))
                .GetAwaiter().GetResult();
            var afterCancel = store.MarkExchangeAsync(
                    cancelled.ConversationId,
                    cancelled.ExchangeId,
                    AiTeacherConversationExchangeStatus.Cancelled)
                .GetAwaiter().GetResult();
            var cancelledExchange = afterCancel.Exchanges.Single();
            Require(cancelledExchange.Status == AiTeacherConversationExchangeStatus.Cancelled);
            Require(cancelledExchange.Answer is null && cancelledExchange.Grounding is null && cancelledExchange.Knowledge is null);

            var failed = store.BeginExchangeAsync(cancelled.ConversationId, Request(context, "Pergunta com falha."))
                .GetAwaiter().GetResult();
            var afterFailure = store.MarkExchangeAsync(
                    failed.ConversationId,
                    failed.ExchangeId,
                    AiTeacherConversationExchangeStatus.Failed)
                .GetAwaiter().GetResult();
            Require(afterFailure.Exchanges.Count == 2);
            Require(afterFailure.Exchanges[1].Status == AiTeacherConversationExchangeStatus.Failed);
            Require(afterFailure.Exchanges[1].Answer is null);
        });
    }

    private static void RecoversInterruptedExchange()
    {
        var directory = TempDirectory();
        try
        {
            Guid conversationId;
            using (var firstStore = Store(directory))
            {
                var begin = firstStore.BeginExchangeAsync(
                        Guid.Empty,
                        Request(Context("razao", "Razão"), "Pergunta interrompida."))
                    .GetAwaiter().GetResult();
                conversationId = begin.ConversationId;
                Require(begin.Conversation.Exchanges.Single().Status == AiTeacherConversationExchangeStatus.Pending);
            }

            using var reopened = Store(directory);
            var recovered = reopened.LoadAsync(conversationId).GetAwaiter().GetResult();
            Require(recovered.Exchanges.Single().Status == AiTeacherConversationExchangeStatus.Failed);
            Require(recovered.Exchanges.Single().Answer is null);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void ControllerPassesBoundedContinuity()
    {
        var directory = TempDirectory();
        try
        {
            var context = Context("razao", "Razão");
            using var store = Store(directory);
            var service = new RecordingTeacherService(context);
            var controller = new AiTeacherLessonController(service, context, store);

            var first = controller.AskAsync(
                    Guid.Empty,
                    "Primeira pergunta sobre razão.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.Simple)
                .GetAwaiter().GetResult();
            var second = controller.AskAsync(
                    first.ConversationId,
                    "Segunda pergunta sobre razão.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.Visual)
                .GetAwaiter().GetResult();

            Require(first.ConversationId != Guid.Empty && second.ConversationId == first.ConversationId);
            Require(second.Conversation?.Exchanges.Count == 2);
            Require(service.Requests.Count == 2);
            Require(service.Requests[0].Question == "Primeira pergunta sobre razão.");
            Require(service.Requests[1].Question == "Segunda pergunta sobre razão.");
            Require(service.Requests[1].StudentAttempt.Length == 0);
            Require(service.Requests[1].ExplanationStyle == AiTeacherExplanationStyle.Visual);
            Require(service.Requests[0].Continuity is null);
            Require(service.Requests[1].Continuity is not null);
            Require(service.Requests[1].Continuity!.CompletedExchangeCount == 1);
            Require(service.Requests[1].Continuity!.LastAnswerRecap == "Retome o conceito e faça a próxima tentativa com seu próprio raciocínio.");
            Require(!service.Requests[1].Continuity!.LastAnswerRecap.Contains("Primeira pergunta", StringComparison.Ordinal));
            Require(!ReferenceEquals(service.Requests[0].LessonContext, context));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void RecoversFromDuplicateJson()
    {
        WithStore((store, root) =>
        {
            var context = Context("razao", "Razão");
            var begin = store.BeginExchangeAsync(Guid.Empty, Request(context, "Pergunta."))
                .GetAwaiter().GetResult();
            store.MarkExchangeAsync(begin.ConversationId, begin.ExchangeId, AiTeacherConversationExchangeStatus.Failed)
                .GetAwaiter().GetResult();

            var file = Path.Combine(root, begin.ConversationId.ToString("D") + ".json");
            File.WriteAllText(file, "{\"SchemaVersion\":1,\"SchemaVersion\":1}");

            var recovered = store.LoadAsync(begin.ConversationId).GetAwaiter().GetResult();
            Require(recovered.ConversationId == begin.ConversationId);
            Require(recovered.Exchanges.Single().Status == AiTeacherConversationExchangeStatus.Failed);
            Require(Directory.EnumerateFiles(root)
                .Any(path => Path.GetFileName(path).StartsWith(begin.ConversationId.ToString("D") + ".json.corrupt-", StringComparison.Ordinal)));
        });
    }

    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 16, 0, 0, TimeSpan.Zero);

    private static AiTeacherRequest Request(AiTeacherLessonContext context, string question) => new()
    {
        Question = question,
        Mode = AiTeacherRequestMode.Explain,
        ExplanationStyle = AiTeacherExplanationStyle.StepByStep,
        LessonContext = context
    };

    private static AiTeacherGroundedAnswer Grounded(AiTeacherRequest request)
    {
        var context = request.LessonContext ?? throw new InvalidOperationException("Expected lesson context.");
        var answer = new AiTeacherAnswer
        {
            Title = "Explicação validada",
            Introduction = "Vamos usar somente o material interno verificado deste conteúdo.",
            Steps = new[]
            {
                new AiTeacherStep
                {
                    Number = 1,
                    Title = "Identifique a relação",
                    Explanation = "Compare as quantidades e preserve a operação descrita pelo material da aula."
                }
            },
            Recap = "Retome o conceito e faça a próxima tentativa com seu próprio raciocínio.",
            Limitations = Array.Empty<string>(),
            HiddenDoubts = Array.Empty<AiTeacherHiddenDoubt>(),
            CorrectionFeedback = request.Mode == AiTeacherRequestMode.GuidedCorrection
                ? new AiTeacherCorrectionFeedback
                {
                    WhatIsWorking = "Você identificou que precisa transformar a expressão.",
                    FirstIssue = "A operação escolhida ainda não desfaz a relação indicada no passo atual.",
                    Hint = "Procure a operação inversa sem concluir o exercício inteiro.",
                    NextAction = "Refaça somente o próximo passo e tente novamente.",
                    RequiresStudentRetry = true,
                    FinalAnswerDisclosed = false
                }
                : null,
            Mode = request.Mode,
            ExplanationStyle = request.ExplanationStyle,
            ManualId = AiTeacherManual.Current.ManualId,
            ManualVersion = AiTeacherManual.Current.ManualVersion,
            ManualFingerprintSha256 = AiTeacherManual.Current.FingerprintSha256,
            UsedLessonContext = true,
            ContentId = context.ContentId
        };
        AiTeacherContractValidator.ValidateAnswer(answer, AiTeacherManual.Current, request);
        return new AiTeacherGroundedAnswer
        {
            Answer = answer,
            Grounding = AiTeacherGroundingMetadataFactory.Create(context),
            Knowledge = AiTeacherKnowledgeDisclosureFactory.Create(context)
        };
    }

    private static AiTeacherLessonContext Context(string contentId, string title) => new()
    {
        ContentId = contentId,
        ContentTitle = title,
        ContentSummary = $"Resumo verificado de {title}.",
        Prerequisites = new()
        {
            new AiTeacherPrerequisiteContext
            {
                ContentId = contentId + "-base",
                Title = "Base de " + title
            }
        },
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-" + contentId,
            Title = "Teoria de " + title,
            LearningGoal = "Compreender o conceito de " + title + ".",
            Sections = new()
            {
                new AiTeacherTheorySectionContext
                {
                    Kind = LearningTheorySectionKinds.Explanation,
                    Title = "Conceito",
                    Body = "Material interno verificado usado exclusivamente para esta aula.",
                    Position = 1
                }
            }
        }
    };

    private static AiTeacherConversationStore Store(string directory) =>
        new(directory, () => Timestamp, () => Guid.NewGuid());

    private static void WithStore(
        Action<AiTeacherConversationStore, string> body,
        Queue<Guid>? ids = null)
    {
        var directory = TempDirectory();
        try
        {
            Guid NewId() => ids is { Count: > 0 } ? ids.Dequeue() : Guid.NewGuid();
            using var store = new AiTeacherConversationStore(directory, () => Timestamp, NewId);
            body(store, directory);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RotaTeacherConversationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher conversation history assertion failed.");
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

        public List<AiTeacherRequest> Requests { get; } = new();

        public Task<AiTeacherAnswer> ExplainAsync(
            AiTeacherRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Grounded(request).Answer);

        public Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
            AiTeacherRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Require(request.LessonContext?.ContentId == _context.ContentId);
            return Task.FromResult(Grounded(request));
        }
    }
}
