using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherLessonSummaryTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher lesson summary is deterministic and derived from completed history", SummaryIsDeterministic),
        ("Teacher lesson summary truncation is explicit and preserves full history", TruncationIsExplicit),
        ("Teacher lesson summary store persists strict local sidecars", StorePersists),
        ("Teacher lesson summary store recovers the last valid backup", StoreRecoversBackup),
        ("Teacher lesson summary service rebuilds missing and stale sidecars", ServiceRebuildsDerivedCache),
        ("Teacher controller refreshes the automatic summary after a successful turn", ControllerRefreshesSummary),
        ("Teacher controller never feeds summaries or prior turns back to the teacher service", ControllerKeepsSummaryPassive),
        ("Teacher summary failure cannot erase a successfully persisted answer", SummaryFailureIsNonFatal)
    };

    private static void SummaryIsDeterministic()
    {
        WithConversation((conversationStore, summaryStore, _) =>
        {
            var context = Context();
            var request = Request(context, "Por que uma razão compara duas quantidades?");
            var begin = conversationStore.BeginExchangeAsync(Guid.Empty, request).GetAwaiter().GetResult();
            var completed = conversationStore.CompleteExchangeAsync(
                    begin.ConversationId,
                    begin.ExchangeId,
                    Grounded(request))
                .GetAwaiter().GetResult();

            var first = AiTeacherLessonSummaryFactory.Create(completed);
            var second = AiTeacherLessonSummaryFactory.Create(completed);
            Require(AiTeacherLessonSummaryFactory.Equivalent(first, second));
            Require(first.ConversationId == completed.ConversationId);
            Require(first.ContentId == context.ContentId);
            Require(first.SourceExchangeCount == 1);
            Require(first.CompletedExchangeCount == 1);
            Require(first.UnansweredExchangeCount == 0);
            Require(first.Exchanges.Single().Question == request.Question);
            Require(first.Exchanges.Single().AnswerTitle == "Explicação validada");
            Require(first.Exchanges.Single().KnowledgeStatus == AiTeacherKnowledgeStatus.Supported);
            Require(first.Exchanges.Single().GroundingConfidence == AiTeacherGroundingConfidence.High);
            Require(!Directory.Exists(summaryStore.RootDirectory));
        });
    }

    private static void TruncationIsExplicit()
    {
        WithConversation((conversationStore, _, _) =>
        {
            var context = Context();
            var question = new string('q', AiTeacherLessonSummaryFactory.MaximumQuestionCharacters + 20);
            var request = Request(context, question);
            var begin = conversationStore.BeginExchangeAsync(Guid.Empty, request).GetAwaiter().GetResult();
            var grounded = Grounded(request) with
            {
                Answer = Grounded(request).Answer with
                {
                    Recap = new string('r', AiTeacherLessonSummaryFactory.MaximumRecapCharacters + 30)
                }
            };
            AiTeacherContractValidator.ValidateAnswer(grounded.Answer, AiTeacherManual.Current, request);
            var completed = conversationStore.CompleteExchangeAsync(begin.ConversationId, begin.ExchangeId, grounded)
                .GetAwaiter().GetResult();

            var summary = AiTeacherLessonSummaryFactory.Create(completed);
            var exchange = summary.Exchanges.Single();
            Require(exchange.QuestionTruncated);
            Require(exchange.Question.Length == AiTeacherLessonSummaryFactory.MaximumQuestionCharacters);
            Require(exchange.RecapTruncated);
            Require(exchange.Recap.Length == AiTeacherLessonSummaryFactory.MaximumRecapCharacters);
            Require(completed.Exchanges.Single().Question.Length == question.Length);
            Require(completed.Exchanges.Single().Answer!.Recap.Length > exchange.Recap.Length);
        });
    }

    private static void StorePersists()
    {
        WithConversation((conversationStore, summaryStore, _) =>
        {
            var completed = CompletedConversation(conversationStore, "Explique razão.");
            var summary = AiTeacherLessonSummaryFactory.Create(completed);
            summaryStore.SaveAsync(summary).GetAwaiter().GetResult();

            var loaded = summaryStore.TryLoadAsync(summary.ConversationId).GetAwaiter().GetResult();
            Require(loaded is not null);
            Require(AiTeacherLessonSummaryFactory.Equivalent(summary, loaded!));
            Require(File.Exists(Path.Combine(summaryStore.RootDirectory, summary.ConversationId.ToString("D") + ".json")));
        });
    }

    private static void StoreRecoversBackup()
    {
        WithConversation((conversationStore, summaryStore, _) =>
        {
            var firstConversation = CompletedConversation(conversationStore, "Primeira pergunta.");
            var first = AiTeacherLessonSummaryFactory.Create(firstConversation);
            summaryStore.SaveAsync(first).GetAwaiter().GetResult();

            var secondRequest = Request(firstConversation.LessonContext, "Segunda pergunta.");
            var secondBegin = conversationStore.BeginExchangeAsync(first.ConversationId, secondRequest)
                .GetAwaiter().GetResult();
            var secondConversation = conversationStore.CompleteExchangeAsync(
                    first.ConversationId,
                    secondBegin.ExchangeId,
                    Grounded(secondRequest))
                .GetAwaiter().GetResult();
            var second = AiTeacherLessonSummaryFactory.Create(secondConversation);
            summaryStore.SaveAsync(second).GetAwaiter().GetResult();

            var path = Path.Combine(summaryStore.RootDirectory, first.ConversationId.ToString("D") + ".json");
            Require(File.Exists(path + ".bak"));
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"SchemaVersion\":1}");

            var recovered = summaryStore.TryLoadAsync(first.ConversationId).GetAwaiter().GetResult();
            Require(recovered is not null);
            Require(AiTeacherLessonSummaryFactory.Equivalent(first, recovered!));
            Require(Directory.EnumerateFiles(summaryStore.RootDirectory)
                .Any(item => Path.GetFileName(item).StartsWith(first.ConversationId.ToString("D") + ".json.corrupt-", StringComparison.Ordinal)));
        });
    }

    private static void ServiceRebuildsDerivedCache()
    {
        WithConversation((conversationStore, summaryStore, _) =>
        {
            var completed = CompletedConversation(conversationStore, "Explique razão de outro jeito.");
            var service = new AiTeacherLessonSummaryService(conversationStore, summaryStore);

            var rebuiltMissing = service.GetAsync(completed.ConversationId).GetAwaiter().GetResult();
            Require(rebuiltMissing.CompletedExchangeCount == 1);

            var stale = rebuiltMissing with
            {
                SourceExchangeCount = 0,
                CompletedExchangeCount = 0,
                UnansweredExchangeCount = 0,
                Exchanges = Array.Empty<AiTeacherLessonSummaryExchange>()
            };
            summaryStore.SaveAsync(stale).GetAwaiter().GetResult();

            var repaired = service.GetAsync(completed.ConversationId).GetAwaiter().GetResult();
            Require(AiTeacherLessonSummaryFactory.Equivalent(repaired, AiTeacherLessonSummaryFactory.Create(completed)));
            Require(repaired.CompletedExchangeCount == 1);
        });
    }

    private static void ControllerRefreshesSummary()
    {
        WithConversation((conversationStore, summaryStore, _) =>
        {
            var context = Context();
            var teacher = new RecordingTeacherService();
            var summaries = new AiTeacherLessonSummaryService(conversationStore, summaryStore);
            var controller = new AiTeacherLessonController(teacher, context, conversationStore, summaries);

            var turn = controller.AskAsync(
                    Guid.Empty,
                    "Explique razão.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.StepByStep)
                .GetAwaiter().GetResult();

            Require(turn.ConversationId != Guid.Empty);
            Require(turn.SummaryWarning.Length == 0);
            var summary = summaryStore.TryLoadAsync(turn.ConversationId).GetAwaiter().GetResult();
            Require(summary is not null && summary.CompletedExchangeCount == 1);
            Require(summary!.Exchanges.Single().Question == "Explique razão.");
        });
    }

    private static void ControllerKeepsSummaryPassive()
    {
        WithConversation((conversationStore, summaryStore, _) =>
        {
            var context = Context();
            var teacher = new RecordingTeacherService();
            var summaries = new AiTeacherLessonSummaryService(conversationStore, summaryStore);
            var controller = new AiTeacherLessonController(teacher, context, conversationStore, summaries);

            var first = controller.AskAsync(
                    Guid.Empty,
                    "Primeira pergunta sobre razão.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.Simple)
                .GetAwaiter().GetResult();
            controller.AskAsync(
                    first.ConversationId,
                    "Segunda pergunta independente.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.Visual)
                .GetAwaiter().GetResult();

            Require(teacher.Requests.Count == 2);
            Require(teacher.Requests[0].Question == "Primeira pergunta sobre razão.");
            Require(teacher.Requests[1].Question == "Segunda pergunta independente.");
            Require(!teacher.Requests[1].Question.Contains("Primeira", StringComparison.Ordinal));
            Require(teacher.Requests[1].StudentAttempt.Length == 0);
            Require(teacher.Requests[1].LessonContext?.ContentId == context.ContentId);
        });
    }

    private static void SummaryFailureIsNonFatal()
    {
        var directory = TempDirectory();
        try
        {
            using var conversationStore = new AiTeacherConversationStore(
                Path.Combine(directory, "conversations"), () => Timestamp, () => Guid.NewGuid());
            var teacher = new RecordingTeacherService();
            var controller = new AiTeacherLessonController(
                teacher,
                Context(),
                conversationStore,
                new FailingSummaryService());

            var turn = controller.AskAsync(
                    Guid.Empty,
                    "Explique razão apesar da falha do cache.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.StepByStep)
                .GetAwaiter().GetResult();

            Require(turn.SummaryWarning.Length > 0);
            var persisted = conversationStore.LoadAsync(turn.ConversationId).GetAwaiter().GetResult();
            Require(persisted.Exchanges.Single().Status == AiTeacherConversationExchangeStatus.Completed);
            Require(persisted.Exchanges.Single().Answer?.Title == "Explicação validada");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static AiTeacherConversationSnapshot CompletedConversation(
        AiTeacherConversationStore store,
        string question)
    {
        var context = Context();
        var request = Request(context, question);
        var begin = store.BeginExchangeAsync(Guid.Empty, request).GetAwaiter().GetResult();
        return store.CompleteExchangeAsync(begin.ConversationId, begin.ExchangeId, Grounded(request))
            .GetAwaiter().GetResult();
    }

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

    private static AiTeacherLessonContext Context() => new()
    {
        ContentId = "razao",
        ContentTitle = "Razão",
        ContentSummary = "Resumo verificado de Razão.",
        Prerequisites = new()
        {
            new AiTeacherPrerequisiteContext
            {
                ContentId = "operacoes-basicas",
                Title = "Operações básicas"
            }
        },
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-razao",
            Title = "Teoria de Razão",
            LearningGoal = "Compreender o conceito de razão.",
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

    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 18, 0, 0, TimeSpan.Zero);

    private static void WithConversation(Action<AiTeacherConversationStore, AiTeacherLessonSummaryStore, string> body)
    {
        var directory = TempDirectory();
        try
        {
            using var conversationStore = new AiTeacherConversationStore(
                Path.Combine(directory, "conversations"), () => Timestamp, () => Guid.NewGuid());
            using var summaryStore = new AiTeacherLessonSummaryStore(Path.Combine(directory, "summaries"));
            body(conversationStore, summaryStore, directory);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RotaTeacherSummaryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher lesson summary assertion failed.");
    }

    private sealed class RecordingTeacherService : IAiTeacherService
    {
        public List<AiTeacherRequest> Requests { get; } = new();

        public Task<AiTeacherAnswer> ExplainAsync(AiTeacherRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExplainWithGroundingAsync(request, cancellationToken).GetAwaiter().GetResult().Answer);

        public Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
            AiTeacherRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request with
            {
                LessonContext = request.LessonContext is null ? null : request.LessonContext with
                {
                    Prerequisites = request.LessonContext.Prerequisites.Select(item => item with { }).ToList(),
                    Theory = request.LessonContext.Theory is null ? null : request.LessonContext.Theory with
                    {
                        Sections = request.LessonContext.Theory.Sections.Select(item => item with { }).ToList()
                    }
                }
            });
            return Task.FromResult(Grounded(request));
        }
    }

    private sealed class FailingSummaryService : IAiTeacherLessonSummaryService
    {
        public Task<AiTeacherLessonSummarySnapshot> RefreshAsync(
            AiTeacherConversationSnapshot conversation,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada no cache derivado.");

        public Task<AiTeacherLessonSummarySnapshot> GetAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada no cache derivado.");
    }
}
