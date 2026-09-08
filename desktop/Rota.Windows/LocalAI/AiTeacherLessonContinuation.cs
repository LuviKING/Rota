namespace Rota.Desktop.LocalAI;

/// <summary>
/// Encontra, para a interface, a conversa local mais recente da mesma aula
/// congelada. A continuação apenas preserva a linha de registro e reapresenta a
/// resposta já validada ao estudante; nenhuma troca anterior é anexada ao prompt
/// ou passa a ser uma instrução para o modelo.
/// </summary>
public sealed class AiTeacherLessonContinuationService
{
    private readonly IAiTeacherConversationStore _conversationStore;

    public AiTeacherLessonContinuationService(IAiTeacherConversationStore conversationStore)
    {
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
    }

    public async Task<AiTeacherConversationSnapshot?> FindLatestAsync(
        AiTeacherLessonContext lessonContext,
        CancellationToken cancellationToken = default)
    {
        AiTeacherContractValidator.ValidateLessonContext(lessonContext);
        var summaries = await _conversationStore.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summary.ConversationId == Guid.Empty ||
                summary.ExchangeCount >= AiTeacherConversationStore.MaximumExchangesPerConversation ||
                !string.Equals(summary.ContentId, lessonContext.ContentId, StringComparison.Ordinal) ||
                !string.Equals(summary.ContentTitle, lessonContext.ContentTitle, StringComparison.Ordinal))
            {
                continue;
            }

            var conversation = await _conversationStore.LoadAsync(summary.ConversationId, cancellationToken)
                .ConfigureAwait(false);
            if (ContextEquals(conversation.LessonContext, lessonContext))
                return conversation;
        }

        return null;
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
        if (!left.Prerequisites.SequenceEqual(right.Prerequisites)) return false;
        if ((left.Theory is null) != (right.Theory is null)) return false;
        if (left.Theory is null) return true;
        var currentTheory = left.Theory;
        var expectedTheory = right.Theory!;
        return currentTheory.MaterialId == expectedTheory.MaterialId &&
               currentTheory.Title == expectedTheory.Title &&
               currentTheory.LearningGoal == expectedTheory.LearningGoal &&
               currentTheory.HasMoreSections == expectedTheory.HasMoreSections &&
               currentTheory.Sections.SequenceEqual(expectedTheory.Sections);
    }
}
