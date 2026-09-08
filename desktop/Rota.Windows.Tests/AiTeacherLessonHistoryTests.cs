using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherLessonHistoryTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher lesson history keeps the local question and validated recap", ShowsLocalTranscript),
        ("Teacher lesson history limits the displayed exchanges", BoundsDisplay),
        ("Teacher lesson history labels terminal states without a fabricated answer", LabelsTerminalStates),
        ("Teacher lesson history rejects a different frozen lesson", RejectsForeignLesson)
    };

    private static void ShowsLocalTranscript()
    {
        var context = Context();
        var result = AiTeacherLessonHistoryFactory.Create(Conversation(context, Completed("Pergunta privada.", "Resposta validada.")), context);
        var entry = result.Entries.Single();
        Require(entry.StatusLabel == "Respondida");
        Require(entry.Question == "Pergunta privada.");
        Require(entry.AnswerTitle == "Explicação validada");
        Require(entry.AnswerRecap == "Resposta validada.");
        Require(entry.EvidenceSummary == "Base: Material interno suficiente · Cobertura: Alta");
    }

    private static void BoundsDisplay()
    {
        var context = Context();
        var exchanges = Enumerable.Range(1, 24)
            .Select(index => Completed("Pergunta " + index + ".", "Recap " + index + "."))
            .ToArray();
        var result = AiTeacherLessonHistoryFactory.Create(Conversation(context, exchanges), context);
        Require(result.TotalExchangeCount == 24);
        Require(result.HasEarlierEntries && result.Entries.Count == AiTeacherLessonHistoryFactory.MaximumDisplayedExchanges);
        Require(result.Entries.First().Question == "Pergunta 5.");
    }

    private static void LabelsTerminalStates()
    {
        var context = Context();
        var failed = new AiTeacherConversationExchange
        {
            ExchangeId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            Status = AiTeacherConversationExchangeStatus.Failed,
            Question = "Pergunta sem resposta."
        };
        var result = AiTeacherLessonHistoryFactory.Create(Conversation(context, failed), context);
        Require(result.Entries.Single().StatusLabel == "Sem resposta");
        Require(result.Entries.Single().AnswerTitle.Length == 0 && result.Entries.Single().AnswerRecap.Length == 0);
        Require(result.Entries.Single().EvidenceSummary.Length == 0);
    }

    private static void RejectsForeignLesson()
    {
        var context = Context();
        var other = context with { ContentSummary = "Outro material pedagógico." };
        Expect<AiContractValidationException>(() => AiTeacherLessonHistoryFactory.Create(
            Conversation(other, Completed("Pergunta.", "Recap.")), context));
    }

    private static AiTeacherLessonContext Context() => new()
    {
        ContentId = "razao",
        ContentTitle = "Razão",
        ContentSummary = "Comparação entre duas quantidades.",
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-razao",
            Title = "Razão",
            LearningGoal = "Compreender a razão.",
            Sections = new()
            {
                new AiTeacherTheorySectionContext
                {
                    Kind = LearningTheorySectionKinds.Explanation,
                    Title = "Conceito",
                    Body = "Uma razão compara duas quantidades por meio de divisão.",
                    Position = 1
                }
            }
        }
    };

    private static AiTeacherConversationSnapshot Conversation(AiTeacherLessonContext context, params AiTeacherConversationExchange[] exchanges) => new()
    {
        ConversationId = Guid.NewGuid(),
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        LessonContext = context,
        Exchanges = exchanges.ToList().AsReadOnly()
    };

    private static AiTeacherConversationExchange Completed(string question, string recap) => new()
    {
        ExchangeId = Guid.NewGuid(),
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        Status = AiTeacherConversationExchangeStatus.Completed,
        Question = question,
        Answer = new AiTeacherAnswer { Title = "Explicação validada", Recap = recap },
        Grounding = AiTeacherGroundingMetadataFactory.Create(Context()),
        Knowledge = AiTeacherKnowledgeDisclosureFactory.Create(Context())
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher lesson history assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
