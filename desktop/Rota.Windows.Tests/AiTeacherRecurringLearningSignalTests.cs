using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherRecurringLearningSignalTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher recurring signals require separate local conversations", RequiresSeparateConversations),
        ("Teacher recurring signals ignore incomplete exchanges", IgnoresIncompleteExchanges),
        ("Teacher recurring signals stay isolated by verified subject", KeepsSubjectsSeparate),
        ("Teacher recurring signals expose no student text", DoesNotExposeStudentText),
        ("Teacher recurring signal service reads bindings and history without a model", ServiceReadsOnlyLocalSources),
        ("Teacher recurring signals reject a source from another subject", RejectsForeignSource)
    };

    private static void RequiresSeparateConversations()
    {
        var subject = Binding("razao", "Razão");
        var result = AiTeacherRecurringLearningSignalFactory.Build(subject,
        [
            Source(Conversation(Guid.Parse("00000000-0000-0000-0000-000000000011"), "Razão", new[]
            {
                Completed(AiTeacherHiddenDoubtKind.ConceptConfusion),
                Completed(AiTeacherHiddenDoubtKind.ConceptConfusion)
            }), subject),
            Source(Conversation(Guid.Parse("00000000-0000-0000-0000-000000000012"), "Proporção", new[]
            {
                Completed(AiTeacherHiddenDoubtKind.ConceptConfusion),
                Completed(AiTeacherHiddenDoubtKind.ProceduralReasoning)
            }), Binding("proporcao", "Proporção"))
        ]);

        Require(result.ConversationsScanned == 2);
        Require(result.CompletedExchangesScanned == 4);
        Require(result.Signals.Count == 1);
        var signal = result.Signals.Single();
        Require(signal.Kind == AiTeacherHiddenDoubtKind.ConceptConfusion);
        Require(signal.ConversationCount == 2);
        Require(signal.ExchangeCount == 3);
        Require(signal.RecentContentTitles.SequenceEqual(new[] { "Razão", "Proporção" }));
    }

    private static void IgnoresIncompleteExchanges()
    {
        var subject = Binding("razao", "Razão");
        var first = Conversation(Guid.Parse("00000000-0000-0000-0000-000000000021"), "Razão", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.Prerequisite),
            Incomplete(AiTeacherConversationExchangeStatus.Failed)
        });
        var second = Conversation(Guid.Parse("00000000-0000-0000-0000-000000000022"), "Razão", new[]
        {
            Incomplete(AiTeacherConversationExchangeStatus.Cancelled)
        });

        var result = AiTeacherRecurringLearningSignalFactory.Build(subject, [Source(first, subject), Source(second, subject)]);
        Require(result.CompletedExchangesScanned == 1);
        Require(result.Signals.Count == 0);
    }

    private static void KeepsSubjectsSeparate()
    {
        var math = Binding("razao", "Razão", subjectId: "matematica", subjectName: "Matemática");
        var physics = Binding("cinematica", "Cinemática", subjectId: "fisica", subjectName: "Física");
        var mathSource = Source(Conversation(Guid.Parse("00000000-0000-0000-0000-000000000031"), "Razão", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.NotationOrVocabulary)
        }), math);
        var physicsSource = Source(Conversation(Guid.Parse("00000000-0000-0000-0000-000000000032"), "Cinemática", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.NotationOrVocabulary)
        }), physics);

        var mathResult = AiTeacherRecurringLearningSignalFactory.Build(math, [mathSource]);
        var physicsResult = AiTeacherRecurringLearningSignalFactory.Build(physics, [physicsSource]);
        Require(mathResult.SubjectId == "matematica" && mathResult.Signals.Count == 0);
        Require(physicsResult.SubjectId == "fisica" && physicsResult.Signals.Count == 0);
    }

    private static void DoesNotExposeStudentText()
    {
        const string privateQuestion = "Minha pergunta particular não deve aparecer no sinal.";
        const string privateAttempt = "Minha tentativa particular não deve aparecer no sinal.";
        var subject = Binding("razao", "Razão");
        var first = Conversation(Guid.Parse("00000000-0000-0000-0000-000000000041"), "Razão", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.ProceduralReasoning, privateQuestion, privateAttempt)
        });
        var second = Conversation(Guid.Parse("00000000-0000-0000-0000-000000000042"), "Razão", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.ProceduralReasoning, privateQuestion, privateAttempt)
        });

        var signal = AiTeacherRecurringLearningSignalFactory.Build(subject, [Source(first, subject), Source(second, subject)])
            .Signals.Single();
        var rendered = string.Join(" ", signal.DisplayName, signal.Description, string.Join(" ", signal.RecentContentTitles));
        Require(!rendered.Contains(privateQuestion, StringComparison.Ordinal));
        Require(!rendered.Contains(privateAttempt, StringComparison.Ordinal));
    }

    private static void ServiceReadsOnlyLocalSources()
    {
        var subject = Binding("razao", "Razão");
        var first = Conversation(Guid.Parse("00000000-0000-0000-0000-000000000051"), "Razão", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.Prerequisite)
        });
        var second = Conversation(Guid.Parse("00000000-0000-0000-0000-000000000052"), "Razão", new[]
        {
            Completed(AiTeacherHiddenDoubtKind.Prerequisite)
        });
        var bindings = new TestBindingStore(
        [
            Bound(first.ConversationId, subject),
            Bound(second.ConversationId, subject)
        ]);
        var conversations = new TestConversationStore(first, second);

        var result = new AiTeacherRecurringLearningSignalService(conversations, bindings)
            .GetAsync(subject).GetAwaiter().GetResult();
        Require(result.Signals.Single().Kind == AiTeacherHiddenDoubtKind.Prerequisite);
        Require(conversations.LoadCalls == 2);
        Require(bindings.ListCalls == 1);
    }

    private static void RejectsForeignSource()
    {
        var math = Binding("razao", "Razão", subjectId: "matematica", subjectName: "Matemática");
        var physics = Binding("cinematica", "Cinemática", subjectId: "fisica", subjectName: "Física");
        Expect<AiContractValidationException>(() => AiTeacherRecurringLearningSignalFactory.Build(math,
        [
            Source(Conversation(Guid.Parse("00000000-0000-0000-0000-000000000061"), "Cinemática", new[]
            {
                Completed(AiTeacherHiddenDoubtKind.ConceptConfusion)
            }), physics)
        ]));
    }

    private static AiTeacherRecurringLearningSignalSource Source(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding) => new() { Conversation = conversation, Binding = binding };

    private static AiTeacherConversationSubjectBinding Bound(Guid conversationId, AiTeacherSubjectBinding binding) => new()
    {
        ConversationId = conversationId,
        BoundAtUtc = Timestamp,
        Binding = binding
    };

    private static AiTeacherSubjectBinding Binding(
        string contentId,
        string contentTitle,
        string subjectId = "matematica",
        string subjectName = "Matemática") => new()
    {
        PackageId = "pacote-escola",
        PackageVersion = "1.0.0",
        SubjectId = subjectId,
        SubjectName = subjectName,
        ContentId = contentId,
        ContentTitle = contentTitle
    };

    private static AiTeacherConversationSnapshot Conversation(
        Guid id,
        string title,
        IReadOnlyList<AiTeacherConversationExchange> exchanges) => new()
    {
        ConversationId = id,
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = exchanges.Max(item => item.UpdatedAtUtc),
        LessonContext = new AiTeacherLessonContext
        {
            ContentId = title == "Cinemática" ? "cinematica" : title == "Proporção" ? "proporcao" : "razao",
            ContentTitle = title,
            ContentSummary = "Contexto verificado.",
            Theory = new AiTeacherTheoryContext
            {
                MaterialId = "teoria-" + (title == "Cinemática" ? "cinematica" : title == "Proporção" ? "proporcao" : "razao"),
                Title = "Teoria " + title,
                LearningGoal = "Compreender o conteúdo.",
                Sections = new()
                {
                    new AiTeacherTheorySectionContext
                    {
                        Kind = "explanation",
                        Title = "Conceito",
                        Body = "Material interno verificado.",
                        Position = 1
                    }
                }
            }
        },
        Exchanges = exchanges
    };

    private static AiTeacherConversationExchange Completed(
        AiTeacherHiddenDoubtKind kind,
        string question = "Explique o conteúdo.",
        string attempt = "")
    {
        var context = new AiTeacherLessonContext
        {
            ContentId = "razao",
            ContentTitle = "Razão",
            ContentSummary = "Contexto verificado."
        };
        return new AiTeacherConversationExchange
        {
            ExchangeId = Guid.NewGuid(),
            CreatedAtUtc = Timestamp,
            UpdatedAtUtc = Timestamp.AddMinutes(1),
            Status = AiTeacherConversationExchangeStatus.Completed,
            Question = question,
            StudentAttempt = attempt,
            Mode = AiTeacherRequestMode.Explain,
            Answer = new AiTeacherAnswer
            {
                HiddenDoubts = new[]
                {
                    new AiTeacherHiddenDoubt
                    {
                        Kind = kind,
                        Summary = "Sinal estruturado.",
                        QuestionSignal = question,
                        Reason = "Observação estruturada.",
                        AddressedInStep = 1
                    }
                }
            }
        };
    }

    private static AiTeacherConversationExchange Incomplete(AiTeacherConversationExchangeStatus status) => new()
    {
        ExchangeId = Guid.NewGuid(),
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = Timestamp.AddMinutes(1),
        Status = status,
        Question = "Pergunta incompleta.",
        Mode = AiTeacherRequestMode.Explain
    };

    private static readonly DateTimeOffset Timestamp = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Recurring learning signal assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class TestConversationStore : IAiTeacherConversationStore
    {
        private readonly IReadOnlyDictionary<Guid, AiTeacherConversationSnapshot> _items;
        public TestConversationStore(params AiTeacherConversationSnapshot[] items) =>
            _items = items.ToDictionary(item => item.ConversationId);
        public string RootDirectory => Path.GetTempPath();
        public int LoadCalls { get; private set; }
        public Task<AiTeacherConversationBeginResult> BeginExchangeAsync(Guid id, AiTeacherRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AiTeacherConversationSnapshot> CompleteExchangeAsync(Guid conversationId, Guid exchangeId, AiTeacherGroundedAnswer result, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AiTeacherConversationSnapshot> MarkExchangeAsync(Guid conversationId, Guid exchangeId, AiTeacherConversationExchangeStatus status, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiTeacherConversationSummary>> ListAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<AiTeacherConversationSnapshot> LoadAsync(Guid id, CancellationToken token = default)
        {
            LoadCalls++;
            return Task.FromResult(_items[id]);
        }
    }

    private sealed class TestBindingStore : IAiTeacherSubjectBindingStore
    {
        private readonly IReadOnlyList<AiTeacherConversationSubjectBinding> _items;
        public TestBindingStore(IReadOnlyList<AiTeacherConversationSubjectBinding> items) => _items = items;
        public string RootDirectory => Path.GetTempPath();
        public int ListCalls { get; private set; }
        public Task<AiTeacherConversationSubjectBinding> BindAsync(AiTeacherConversationSnapshot conversation, AiTeacherSubjectBinding binding, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AiTeacherConversationSubjectBinding?> TryLoadAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiTeacherConversationSubjectBinding>> ListBySubjectAsync(string packageId, string subjectId, CancellationToken token = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<AiTeacherConversationSubjectBinding>>(_items
                .Where(item => item.Binding.PackageId == packageId && item.Binding.SubjectId == subjectId)
                .ToList());
        }
    }
}
