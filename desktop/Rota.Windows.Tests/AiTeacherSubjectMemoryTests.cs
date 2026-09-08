using Rota.Desktop;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherSubjectMemoryTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher subject binding follows only the verified package hierarchy", BindingFollowsVerifiedHierarchy),
        ("Teacher subject binding rejects a mismatched frozen lesson", BindingRejectsMismatchedLesson),
        ("Teacher conversation subject binding is immutable and persisted locally", BindingStoreIsImmutable),
        ("Teacher subject memory aggregates factual conversation counts", MemoryAggregatesFacts),
        ("Teacher subject memory bounds recent exchanges without trimming full history", MemoryBoundsDetails),
        ("Teacher subject memory service rebuilds a missing cache from verified bindings", ServiceRebuildsMissingCache),
        ("Teacher subject memory store recovers the last valid backup", MemoryStoreRecoversBackup),
        ("Teacher controller refreshes subject memory without feeding it back to the model", ControllerKeepsMemoryPassive),
        ("Teacher subject memory failure cannot erase a persisted answer", MemoryFailureIsNonFatal)
    };

    private static void BindingFollowsVerifiedHierarchy()
    {
        var package = Package();
        var binding = AiTeacherSubjectBindingFactory.Create(package, "razao");
        Require(binding.PackageId == "pacote-matematica");
        Require(binding.PackageVersion == "1.0.0");
        Require(binding.SubjectId == "matematica");
        Require(binding.SubjectName == "Matemática");
        Require(binding.ContentId == "razao");
        Require(binding.ContentTitle == "Razão");
        AiTeacherSubjectBindingFactory.Validate(binding, AiTeacherLessonContextFactory.Create(package, "razao"));
    }

    private static void BindingRejectsMismatchedLesson()
    {
        var package = Package();
        var binding = AiTeacherSubjectBindingFactory.Create(package, "razao");
        var other = AiTeacherLessonContextFactory.Create(package, "proporcao");
        Expect<AiContractValidationException>(() => AiTeacherSubjectBindingFactory.Validate(binding, other));
    }

    private static void BindingStoreIsImmutable()
    {
        WithStores((conversationStore, bindingStore, _, _) =>
        {
            var context = Context();
            var begin = conversationStore.BeginExchangeAsync(Guid.Empty, Request(context, "Explique razão."))
                .GetAwaiter().GetResult();
            var binding = Binding();
            var stored = bindingStore.BindAsync(begin.Conversation, binding).GetAwaiter().GetResult();
            Require(stored.ConversationId == begin.ConversationId);
            Require(AiTeacherSubjectBindingFactory.Equivalent(stored.Binding, binding));

            var loaded = bindingStore.TryLoadAsync(begin.ConversationId).GetAwaiter().GetResult();
            Require(loaded is not null && AiTeacherSubjectBindingFactory.Equivalent(loaded.Binding, binding));

            var conflict = binding with { SubjectId = "fisica", SubjectName = "Física" };
            Expect<AiContractValidationException>(() => bindingStore.BindAsync(begin.Conversation, conflict)
                .GetAwaiter().GetResult());
        });
    }

    private static void MemoryAggregatesFacts()
    {
        WithStores((conversationStore, _, _, _) =>
        {
            var first = CompletedConversation(conversationStore, "Primeira pergunta.");
            var second = CompletedConversation(conversationStore, "Segunda pergunta.");
            var binding = Binding();
            var memory = AiTeacherSubjectMemoryFactory.Build(
                binding.PackageId,
                binding.SubjectId,
                new[]
                {
                    AiTeacherSubjectMemoryFactory.CreateConversationIndex(first, binding),
                    AiTeacherSubjectMemoryFactory.CreateConversationIndex(second, binding)
                },
                new[]
                {
                    AiTeacherSubjectMemoryFactory.CreateLesson(first, binding),
                    AiTeacherSubjectMemoryFactory.CreateLesson(second, binding)
                });

            Require(memory.ConversationCount == 2);
            Require(memory.CompletedExchangeCount == 2);
            Require(memory.UnansweredExchangeCount == 0);
            Require(memory.SubjectName == "Matemática");
            Require(memory.Conversations.Select(item => item.ConversationId).Distinct().Count() == 2);
            AiTeacherSubjectMemoryFactory.Validate(memory);
        });
    }

    private static void MemoryBoundsDetails()
    {
        WithStores((conversationStore, _, _, _) =>
        {
            var context = Context();
            var firstRequest = Request(context, "Pergunta 1.");
            var begin = conversationStore.BeginExchangeAsync(Guid.Empty, firstRequest).GetAwaiter().GetResult();
            var conversation = conversationStore.CompleteExchangeAsync(
                    begin.ConversationId,
                    begin.ExchangeId,
                    Grounded(firstRequest))
                .GetAwaiter().GetResult();
            for (var index = 2; index <= 8; index++)
            {
                var request = Request(context, $"Pergunta {index}.");
                var next = conversationStore.BeginExchangeAsync(conversation.ConversationId, request)
                    .GetAwaiter().GetResult();
                conversation = conversationStore.CompleteExchangeAsync(
                        conversation.ConversationId,
                        next.ExchangeId,
                        Grounded(request))
                    .GetAwaiter().GetResult();
            }

            var lesson = AiTeacherSubjectMemoryFactory.CreateLesson(conversation, Binding());
            Require(conversation.Exchanges.Count == 8);
            Require(lesson.RecentExchanges.Count == AiTeacherSubjectMemoryFactory.MaximumRecentExchangesPerLesson);
            Require(lesson.HasEarlierCompletedExchanges);
            Require(lesson.RecentExchanges.First().Question == "Pergunta 3.");
            Require(conversation.Exchanges.First().Question == "Pergunta 1.");
        });
    }

    private static void ServiceRebuildsMissingCache()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var conversation = CompletedConversation(conversationStore, "Explique a relação.");
            var binding = Binding();
            bindingStore.BindAsync(conversation, binding).GetAwaiter().GetResult();
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);

            Require(memoryStore.TryLoadAsync(binding.PackageId, binding.SubjectId).GetAwaiter().GetResult() is null);
            var rebuilt = service.GetAsync(binding).GetAwaiter().GetResult();
            Require(rebuilt.ConversationCount == 1);
            Require(rebuilt.CompletedExchangeCount == 1);
            Require(memoryStore.TryLoadAsync(binding.PackageId, binding.SubjectId).GetAwaiter().GetResult() is not null);
        });
    }

    private static void MemoryStoreRecoversBackup()
    {
        WithStores((conversationStore, _, memoryStore, _) =>
        {
            var firstConversation = CompletedConversation(conversationStore, "Primeira pergunta.");
            var binding = Binding();
            var first = AiTeacherSubjectMemoryFactory.Build(
                binding.PackageId,
                binding.SubjectId,
                new[] { AiTeacherSubjectMemoryFactory.CreateConversationIndex(firstConversation, binding) },
                new[] { AiTeacherSubjectMemoryFactory.CreateLesson(firstConversation, binding) });
            memoryStore.SaveAsync(first).GetAwaiter().GetResult();

            var secondConversation = CompletedConversation(conversationStore, "Segunda pergunta.");
            var second = AiTeacherSubjectMemoryFactory.Build(
                binding.PackageId,
                binding.SubjectId,
                new[]
                {
                    AiTeacherSubjectMemoryFactory.CreateConversationIndex(firstConversation, binding),
                    AiTeacherSubjectMemoryFactory.CreateConversationIndex(secondConversation, binding)
                },
                new[]
                {
                    AiTeacherSubjectMemoryFactory.CreateLesson(firstConversation, binding),
                    AiTeacherSubjectMemoryFactory.CreateLesson(secondConversation, binding)
                });
            memoryStore.SaveAsync(second).GetAwaiter().GetResult();

            var path = Path.Combine(memoryStore.RootDirectory, "pacote-matematica--matematica.json");
            Require(File.Exists(path + ".bak"));
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"SchemaVersion\":1}");
            var recovered = memoryStore.TryLoadAsync(binding.PackageId, binding.SubjectId).GetAwaiter().GetResult();
            Require(recovered is not null);
            Require(recovered!.ConversationCount == first.ConversationCount);
            Require(Directory.EnumerateFiles(memoryStore.RootDirectory)
                .Any(item => Path.GetFileName(item).StartsWith("pacote-matematica--matematica.json.corrupt-", StringComparison.Ordinal)));
        });
    }

    private static void ControllerKeepsMemoryPassive()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var teacher = new RecordingTeacherService();
            var memory = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);
            var controller = new AiTeacherLessonController(
                teacher,
                Context(),
                conversationStore,
                summaryService: null,
                subjectBinding: Binding(),
                subjectMemoryService: memory);

            var first = controller.AskAsync(
                    Guid.Empty,
                    "Primeira pergunta sobre razão.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.Simple)
                .GetAwaiter().GetResult();
            var second = controller.AskAsync(
                    first.ConversationId,
                    "Segunda pergunta independente.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.Visual)
                .GetAwaiter().GetResult();

            Require(first.SubjectMemoryWarning.Length == 0);
            Require(second.SubjectMemoryWarning.Length == 0);
            Require(teacher.Requests.Count == 2);
            Require(teacher.Requests[1].Question == "Segunda pergunta independente.");
            Require(!teacher.Requests[1].Question.Contains("Primeira", StringComparison.Ordinal));
            Require(teacher.Requests[1].StudentAttempt.Length == 0);
            var stored = memoryStore.TryLoadAsync("pacote-matematica", "matematica").GetAwaiter().GetResult();
            Require(stored is not null && stored.CompletedExchangeCount == 2);
        });
    }

    private static void MemoryFailureIsNonFatal()
    {
        var directory = TempDirectory();
        try
        {
            using var conversationStore = new AiTeacherConversationStore(
                Path.Combine(directory, "conversations"), () => Timestamp, () => Guid.NewGuid());
            var controller = new AiTeacherLessonController(
                new RecordingTeacherService(),
                Context(),
                conversationStore,
                summaryService: null,
                subjectBinding: Binding(),
                subjectMemoryService: new FailingSubjectMemoryService());

            var turn = controller.AskAsync(
                    Guid.Empty,
                    "Explique apesar da falha da memória.",
                    "",
                    AiTeacherRequestMode.Explain,
                    AiTeacherExplanationStyle.StepByStep)
                .GetAwaiter().GetResult();
            Require(turn.SubjectMemoryWarning.Length > 0);
            var persisted = conversationStore.LoadAsync(turn.ConversationId).GetAwaiter().GetResult();
            Require(persisted.Exchanges.Single().Status == AiTeacherConversationExchangeStatus.Completed);
            Require(persisted.Exchanges.Single().Answer?.Title == "Explicação validada");
        }
        finally { TryDeleteDirectory(directory); }
    }

    private static LearningContentPackage Package() => new()
    {
        Package = new LearningPackageIdentity
        {
            Id = "pacote-matematica",
            Version = "1.0.0",
            Title = "Matemática verificada",
            Locale = "pt-BR"
        },
        Catalog = new LearningCatalog
        {
            Subjects = new() { new LearningSubject { Id = "matematica", Name = "Matemática" } },
            Courses = new()
            {
                new LearningCourse { Id = "curso-matematica", SubjectId = "matematica", Name = "Matemática", Position = 1 }
            },
            Modules = new()
            {
                new LearningModule { Id = "modulo-razoes", CourseId = "curso-matematica", Name = "Razões", Position = 1 }
            },
            Skills = new()
            {
                new LearningSkill { Id = "habilidade-razao", SubjectId = "matematica", Name = "Razões" }
            },
            Lessons = new()
            {
                new LearningLesson
                {
                    Id = "aula-razao",
                    ModuleId = "modulo-razoes",
                    Name = "Razão e proporção",
                    Position = 1,
                    SkillIds = new() { "habilidade-razao" }
                }
            },
            Contents = new()
            {
                new LearningContent { Id = "razao", LessonId = "aula-razao", Title = "Razão", Summary = "Conceito de razão.", Position = 1 },
                new LearningContent { Id = "proporcao", LessonId = "aula-razao", Title = "Proporção", Summary = "Conceito de proporção.", Position = 2 }
            }
        },
        TheoryMaterials = new()
        {
            new LearningTheoryMaterial
            {
                Id = "teoria-razao",
                ContentId = "razao",
                Title = "Teoria de Razão",
                LearningGoal = "Compreender o conceito de razão.",
                Sections = new()
                {
                    new LearningTheorySection
                    {
                        Kind = LearningTheorySectionKinds.Explanation,
                        Title = "Conceito",
                        Body = "Razão compara duas quantidades por meio de uma divisão.",
                        Position = 1
                    }
                }
            },
            new LearningTheoryMaterial
            {
                Id = "teoria-proporcao",
                ContentId = "proporcao",
                Title = "Teoria de Proporção",
                LearningGoal = "Compreender o conceito de proporção.",
                Sections = new()
                {
                    new LearningTheorySection
                    {
                        Kind = LearningTheorySectionKinds.Explanation,
                        Title = "Conceito",
                        Body = "Proporção expressa igualdade entre duas razões.",
                        Position = 1
                    }
                }
            }
        }
    };

    private static AiTeacherLessonContext Context() => AiTeacherLessonContextFactory.Create(Package(), "razao");
    private static AiTeacherSubjectBinding Binding() => AiTeacherSubjectBindingFactory.Create(Package(), "razao");

    private static AiTeacherRequest Request(AiTeacherLessonContext context, string question) => new()
    {
        Question = question,
        Mode = AiTeacherRequestMode.Explain,
        ExplanationStyle = AiTeacherExplanationStyle.StepByStep,
        LessonContext = context
    };

    private static AiTeacherConversationSnapshot CompletedConversation(AiTeacherConversationStore store, string question)
    {
        var request = Request(Context(), question);
        var begin = store.BeginExchangeAsync(Guid.Empty, request).GetAwaiter().GetResult();
        return store.CompleteExchangeAsync(begin.ConversationId, begin.ExchangeId, Grounded(request))
            .GetAwaiter().GetResult();
    }

    private static AiTeacherGroundedAnswer Grounded(AiTeacherRequest request)
    {
        var context = request.LessonContext!;
        var answer = new AiTeacherAnswer
        {
            Title = "Explicação validada",
            Introduction = "Vamos usar somente o material interno verificado.",
            Steps = new[]
            {
                new AiTeacherStep { Number = 1, Title = "Conceito", Explanation = "Razão compara duas quantidades." }
            },
            Recap = "Retome o conceito de razão antes de continuar.",
            Limitations = Array.Empty<string>(),
            HiddenDoubts = Array.Empty<AiTeacherHiddenDoubt>(),
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

    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 20, 0, 0, TimeSpan.Zero);

    private static void WithStores(Action<AiTeacherConversationStore, AiTeacherSubjectBindingStore, AiTeacherSubjectMemoryStore, string> body)
    {
        var directory = TempDirectory();
        try
        {
            using var conversationStore = new AiTeacherConversationStore(
                Path.Combine(directory, "conversations"), () => Timestamp, () => Guid.NewGuid());
            using var bindingStore = new AiTeacherSubjectBindingStore(Path.Combine(directory, "bindings"), () => Timestamp);
            using var memoryStore = new AiTeacherSubjectMemoryStore(Path.Combine(directory, "memory"));
            body(conversationStore, bindingStore, memoryStore, directory);
        }
        finally { TryDeleteDirectory(directory); }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RotaTeacherSubjectMemoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher subject memory assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
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
            Requests.Add(request);
            return Task.FromResult(Grounded(request));
        }
    }

    private sealed class FailingSubjectMemoryService : IAiTeacherSubjectMemoryService
    {
        public Task EnsureBindingAsync(
            AiTeacherConversationSnapshot conversation,
            AiTeacherSubjectBinding binding,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada no vínculo de matéria.");

        public Task<AiTeacherSubjectMemorySnapshot> RefreshAsync(
            AiTeacherConversationSnapshot conversation,
            AiTeacherSubjectBinding binding,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada na memória por matéria.");

        public Task<AiTeacherSubjectMemorySnapshot> GetAsync(
            AiTeacherSubjectBinding binding,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada na memória por matéria.");
    }
}
