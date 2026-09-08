using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherLessonContinuationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 22, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher continuation resumes the latest exact frozen lesson", ResumesLatestExactLesson),
        ("Teacher continuation never crosses a changed frozen context", RejectsChangedFrozenContext),
        ("Teacher continuation skips a conversation at the safe turn cap", SkipsConversationAtTurnCap),
        ("Teacher continuation reads local history without a model", ReadsOnlyConversationStore)
    };

    private static void ResumesLatestExactLesson()
    {
        var context = Context("razao", "Razão");
        var older = Conversation("00000000-0000-0000-0000-000000000101", context, Timestamp);
        var latest = Conversation("00000000-0000-0000-0000-000000000102", context, Timestamp.AddMinutes(1));
        var store = new FakeConversationStore(older, latest);

        var found = new AiTeacherLessonContinuationService(store).FindLatestAsync(context).GetAwaiter().GetResult();
        Require(found?.ConversationId == latest.ConversationId);
        Require(store.LoadedIds.SequenceEqual(new[] { latest.ConversationId }));
    }

    private static void RejectsChangedFrozenContext()
    {
        var expected = Context("razao", "Razão");
        var changed = expected with { ContentSummary = "Outro material interno, ainda que com o mesmo título." };
        var store = new FakeConversationStore(Conversation("00000000-0000-0000-0000-000000000111", changed, Timestamp));

        var found = new AiTeacherLessonContinuationService(store).FindLatestAsync(expected).GetAwaiter().GetResult();
        Require(found is null);
        Require(store.LoadedIds.Count == 1);
    }

    private static void SkipsConversationAtTurnCap()
    {
        var context = Context("razao", "Razão");
        var full = Conversation("00000000-0000-0000-0000-000000000121", context, Timestamp,
            AiTeacherConversationStore.MaximumExchangesPerConversation);
        var store = new FakeConversationStore(full);

        var found = new AiTeacherLessonContinuationService(store).FindLatestAsync(context).GetAwaiter().GetResult();
        Require(found is null);
        Require(store.LoadedIds.Count == 0);
    }

    private static void ReadsOnlyConversationStore()
    {
        var context = Context("razao", "Razão");
        var store = new FakeConversationStore(Conversation("00000000-0000-0000-0000-000000000131", context, Timestamp));
        var service = new AiTeacherLessonContinuationService(store);

        _ = service.FindLatestAsync(context).GetAwaiter().GetResult();
        Require(store.ListCalls == 1 && store.LoadCalls == 1);
        Require(store.BeginCalls == 0 && store.CompleteCalls == 0 && store.MarkCalls == 0);
    }

    private static AiTeacherLessonContext Context(string contentId, string title) => new()
    {
        ContentId = contentId,
        ContentTitle = title,
        ContentSummary = "Resumo verificado de " + title + ".",
        Prerequisites = new()
        {
            new AiTeacherPrerequisiteContext { ContentId = contentId + "-base", Title = "Base de " + title }
        },
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-" + contentId,
            Title = "Teoria de " + title,
            LearningGoal = "Compreender " + title + ".",
            Sections = new()
            {
                new AiTeacherTheorySectionContext
                {
                    Kind = LearningTheorySectionKinds.Explanation,
                    Title = "Conceito",
                    Body = "Material pedagógico interno verificado.",
                    Position = 1
                }
            }
        }
    };

    private static AiTeacherConversationSnapshot Conversation(
        string id,
        AiTeacherLessonContext context,
        DateTimeOffset updatedAt,
        int exchangeCount = 1) => new()
    {
        ConversationId = Guid.Parse(id),
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = updatedAt,
        LessonContext = context,
        Exchanges = Enumerable.Range(1, exchangeCount).Select(index => new AiTeacherConversationExchange
        {
            ExchangeId = Guid.NewGuid(),
            CreatedAtUtc = updatedAt,
            UpdatedAtUtc = updatedAt,
            Status = AiTeacherConversationExchangeStatus.Failed,
            Question = "Pergunta arquivada."
        }).ToList().AsReadOnly()
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher lesson continuation assertion failed.");
    }

    private sealed class FakeConversationStore : IAiTeacherConversationStore
    {
        private readonly IReadOnlyList<AiTeacherConversationSnapshot> _conversations;

        public FakeConversationStore(params AiTeacherConversationSnapshot[] conversations) => _conversations = conversations;
        public string RootDirectory => Path.GetTempPath();
        public int ListCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public int BeginCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int MarkCalls { get; private set; }
        public List<Guid> LoadedIds { get; } = new();

        public Task<IReadOnlyList<AiTeacherConversationSummary>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<AiTeacherConversationSummary>>(_conversations
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Select(item => new AiTeacherConversationSummary
                {
                    ConversationId = item.ConversationId,
                    ContentId = item.LessonContext.ContentId,
                    ContentTitle = item.LessonContext.ContentTitle,
                    CreatedAtUtc = item.CreatedAtUtc,
                    UpdatedAtUtc = item.UpdatedAtUtc,
                    ExchangeCount = item.Exchanges.Count,
                    CompletedExchangeCount = item.Exchanges.Count(exchange => exchange.Status == AiTeacherConversationExchangeStatus.Completed)
                }).ToList().AsReadOnly());
        }

        public Task<AiTeacherConversationSnapshot> LoadAsync(Guid conversationId, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            LoadedIds.Add(conversationId);
            return Task.FromResult(_conversations.Single(item => item.ConversationId == conversationId));
        }

        public Task<AiTeacherConversationBeginResult> BeginExchangeAsync(Guid conversationId, AiTeacherRequest request, CancellationToken cancellationToken = default)
        {
            BeginCalls++;
            throw new NotSupportedException();
        }

        public Task<AiTeacherConversationSnapshot> CompleteExchangeAsync(Guid conversationId, Guid exchangeId, AiTeacherGroundedAnswer result, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            throw new NotSupportedException();
        }

        public Task<AiTeacherConversationSnapshot> MarkExchangeAsync(Guid conversationId, Guid exchangeId, AiTeacherConversationExchangeStatus status, CancellationToken cancellationToken = default)
        {
            MarkCalls++;
            throw new NotSupportedException();
        }
    }
}
