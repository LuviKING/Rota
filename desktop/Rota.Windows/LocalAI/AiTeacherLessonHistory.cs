namespace Rota.Desktop.LocalAI;

/// <summary>
/// Transcrição de leitura para a interface da Professora Local. Ela vem somente
/// do histórico local da mesma aula congelada, é limitada para a tela e nunca é
/// enviada ao modelo como contexto, memória ou instrução.
/// </summary>
public sealed record AiTeacherLessonHistoryEntry
{
    public Guid ExchangeId { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public AiTeacherConversationExchangeStatus Status { get; init; }
    public string StatusLabel { get; init; } = "";
    public string Question { get; init; } = "";
    public string AnswerTitle { get; init; } = "";
    public string AnswerRecap { get; init; } = "";
}

public sealed record AiTeacherLessonHistorySnapshot
{
    public Guid ConversationId { get; init; }
    public int TotalExchangeCount { get; init; }
    public bool HasEarlierEntries { get; init; }
    public IReadOnlyList<AiTeacherLessonHistoryEntry> Entries { get; init; } =
        Array.Empty<AiTeacherLessonHistoryEntry>();
}

public static class AiTeacherLessonHistoryFactory
{
    public const int MaximumDisplayedExchanges = 20;

    public static AiTeacherLessonHistorySnapshot Create(
        AiTeacherConversationSnapshot conversation,
        AiTeacherLessonContext lessonContext)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(lessonContext);
        AiTeacherContractValidator.ValidateLessonContext(lessonContext);
        if (conversation.SchemaVersion != AiTeacherConversationSnapshot.CurrentSchemaVersion ||
            conversation.ConversationId == Guid.Empty || conversation.Exchanges is null ||
            conversation.Exchanges.Count > AiTeacherConversationStore.MaximumExchangesPerConversation ||
            !ContextEquals(conversation.LessonContext, lessonContext))
        {
            throw new AiContractValidationException("O histórico não pertence à aula pedagógica atual.");
        }

        var entries = conversation.Exchanges
            .TakeLast(MaximumDisplayedExchanges)
            .Select(CreateEntry)
            .ToList()
            .AsReadOnly();
        return new AiTeacherLessonHistorySnapshot
        {
            ConversationId = conversation.ConversationId,
            TotalExchangeCount = conversation.Exchanges.Count,
            HasEarlierEntries = conversation.Exchanges.Count > entries.Count,
            Entries = entries
        };
    }

    private static AiTeacherLessonHistoryEntry CreateEntry(AiTeacherConversationExchange exchange)
    {
        if (exchange is null || exchange.ExchangeId == Guid.Empty || !Enum.IsDefined(exchange.Status) ||
            string.IsNullOrWhiteSpace(exchange.Question))
        {
            throw new InvalidDataException("O histórico da aula contém uma troca inválida.");
        }

        var answer = exchange.Status == AiTeacherConversationExchangeStatus.Completed
            ? exchange.Answer ?? throw new InvalidDataException("Uma troca concluída está sem resposta no histórico.")
            : null;
        return new AiTeacherLessonHistoryEntry
        {
            ExchangeId = exchange.ExchangeId,
            UpdatedAtUtc = exchange.UpdatedAtUtc,
            Status = exchange.Status,
            StatusLabel = StatusLabel(exchange.Status),
            Question = exchange.Question,
            AnswerTitle = answer?.Title ?? "",
            AnswerRecap = answer?.Recap ?? ""
        };
    }

    private static string StatusLabel(AiTeacherConversationExchangeStatus status) => status switch
    {
        AiTeacherConversationExchangeStatus.Completed => "Respondida",
        AiTeacherConversationExchangeStatus.Cancelled => "Cancelada",
        AiTeacherConversationExchangeStatus.Failed => "Sem resposta",
        AiTeacherConversationExchangeStatus.Pending => "Em andamento",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "O estado da conversa não é suportado.")
    };

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
