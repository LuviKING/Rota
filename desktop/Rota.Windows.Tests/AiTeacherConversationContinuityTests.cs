using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherConversationContinuityTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher continuity contains only the last validated recap", ContainsOnlyLastValidatedRecap),
        ("Teacher continuity explicitly bounds a long recap", BoundsLongRecap),
        ("Teacher continuity rejects a conversation from another frozen lesson", RejectsForeignLesson),
        ("Teacher continuity contract rejects an inconsistent truncation marker", RejectsInconsistentTruncation)
    };

    private static void ContainsOnlyLastValidatedRecap()
    {
        var context = Context();
        var conversation = Conversation(context, "Primeiro recap.", "Último recap.");
        var continuity = AiTeacherConversationContinuityFactory.Create(conversation, context);
        Require(continuity is not null);
        Require(continuity!.CompletedExchangeCount == 2);
        Require(continuity.LastAnswerRecap == "Último recap.");
        Require(continuity.LastExplanationStyle == AiTeacherExplanationStyle.Visual);
    }

    private static void BoundsLongRecap()
    {
        var context = Context();
        var continuity = AiTeacherConversationContinuityFactory.Create(
            Conversation(context, new string('a', 600)), context);
        Require(continuity is not null && continuity.LastAnswerRecap.Length == AiTeacherConversationContinuityFactory.MaximumRecapCharacters);
        Require(continuity!.LastAnswerRecapTruncated);
    }

    private static void RejectsForeignLesson()
    {
        var context = Context();
        var foreign = context with { ContentSummary = "Material diferente, apesar da mesma identidade visual." };
        Expect<AiContractValidationException>(() => AiTeacherConversationContinuityFactory.Create(
            Conversation(foreign, "Recap válido."), context));
    }

    private static void RejectsInconsistentTruncation()
    {
        Expect<AiContractValidationException>(() => AiTeacherConversationContinuityFactory.Validate(new AiTeacherConversationContinuity
        {
            CompletedExchangeCount = 1,
            LastExplanationStyle = AiTeacherExplanationStyle.Simple,
            LastAnswerRecap = "Curto.",
            LastAnswerRecapTruncated = true
        }));
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

    private static AiTeacherConversationSnapshot Conversation(AiTeacherLessonContext context, params string[] recaps) => new()
    {
        ConversationId = Guid.NewGuid(),
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        LessonContext = context,
        Exchanges = recaps.Select((recap, index) => new AiTeacherConversationExchange
        {
            ExchangeId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            Status = AiTeacherConversationExchangeStatus.Completed,
            Question = "Pergunta privada que não entra na continuidade.",
            StudentAttempt = "Tentativa privada que não entra na continuidade.",
            ExplanationStyle = index == recaps.Length - 1 ? AiTeacherExplanationStyle.Visual : AiTeacherExplanationStyle.Simple,
            Answer = new AiTeacherAnswer { Recap = recap }
        }).ToList().AsReadOnly()
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher continuity assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
