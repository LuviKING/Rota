using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherSubjectMemoryRegressionTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher subject memory rebuilds a stale derived cache", StaleCacheIsRebuilt),
        ("Teacher subject memory groups package versions by logical subject identity", PackageVersionsShareLogicalSubject),
        ("Teacher subject memory keeps equal subject ids from different packages separate", EqualSubjectIdsFromDifferentPackagesStaySeparate),
        ("Teacher subject memory keeps different subjects separate", DifferentSubjectsStaySeparate),
        ("Teacher subject memory updates one conversation idempotently", ExistingConversationUpdatesWithoutDuplication),
        ("Teacher history without a verified subject binding stays readable and unindexed", UnboundHistoryStaysReadableAndUnindexed),
        ("Teacher subject memory corruption rebuilds without rewriting history", CorruptMemoryRebuildsWithoutTouchingHistory),
        ("Teacher subject memory bounds detailed lessons deterministically", DetailedLessonLimitIsDeterministic),
        ("Teacher subject memory contract failure after persistence is non fatal", ContractFailureAfterPersistenceIsNonFatal)
    };

    private static void StaleCacheIsRebuilt()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var context = Context("razao", "Razão");
            var binding = Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context);
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);

            var first = CompletedConversation(conversationStore, context, "Primeira pergunta.");
            var cached = service.RefreshAsync(first, binding).GetAwaiter().GetResult();
            Require(cached.ConversationCount == 1);

            var second = CompletedConversation(conversationStore, context, "Segunda pergunta.");
            service.EnsureBindingAsync(second, binding).GetAwaiter().GetResult();

            var rebuilt = service.GetAsync(binding).GetAwaiter().GetResult();
            Require(rebuilt.ConversationCount == 2);
            Require(rebuilt.CompletedExchangeCount == 2);
            Require(rebuilt.Conversations.Select(item => item.ConversationId).ToHashSet().SetEquals(
                new[] { first.ConversationId, second.ConversationId }));
        });
    }

    private static void PackageVersionsShareLogicalSubject()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var context = Context("razao", "Razão");
            var version1 = Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context);
            var version2 = Binding("pacote-matematica", "2.0.0", "matematica", "Matemática", context);
            var first = CompletedConversation(conversationStore, context, "Versão um.");
            var second = CompletedConversation(conversationStore, context, "Versão dois.");
            bindingStore.BindAsync(first, version1).GetAwaiter().GetResult();
            bindingStore.BindAsync(second, version2).GetAwaiter().GetResult();

            var memory = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore)
                .GetAsync(version2).GetAwaiter().GetResult();

            Require(memory.PackageId == "pacote-matematica");
            Require(memory.SubjectId == "matematica");
            Require(memory.ConversationCount == 2);
            Require(memory.Conversations.Select(item => item.PackageVersion).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "1.0.0", "2.0.0" }));
        });
    }

    private static void EqualSubjectIdsFromDifferentPackagesStaySeparate()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var context = Context("razao", "Razão");
            var packageA = Binding("pacote-a", "1.0.0", "matematica", "Matemática", context);
            var packageB = Binding("pacote-b", "1.0.0", "matematica", "Matemática", context);
            var first = CompletedConversation(conversationStore, context, "Pacote A.");
            var second = CompletedConversation(conversationStore, context, "Pacote B.");
            bindingStore.BindAsync(first, packageA).GetAwaiter().GetResult();
            bindingStore.BindAsync(second, packageB).GetAwaiter().GetResult();
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);

            var a = service.GetAsync(packageA).GetAwaiter().GetResult();
            var b = service.GetAsync(packageB).GetAwaiter().GetResult();
            Require(a.ConversationCount == 1 && a.Conversations.Single().ConversationId == first.ConversationId);
            Require(b.ConversationCount == 1 && b.Conversations.Single().ConversationId == second.ConversationId);
        });
    }

    private static void DifferentSubjectsStaySeparate()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var mathContext = Context("razao", "Razão");
            var physicsContext = Context("cinematica", "Cinemática");
            var math = Binding("pacote-escola", "1.0.0", "matematica", "Matemática", mathContext);
            var physics = Binding("pacote-escola", "1.0.0", "fisica", "Física", physicsContext);
            var mathConversation = CompletedConversation(conversationStore, mathContext, "Explique razão.");
            var physicsConversation = CompletedConversation(conversationStore, physicsContext, "Explique movimento.");
            bindingStore.BindAsync(mathConversation, math).GetAwaiter().GetResult();
            bindingStore.BindAsync(physicsConversation, physics).GetAwaiter().GetResult();
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);

            var mathMemory = service.GetAsync(math).GetAwaiter().GetResult();
            var physicsMemory = service.GetAsync(physics).GetAwaiter().GetResult();
            Require(mathMemory.ConversationCount == 1 && mathMemory.SubjectId == "matematica");
            Require(physicsMemory.ConversationCount == 1 && physicsMemory.SubjectId == "fisica");
            Require(mathMemory.Conversations.Single().ConversationId != physicsMemory.Conversations.Single().ConversationId);
        });
    }

    private static void ExistingConversationUpdatesWithoutDuplication()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var context = Context("razao", "Razão");
            var binding = Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context);
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);
            var conversation = CompletedConversation(conversationStore, context, "Primeira pergunta.");
            service.RefreshAsync(conversation, binding).GetAwaiter().GetResult();

            var secondRequest = Request(context, "Segunda pergunta.");
            var begin = conversationStore.BeginExchangeAsync(conversation.ConversationId, secondRequest)
                .GetAwaiter().GetResult();
            var updated = conversationStore.CompleteExchangeAsync(
                    begin.ConversationId,
                    begin.ExchangeId,
                    Grounded(secondRequest))
                .GetAwaiter().GetResult();

            var firstRefresh = service.RefreshAsync(updated, binding).GetAwaiter().GetResult();
            var secondRefresh = service.RefreshAsync(updated, binding).GetAwaiter().GetResult();
            Require(firstRefresh.ConversationCount == 1 && secondRefresh.ConversationCount == 1);
            Require(firstRefresh.CompletedExchangeCount == 2 && secondRefresh.CompletedExchangeCount == 2);
            Require(secondRefresh.Conversations.Single().SourceExchangeCount == 2);
            Require(secondRefresh.Conversations.Select(item => item.ConversationId).Distinct().Count() == 1);
        });
    }

    private static void UnboundHistoryStaysReadableAndUnindexed()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var context = Context("razao", "Razão");
            var conversation = CompletedConversation(conversationStore, context, "Histórico antigo sem vínculo.");
            var reloaded = conversationStore.LoadAsync(conversation.ConversationId).GetAwaiter().GetResult();
            Require(reloaded.ConversationId == conversation.ConversationId);
            Require(reloaded.Exchanges.Single().Status == AiTeacherConversationExchangeStatus.Completed);

            var binding = Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context);
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);
            Expect<KeyNotFoundException>(() => service.GetAsync(binding).GetAwaiter().GetResult());

            var stillReadable = conversationStore.LoadAsync(conversation.ConversationId).GetAwaiter().GetResult();
            Require(stillReadable.Exchanges.Single().Answer?.Title == "Explicação validada");
        });
    }

    private static void CorruptMemoryRebuildsWithoutTouchingHistory()
    {
        WithStores((conversationStore, bindingStore, memoryStore, _) =>
        {
            var context = Context("razao", "Razão");
            var binding = Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context);
            var conversation = CompletedConversation(conversationStore, context, "Pergunta preservada.");
            var service = new AiTeacherSubjectMemoryService(conversationStore, bindingStore, memoryStore);
            service.RefreshAsync(conversation, binding).GetAwaiter().GetResult();

            var historyPath = Path.Combine(conversationStore.RootDirectory, conversation.ConversationId.ToString("D") + ".json");
            var historyBefore = File.ReadAllBytes(historyPath);
            var memoryPath = Path.Combine(memoryStore.RootDirectory, "pacote-matematica--matematica.json");
            File.WriteAllText(memoryPath, "{\"SchemaVersion\":1,\"SchemaVersion\":1}");

            var rebuilt = service.GetAsync(binding).GetAwaiter().GetResult();
            var historyAfter = File.ReadAllBytes(historyPath);
            Require(rebuilt.ConversationCount == 1 && rebuilt.CompletedExchangeCount == 1);
            Require(historyBefore.SequenceEqual(historyAfter));
            Require(Directory.EnumerateFiles(memoryStore.RootDirectory)
                .Any(path => Path.GetFileName(path).StartsWith(
                    "pacote-matematica--matematica.json.corrupt-", StringComparison.Ordinal)));
        });
    }

    private static void DetailedLessonLimitIsDeterministic()
    {
        WithStores((conversationStore, _, _, _) =>
        {
            var context = Context("razao", "Razão");
            var binding = Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context);
            var conversations = Enumerable.Range(1, AiTeacherSubjectMemoryFactory.MaximumDetailedLessons + 1)
                .Select(index => CompletedConversation(conversationStore, context, $"Pergunta {index}."))
                .ToList();
            var indexes = conversations
                .Select(item => AiTeacherSubjectMemoryFactory.CreateConversationIndex(item, binding))
                .ToList();
            var lessons = conversations
                .Select(item => AiTeacherSubjectMemoryFactory.CreateLesson(item, binding))
                .ToList();

            var memory = AiTeacherSubjectMemoryFactory.Build(binding.PackageId, binding.SubjectId, indexes, lessons);
            Require(memory.ConversationCount == AiTeacherSubjectMemoryFactory.MaximumDetailedLessons + 1);
            Require(memory.RecentLessons.Count == AiTeacherSubjectMemoryFactory.MaximumDetailedLessons);
            Require(memory.HasMoreDetailedLessons);
            Require(memory.RecentLessons.Select(item => item.ConversationId).SequenceEqual(
                memory.Conversations.Take(AiTeacherSubjectMemoryFactory.MaximumDetailedLessons)
                    .Select(item => item.ConversationId)));
        });
    }

    private static void ContractFailureAfterPersistenceIsNonFatal()
    {
        var directory = TempDirectory();
        try
        {
            var context = Context("razao", "Razão");
            using var conversationStore = new AiTeacherConversationStore(
                Path.Combine(directory, "conversations"), () => Timestamp, () => Guid.NewGuid());
            var controller = new AiTeacherLessonController(
                new RecordingTeacherService(),
                context,
                conversationStore,
                summaryService: null,
                subjectBinding: Binding("pacote-matematica", "1.0.0", "matematica", "Matemática", context),
                subjectMemoryService: new ContractFailingSubjectMemoryService());

            var turn = controller.AskAsync(
                    Guid.Empty,
                    "Explique com falha derivada depois de salvar.",
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

    private static AiTeacherLessonContext Context(string contentId, string title) => new()
    {
        ContentId = contentId,
        ContentTitle = title,
        ContentSummary = $"Resumo verificado de {title}.",
        SourceKind = AiTeacherLessonContext.LearningPackageSource,
        Prerequisites = new()
    };

    private static AiTeacherSubjectBinding Binding(
        string packageId,
        string packageVersion,
        string subjectId,
        string subjectName,
        AiTeacherLessonContext context) => new()
    {
        PackageId = packageId,
        PackageVersion = packageVersion,
        SubjectId = subjectId,
        SubjectName = subjectName,
        ContentId = context.ContentId,
        ContentTitle = context.ContentTitle
    };

    private static AiTeacherConversationSnapshot CompletedConversation(
        AiTeacherConversationStore store,
        AiTeacherLessonContext context,
        string question)
    {
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
        var context = request.LessonContext!;
        var answer = new AiTeacherAnswer
        {
            Title = "Explicação validada",
            Introduction = "Vamos usar somente o material interno verificado.",
            Steps = new[]
            {
                new AiTeacherStep { Number = 1, Title = "Conceito", Explanation = "Explicação derivada do conteúdo verificado." }
            },
            Recap = "Retome o conceito antes de continuar.",
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

    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 21, 0, 0, TimeSpan.Zero);

    private static void WithStores(
        Action<AiTeacherConversationStore, AiTeacherSubjectBindingStore, AiTeacherSubjectMemoryStore, string> body)
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
        var path = Path.Combine(Path.GetTempPath(), "RotaTeacherSubjectMemoryRegressionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher subject memory regression assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class RecordingTeacherService : IAiTeacherService
    {
        public Task<AiTeacherAnswer> ExplainAsync(AiTeacherRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExplainWithGroundingAsync(request, cancellationToken).GetAwaiter().GetResult().Answer);

        public Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
            AiTeacherRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Grounded(request));
        }
    }

    private sealed class ContractFailingSubjectMemoryService : IAiTeacherSubjectMemoryService
    {
        public Task EnsureBindingAsync(
            AiTeacherConversationSnapshot conversation,
            AiTeacherSubjectBinding binding,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AiTeacherSubjectMemorySnapshot> RefreshAsync(
            AiTeacherConversationSnapshot conversation,
            AiTeacherSubjectBinding binding,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AiTeacherSubjectMemorySnapshot>(
                new AiContractValidationException("Falha simulada no índice derivado."));

        public Task<AiTeacherSubjectMemorySnapshot> GetAsync(
            AiTeacherSubjectBinding binding,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AiTeacherSubjectMemorySnapshot>(
                new AiContractValidationException("Falha simulada no índice derivado."));
    }
}
