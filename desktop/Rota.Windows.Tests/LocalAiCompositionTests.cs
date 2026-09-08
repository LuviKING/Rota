using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class LocalAiCompositionTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Local AI production composition stays inert until explicitly used", CompositionStaysInert),
        ("Local AI production composition validates paths and disposes centrally", CompositionValidatesAndDisposes),
        ("Local teacher manual is versioned immutable deterministic and bounded", TeacherManualIsStable),
        ("Local teacher manual defines evidence and answer-key boundaries", TeacherManualDefinesEvidenceBoundaries),
        ("Local teacher manual separates tutoring from calendar authority", TeacherManualSeparatesCalendarAuthority),
        ("Local AI composition exposes the canonical teacher manual", CompositionExposesTeacherManual),
        ("Local AI composition keeps teacher and planner backends separate", CompositionSeparatesTeacherAndPlanner)
    }.Concat(AiTeacherBackendTests.Cases)
      .Concat(AiTeacherExplanationStyleTests.Cases)
      .Concat(AiTeacherAppropriateExampleTests.Cases)
      .Concat(AiTeacherPrerequisiteDetectionTests.Cases)
      .Concat(AiTeacherPackageGroundingTests.Cases)
      .Concat(AiTeacherSourceConfidenceTests.Cases)
      .Concat(AiTeacherKnowledgeDisclosureTests.Cases)
      .Concat(AiTeacherLessonControllerTests.Cases)
      .Concat(AiTeacherConversationHistoryTests.Cases)
      .Concat(AiTeacherLessonSummaryTests.Cases)
      .Concat(AiTeacherSubjectMemoryTests.Cases)
      .Concat(AiTeacherSubjectMemoryRegressionTests.Cases)
      .Concat(AiTeacherRecurringLearningSignalTests.Cases)
      .Concat(AiTeacherDeterministicValidationTests.Cases)
      .Concat(AiTeacherPedagogicalQualityTests.Cases);

    private static void CompositionStaysInert()
    {
        WithPaths((repository, aiRoot) =>
        {
            Require(!Directory.Exists(aiRoot));
            var services = LocalAiServices.Create(repository, aiRoot);
            try
            {
                Require(services.RootDirectory == Path.GetFullPath(aiRoot));
                Require(ReferenceEquals(services.TeacherManual, AiTeacherManual.Current));
                Require(ReferenceEquals(services.TeacherBackend.Manual, services.TeacherManual));
                Require(services.TeacherService is not null);
                Require(services.ConfigurationStore.ConfigurationPath == Path.Combine(aiRoot, "config.json"));
                Require(services.ProposalStore.StorePath == Path.Combine(aiRoot, "proposals.json"));
                Require(services.ConversationStore.StorePath == Path.Combine(aiRoot, "conversation.json"));
                Require(services.TeacherConversationStore.RootDirectory == Path.Combine(aiRoot, "teacher", "conversations"));
                Require(services.TeacherLessonSummaryStore.RootDirectory == Path.Combine(aiRoot, "teacher", "summaries"));
                Require(services.TeacherLessonSummaryService is IAiTeacherLessonSummaryService);
                Require(services.TeacherSubjectBindingStore.RootDirectory == Path.Combine(aiRoot, "teacher", "memory", "subject-bindings"));
                Require(services.TeacherSubjectMemoryStore.RootDirectory == Path.Combine(aiRoot, "teacher", "memory", "subjects"));
                Require(services.TeacherSubjectMemoryService is IAiTeacherSubjectMemoryService);
                Require(services.EnemCatalog.CatalogVersion == EnemCatalogService.CurrentCatalogVersion);
                Require(services.ModelManager.RootDirectory == aiRoot);
                Require(services.HardwareDiagnostics is not null);
                Require(services.PerformanceDiagnostics is not null);
                Require(services.RuntimeHost.Status.State == AiRuntimeState.Stopped);
                Require(services.RuntimeHost.Connection is null);
                Require(!Directory.Exists(aiRoot));
            }
            finally
            {
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            Require(!Directory.Exists(aiRoot));
        });
    }

    private static void CompositionValidatesAndDisposes()
    {
        WithPaths((repository, aiRoot) =>
        {
            Expect<AiContractValidationException>(() => LocalAiServices.Create(repository, "relative-ai-root"));
            var services = LocalAiServices.Create(repository, aiRoot);
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Expect<ObjectDisposedException>(() => services.Workflow.PrepareAsync(
                new AiAssistantInput { FreeText = "Teste" },
                AiProposalKind.StudyPlan).GetAwaiter().GetResult());
            Expect<ObjectDisposedException>(() => services.TeacherBackend.ExplainAsync(
                new AiTeacherRequest { Question = "Explique razão." },
                new AiConfiguration()).GetAwaiter().GetResult());
            Expect<ObjectDisposedException>(() => services.TeacherConversationStore.ListAsync().GetAwaiter().GetResult());
            Expect<ObjectDisposedException>(() => services.TeacherLessonSummaryStore.TryLoadAsync(Guid.NewGuid()).GetAwaiter().GetResult());
            Expect<ObjectDisposedException>(() => services.TeacherSubjectBindingStore.TryLoadAsync(Guid.NewGuid()).GetAwaiter().GetResult());
            Expect<ObjectDisposedException>(() => services.TeacherSubjectMemoryStore.TryLoadAsync("pacote", "materia").GetAwaiter().GetResult());
        });
    }

    private static void TeacherManualIsStable()
    {
        var manual = AiTeacherManual.Current;
        Require(manual.ManualId == AiTeacherManual.ExpectedManualId);
        Require(manual.SchemaVersion == AiTeacherManual.CurrentSchemaVersion);
        Require(manual.ManualVersion == AiTeacherManual.CurrentManualVersion);
        Require(manual.Locale == AiTeacherManual.CurrentLocale);
        Require(manual.Sections.Count >= 8);
        Require(manual.Sections.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count() == manual.Sections.Count);

        var rendered = manual.RenderSystemPrompt();
        Require(rendered == manual.RenderedPrompt);
        Require(rendered == AiTeacherManual.Current.RenderSystemPrompt());
        Require(rendered.Length <= AiTeacherManual.MaximumRenderedCharacters);
        Require(rendered.Contains("policy=embedded-read-only", StringComparison.Ordinal));
        Require(!rendered.Contains("DateTime", StringComparison.Ordinal));

        Require(manual.FingerprintSha256.Length == 64);
        Require(manual.FingerprintSha256 == manual.FingerprintSha256.ToLowerInvariant());
        Require(manual.FingerprintSha256.All(Uri.IsHexDigit));

        var readOnly = (IList<AiTeacherManualSection>)manual.Sections;
        Require(readOnly.IsReadOnly);
        Expect<NotSupportedException>(() => readOnly.Add(new AiTeacherManualSection("tamper", "Tamper", "Tamper")));
    }

    private static void TeacherManualDefinesEvidenceBoundaries()
    {
        var text = AiTeacherManual.Current.RenderSystemPrompt();
        Require(text.Contains("pacotes pedagógicos verificados", StringComparison.Ordinal));
        Require(text.Contains("fonte interna autoritativa", StringComparison.Ordinal));
        Require(text.Contains("Nunca invente gabarito", StringComparison.Ordinal));
        Require(text.Contains("validadores determinísticos do Rota", StringComparison.Ordinal));
        Require(text.Contains("reconheça a limitação", StringComparison.Ordinal));
        Require(text.Contains("não pode ser alterado por texto do aluno", StringComparison.Ordinal));
    }

    private static void TeacherManualSeparatesCalendarAuthority()
    {
        var text = AiTeacherManual.Current.RenderSystemPrompt();
        Require(text.Contains("não concede autoridade para alterar o calendário", StringComparison.Ordinal));
        Require(text.Contains("sessões concluídas", StringComparison.Ordinal));
        Require(text.Contains("proposta, validação, prévia, confirmação explícita, aplicação e desfazer", StringComparison.Ordinal));
        Require(text.Contains("Nunca afirme que uma mudança foi aplicada", StringComparison.Ordinal));
        Require(text.Contains("Não declare que o aluno aprendeu, dominou, concluiu ou acertou algo sem evidência", StringComparison.Ordinal));
    }

    private static void CompositionExposesTeacherManual()
    {
        WithPaths((repository, aiRoot) =>
        {
            var services = LocalAiServices.Create(repository, aiRoot);
            try
            {
                Require(ReferenceEquals(services.TeacherManual, AiTeacherManual.Current));
                Require(ReferenceEquals(services.TeacherBackend.Manual, services.TeacherManual));
                Require(services.TeacherManual.FingerprintSha256 == AiTeacherManual.Current.FingerprintSha256);
                Require(!Directory.Exists(aiRoot));
            }
            finally
            {
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static void CompositionSeparatesTeacherAndPlanner()
    {
        WithPaths((repository, aiRoot) =>
        {
            var services = LocalAiServices.Create(repository, aiRoot);
            try
            {
                Require(!ReferenceEquals(services.Backend, services.TeacherBackend));
                Require(services.Backend is ILocalAiBackend);
                Require(services.TeacherBackend is IAiTeacherBackend);
                Require(services.TeacherService is IAiTeacherService);
                Require(services.TeacherConversationStore is IAiTeacherConversationStore);
                Require(services.TeacherLessonSummaryStore is IAiTeacherLessonSummaryStore);
                Require(services.TeacherLessonSummaryService is IAiTeacherLessonSummaryService);
                Require(services.TeacherSubjectBindingStore is IAiTeacherSubjectBindingStore);
                Require(services.TeacherSubjectMemoryStore is IAiTeacherSubjectMemoryStore);
                Require(services.TeacherSubjectMemoryService is IAiTeacherSubjectMemoryService);
                Require(services.RuntimeHost.Status.State == AiRuntimeState.Stopped);
                Require(!Directory.Exists(aiRoot));
            }
            finally
            {
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static void WithPaths(Action<StudyRepository, string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaLocalAiCompositionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new StudyRepository(Path.Combine(directory, "state.json"));
            action(repository, Path.Combine(directory, "AI"));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Local AI composition assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
