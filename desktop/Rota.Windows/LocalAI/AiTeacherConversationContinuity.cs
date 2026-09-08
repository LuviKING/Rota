namespace Rota.Desktop.LocalAI;

/// <summary>
/// Recorte mínimo de continuidade para uma conversa da mesma aula congelada. Ele
/// contém somente contagem e o recap da última resposta já validada; nunca inclui
/// pergunta, tentativa, identificador de estudante, memória por matéria ou dados
/// de calendário. O backend o recebe como dado não confiável, não como instrução.
/// </summary>
public sealed record AiTeacherConversationContinuity
{
    public int CompletedExchangeCount { get; init; }
    public AiTeacherExplanationStyle LastExplanationStyle { get; init; }
    public string LastAnswerRecap { get; init; } = "";
    public bool LastAnswerRecapTruncated { get; init; }
}

public static class AiTeacherConversationContinuityFactory
{
    public const int MaximumRecapCharacters = 420;

    public static AiTeacherConversationContinuity? Create(
        AiTeacherConversationSnapshot conversation,
        AiTeacherLessonContext lessonContext)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(lessonContext);
        AiTeacherContractValidator.ValidateLessonContext(lessonContext);
        if (!ContextEquals(conversation.LessonContext, lessonContext))
            throw new AiContractValidationException("A continuidade precisa pertencer exatamente à aula pedagógica congelada.");

        var completed = conversation.Exchanges
            .Where(item => item.Status == AiTeacherConversationExchangeStatus.Completed)
            .ToList();
        if (completed.Count == 0) return null;
        var last = completed[^1];
        var answer = last.Answer ?? throw new InvalidDataException(
            "Uma troca concluída está sem resposta para a continuidade da aula.");
        var recap = Compact(answer.Recap);
        var result = new AiTeacherConversationContinuity
        {
            CompletedExchangeCount = completed.Count,
            LastExplanationStyle = last.ExplanationStyle,
            LastAnswerRecap = recap.Text,
            LastAnswerRecapTruncated = recap.Truncated
        };
        Validate(result);
        return result;
    }

    public static void Validate(AiTeacherConversationContinuity continuity)
    {
        ArgumentNullException.ThrowIfNull(continuity);
        if (continuity.CompletedExchangeCount is < 1 or > AiTeacherConversationStore.MaximumExchangesPerConversation ||
            !AiTeacherExplanationStyles.IsSupported(continuity.LastExplanationStyle) ||
            string.IsNullOrWhiteSpace(continuity.LastAnswerRecap) ||
            continuity.LastAnswerRecap.Length > MaximumRecapCharacters ||
            continuity.LastAnswerRecap.Any(char.IsControl))
        {
            throw new AiContractValidationException("A continuidade da conversa da Professora Local é inválida.");
        }
        if (continuity.LastAnswerRecapTruncated && continuity.LastAnswerRecap.Length != MaximumRecapCharacters)
            throw new AiContractValidationException("O marcador de truncamento da continuidade é inconsistente.");
    }

    private static (string Text, bool Truncated) Compact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("A última resposta não possui recap suficiente para continuidade.");
        var normalized = value.Trim();
        if (normalized.Any(char.IsControl))
            throw new InvalidDataException("O recap anterior contém caracteres inválidos.");
        return normalized.Length <= MaximumRecapCharacters
            ? (normalized, false)
            : (normalized[..MaximumRecapCharacters], true);
    }

    private static bool ContextEquals(AiTeacherLessonContext left, AiTeacherLessonContext right)
    {
        if (left.SchemaVersion != right.SchemaVersion || left.SourceKind != right.SourceKind ||
            left.ContentId != right.ContentId || left.ContentTitle != right.ContentTitle ||
            left.ContentSummary != right.ContentSummary || left.HasMorePrerequisites != right.HasMorePrerequisites ||
            left.Prerequisites.Count != right.Prerequisites.Count)
        {
            return false;
        }
        return left.Prerequisites.SequenceEqual(right.Prerequisites) &&
               ((left.Theory is null && right.Theory is null) ||
                (left.Theory is not null && right.Theory is not null &&
                 left.Theory.MaterialId == right.Theory.MaterialId &&
                 left.Theory.Title == right.Theory.Title &&
                 left.Theory.LearningGoal == right.Theory.LearningGoal &&
                 left.Theory.HasMoreSections == right.Theory.HasMoreSections &&
                 left.Theory.Sections.SequenceEqual(right.Theory.Sections)));
    }
}
