using Rota.Desktop;
using Rota.Desktop.LocalAI;
using Rota.Desktop.Tests.Fakes;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

var tests = new (string Name, Action Body)[]
{
    ("Importer accepts valid StudyPlan 0.2", ImporterAcceptsValid),
    ("Importer rejects malformed JSON", ImporterRejectsMalformedJson),
    ("Importer rejects unknown keys", ImporterRejectsUnknown),
    ("Importer rejects integer strings", ImporterRejectsIntegerString),
    ("Importer rejects fractional integer lexemes", ImporterRejectsFractionalInteger),
    ("Importer rejects duplicate properties", ImporterRejectsDuplicateProperties),
    ("Importer rejects duplicate session IDs", ImporterRejectsDuplicateSessionIds),
    ("Importer rejects invalid calendar dates", ImporterRejectsInvalidDate),
    ("Importer requires target in StudyPlan 0.2", ImporterRequiresTarget),
    ("Importer restricts review_label to reviews", ImporterRestrictsReviewLabel),
    ("Importer protects runtime review namespace", ImporterProtectsReviewNamespace),
    ("Importer enforces objective deadline", ImporterEnforcesDeadline),
    ("Repository applies and persists a plan", RepositoryAppliesAndPersists),
    ("Repository keeps revision monotonic across plan switching", RepositoryKeepsRevisionMonotonic),
    ("Repository rejects past-only imports before removal", RepositoryRejectsPastOnly),
    ("Completion creates reviews from actual completion date", CompletionUsesActualDate),
    ("Review completion does not recurse", ReviewDoesNotRecurse),
    ("Assessment completion does not create reviews", AssessmentDoesNotCreateReviews),
    ("New plan replaces pending future but preserves completed/runtime", NewPlanPreservesProtectedState),
    ("Session identity remains scoped to plan id", IdentityIsScopedToPlan),
    ("Daily capacity includes runtime reviews", CapacityIncludesRuntime),
    ("Preview does not mutate memory or disk", PreviewDoesNotMutate),
    ("Failed persistence leaves memory and disk unchanged", FailedPersistenceIsTransactional),
    ("Repository recovers the last valid atomic backup", RepositoryRecoversAtomicBackup),
    ("Repository preserves corrupt state without a backup", RepositoryPreservesCorruptState),
    ("Preferences reject unsafe text without mutation", PreferencesRejectUnsafeText),
    ("Preferences persist and reload", PreferencesPersistAndReload),
    ("Stored state rejects unknown properties", StoredStateRejectsUnknownProperties),
    ("Exported backup can be loaded independently", ExportedBackupReloads),
    ("Desktop windows load without XAML or binding failures", DesktopWindowsLoad),
    ("AI prompt explains Rota execution model", PromptExplainsExecutionModel),
    ("Local AI profiles expose the supported product tiers", AiProfilesAreStable),
    ("Local AI configuration serializes and reloads", AiConfigurationSerializationRoundTrips),
    ("Local AI configuration stays separate from study state", AiConfigurationUsesSeparateStorage),
    ("Local AI configuration recovers its atomic backup", AiConfigurationRecoversAtomicBackup),
    ("Local AI input and configuration validation reject invalid contracts", AiValidationRejectsInvalidContracts),
    ("Fake local AI backend returns a valid StudyPlan proposal", FakeAiBuildsStudyPlanProposal),
    ("Fake local AI backend returns structured change operations", FakeAiBuildsChangeProposal),
    ("Fake local AI backend is deterministic", FakeAiIsDeterministic),
    ("Local AI proposal creation honors cancellation", AiPlanningHonorsCancellation),
    ("Local AI backend failures are controlled", AiBackendFailureIsControlled),
    ("Local AI proposals cannot mutate study state", AiProposalDoesNotMutateStudyState),
    ("Hardware policy selects Lightweight for an 8 GB CPU-only PC", HardwarePolicySelectsLightweight),
    ("Hardware policy supports Balanced CPU fallback", HardwarePolicySelectsBalancedCpuFallback),
    ("Hardware policy selects Performance for the target PC", HardwarePolicySelectsPerformanceForTargetPc),
    ("Hardware policy never recommends above Performance", HardwarePolicyCapsAtPerformance),
    ("Hardware policy honors exact profile boundaries", HardwarePolicyHonorsBoundaries),
    ("Hardware policy rejects impossible snapshots", HardwarePolicyRejectsImpossibleSnapshots),
    ("Hardware policy resolves automatic and manual profiles", HardwarePolicyResolvesAutomaticAndManual),
    ("Windows hardware detector maps an injected snapshot", HardwareDetectorMapsSnapshot),
    ("Windows hardware detector honors cancellation", HardwareDetectorHonorsCancellation),
    ("Windows hardware detector reads a safe real snapshot", HardwareDetectorReadsRealSnapshot),
    ("Local AI model catalog exposes one recommendation per profile", AiModelCatalogExposesRecommendations),
    ("Local AI model catalog rejects unsafe artifact names", AiModelCatalogRejectsUnsafeArtifactNames),
    ("Model manager resolves Automatic from detected hardware", AiModelManagerResolvesAutomaticProfile),
    ("Model manager respects a manual profile without probing hardware", AiModelManagerRespectsManualProfile),
    ("Model manager derives installation state from local files", AiModelManagerDerivesInstallationState),
    ("Model manager rejects an unsafe relative root", AiModelManagerRejectsRelativeRoot),
    ("Model manager rejects a model incompatible with the effective profile", AiModelManagerRejectsIncompatibleModel),
    ("Model manager honors cancellation before hardware detection", AiModelManagerHonorsCancellation),
    ("Installation manifest pins trusted runtime and model artifacts", AiInstallationManifestPinsArtifacts),
    ("Installation manifest rejects mutable and untrusted sources", AiInstallationManifestRejectsUntrustedSources),
    ("Local AI installer stages verifies and atomically activates files", AiInstallerActivatesVerifiedFiles),
    ("Local AI installer selects Vulkan for Automatic Performance", AiInstallerSelectsVulkanForAutomaticPerformance),
    ("Local AI installer rejects a corrupted artifact without activation", AiInstallerRejectsCorruptedArtifact),
    ("Local AI installer cleans staging when download is cancelled", AiInstallerCleansCancelledStaging),
    ("Local AI installer rejects runtime archive path traversal", AiInstallerRejectsArchiveTraversal),
    ("Local AI installer rolls back when configuration activation fails", AiInstallerRollsBackFailedActivation),
    ("Local AI installer preserves an active installation on failed update", AiInstallerPreservesActiveInstallation),
    ("Local AI installer completes despite a broken completion observer", AiInstallerCompletionObserverCannotFailCommit),
    ("Local AI installer prevents activation from competing instances", AiInstallerRejectsCompetingInstance),
    ("HTTP AI downloader streams a response to disk", HttpAiDownloaderStreamsToDisk)
};

tests = tests.Concat(Rota.Desktop.Tests.InstallationRegressionTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.RuntimeLifecycleTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.InferenceBackendTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.PlanningContextTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.ProposalPreviewTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.ProposalStoreTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.ProposalWorkflowTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.LocalAiCompositionTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiAssistantControllerTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiAssistantPresentationTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiInstallationControllerTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiProposalApplicationTests.Cases).ToArray();
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROTA_TEST_RUNTIME_ARCHIVES")))
    tests = tests.Append(("Official CPU and Vulkan archives pass the real staging pipeline", (Action)OfficialRuntimeArchivesStage)).ToArray();

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL  {test.Name}\n      {ex.Message}");
    }
}

Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} desktop core tests passed.");
return failed == 0 ? 0 : 1;

static void ImporterAcceptsValid()
{
    var plan = StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60)));
    Eq("plan-a", plan.PlanId);
    Eq(1, plan.Revision);
    Eq(1, plan.Sessions.Count);
    Eq("study", plan.Sessions[0].Kind);
}

static void ImporterRejectsMalformedJson() =>
    Throws(() => StudyPlanImporter.Parse("{\"format\":"), "JSON inválido");

static void ImporterRejectsUnknown()
{
    var json = PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60));
    json = json.Replace("\"format\":", "\"extra\":true,\"format\":", StringComparison.Ordinal);
    Throws(() => StudyPlanImporter.Parse(json), "Campo não suportado");
}

static void ImporterRejectsIntegerString()
{
    var json = PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60));
    json = json.Replace("\"minutes\":60", "\"minutes\":\"60\"", StringComparison.Ordinal);
    Throws(() => StudyPlanImporter.Parse(json), "número inteiro");
}

static void ImporterRejectsFractionalInteger()
{
    var json = PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60));
    json = json.Replace("\"revision\":1", "\"revision\":1.0", StringComparison.Ordinal);
    Throws(() => StudyPlanImporter.Parse(json), "número inteiro");
}

static void ImporterRejectsDuplicateProperties()
{
    var json = PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60));
    json = json.Replace("\"format\":\"studyplan\"", "\"format\":\"studyplan\",\"format\":\"studyplan\"", StringComparison.Ordinal);
    Throws(() => StudyPlanImporter.Parse(json), "duplicado");
}

static void ImporterRejectsDuplicateSessionIds()
{
    Throws(() => StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30",
        SessionJson("same", "2026-09-01", 60),
        SessionJson("same", "2026-09-02", 60))), "duplicado");
}

static void ImporterRejectsInvalidDate()
{
    Throws(() => StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-02-30", 60))), "AAAA-MM-DD");
}

static void ImporterRequiresTarget()
{
    var session = SessionJson("s1", "2026-09-01", 60)
        .Replace(",\"target\":\"Resolver 10 questões e corrigir os erros\"", "", StringComparison.Ordinal);
    Throws(() => StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", session)), "target");
}

static void ImporterRestrictsReviewLabel()
{
    var session = SessionJson("s1", "2026-09-01", 60)
        .Replace("\"kind\":\"study\"", "\"kind\":\"study\",\"review_label\":\"D+1\"", StringComparison.Ordinal);
    Throws(() => StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", session)), "só pode");
}

static void ImporterProtectsReviewNamespace()
{
    Throws(() => StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1::review::1", "2026-09-01", 60))), "marcador reservado");
}

static void ImporterEnforcesDeadline()
{
    Throws(() => StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-05", SessionJson("s1", "2026-09-06", 60))), "depois da data do objetivo");
}

static void RepositoryAppliesAndPersists()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        var result = repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60))));
        True(result.Success, result.Message);
        Eq(1, repo.SessionsForDate(new DateOnly(2026, 9, 1)).Count);

        var reloaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq(1, reloaded.SessionsForDate(new DateOnly(2026, 9, 1)).Count);
        Eq("plan-a", reloaded.Settings.ActivePlanId);
    });
}

static void RepositoryKeepsRevisionMonotonic()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("a1", "2026-09-01", 60)))).Success);
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-b", 1, "2026-09-30", SessionJson("b1", "2026-09-02", 60)))).Success);
        var stale = repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("a2", "2026-09-03", 60))));
        True(!stale.Success, "stale revision unexpectedly accepted");
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 2, "2026-09-30", SessionJson("a2", "2026-09-03", 60)))).Success);
    });
}

static void RepositoryRejectsPastOnly()
{
    WithRepository(new DateTime(2026, 9, 2, 9, 0, 0), (repo, _, _) =>
    {
        var result = repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("past", 1, "2026-09-30", SessionJson("p1", "2026-09-01", 60))));
        True(!result.Success, "past-only plan unexpectedly accepted");
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 1)).Count);
    });
}

static void CompletionUsesActualDate()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, clock) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 90)))).Success);
        clock.Value = new DateTime(2026, 9, 2, 18, 30, 0);
        True(repo.MarkCompleted("plan-a", "s1"));
        Eq(1, repo.SessionsForDate(new DateOnly(2026, 9, 3)).Count);
        Eq(1, repo.SessionsForDate(new DateOnly(2026, 9, 5)).Count);
        Eq(1, repo.SessionsForDate(new DateOnly(2026, 9, 9)).Count);
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 2)).Count(s => s.Kind == "review"));
        Eq(30, repo.SessionsForDate(new DateOnly(2026, 9, 3)).Single().Minutes);
    });
}

static void ReviewDoesNotRecurse()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, clock) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("s1", "2026-09-01", 60)))).Success);
        True(repo.MarkCompleted("plan-a", "s1"));
        var review = repo.SessionsForDate(new DateOnly(2026, 9, 2)).Single(s => s.Kind == "review");
        clock.Value = new DateTime(2026, 9, 2, 10, 0, 0);
        True(repo.MarkCompleted(review.PlanId, review.Id));
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 3)).Count);
    });
}

static void AssessmentDoesNotCreateReviews()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("exam", "2026-09-01", 60, "assessment")))).Success);
        True(repo.MarkCompleted("plan-a", "exam"));
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 2)).Count);
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 4)).Count);
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 8)).Count);
    });
}

static void NewPlanPreservesProtectedState()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        var a = PlanJson("plan-a", 1, "2026-09-30",
            SessionJson("done", "2026-09-01", 60),
            SessionJson("pending", "2026-09-03", 60));
        True(repo.ApplyPlan(StudyPlanImporter.Parse(a)).Success);
        True(repo.MarkCompleted("plan-a", "done"));

        var b = PlanJson("plan-b", 1, "2026-09-30", SessionJson("new", "2026-09-02", 60));
        True(repo.ApplyPlan(StudyPlanImporter.Parse(b)).Success);

        True(repo.SessionsForDate(new DateOnly(2026, 9, 1)).Single().IsCompleted, "completed history was not preserved");
        True(repo.SessionsForDate(new DateOnly(2026, 9, 2)).Any(s => s.Origin == "runtime"), "runtime review was not preserved");
        True(repo.SessionsForDate(new DateOnly(2026, 9, 2)).Any(s => s.PlanId == "plan-b"), "new plan was not applied");
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 3)).Count);
    });
}

static void IdentityIsScopedToPlan()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("shared", "2026-09-01", 60)))).Success);
        True(repo.MarkCompleted("plan-a", "shared"));
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-b", 1, "2026-09-30", SessionJson("shared", "2026-09-02", 60)))).Success);
        True(repo.SessionsForDate(new DateOnly(2026, 9, 1)).Single(s => s.PlanId == "plan-a").IsCompleted);
        True(repo.SessionsForDate(new DateOnly(2026, 9, 2)).Any(s => s.PlanId == "plan-b" && s.Id == "shared"));
        True(repo.MarkCompleted("plan-b", "shared"));
        True(repo.SessionsForDate(new DateOnly(2026, 9, 1)).Single(s => s.PlanId == "plan-a").IsCompleted);
    });
}

static void CapacityIncludesRuntime()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("long", "2026-09-01", 300)))).Success);
        True(repo.MarkCompleted("plan-a", "long"));
        var result = repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-b", 1, "2026-09-30", SessionJson("heavy", "2026-09-02", 280))));
        True(!result.Success, "capacity check ignored the runtime review");
        True(result.Message.Contains("ultrapassa", StringComparison.OrdinalIgnoreCase));
    });
}

static void PreviewDoesNotMutate()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("a1", "2026-09-01", 60)))).Success);
        var before = File.ReadAllText(path);
        var preview = repo.PreviewPlan(StudyPlanImporter.Parse(PlanJson("plan-b", 1, "2026-09-30", SessionJson("b1", "2026-09-02", 60))));
        True(preview.Success, preview.Message);
        Eq(before, File.ReadAllText(path));
        Eq("plan-a", repo.Settings.ActivePlanId);
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 2)).Count);
    });
}

static void FailedPersistenceIsTransactional()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("a1", "2026-09-01", 60)))).Success);
        var before = File.ReadAllText(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ThrowsType<IOException>(() => repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-b", 1, "2026-09-30", SessionJson("b1", "2026-09-02", 60)))));
        }
        Eq("plan-a", repo.Settings.ActivePlanId);
        Eq(1, repo.SessionsForDate(new DateOnly(2026, 9, 1)).Count);
        Eq(0, repo.SessionsForDate(new DateOnly(2026, 9, 2)).Count);
        Eq(before, File.ReadAllText(path));
        Eq(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    });
}

static void RepositoryRecoversAtomicBackup()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("a1", "2026-09-01", 60)))).Success);
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-b", 1, "2026-09-30", SessionJson("b1", "2026-09-02", 60)))).Success);
        True(File.Exists(path + ".bak"), "atomic recovery backup was not created");
        File.WriteAllText(path, "{invalid");

        var recovered = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq("plan-a", recovered.Settings.ActivePlanId);
        Eq(1, recovered.SessionsForDate(new DateOnly(2026, 9, 1)).Count);
        Eq(0, recovered.SessionsForDate(new DateOnly(2026, 9, 2)).Count);
        Contains(recovered.LastLoadWarning, "recuperou");
        True(Directory.GetFiles(Path.GetDirectoryName(path)!, "desktop-state.corrupt-*.json").Length == 1, "corrupt state was not preserved");
    });
}

static void RepositoryPreservesCorruptState()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(!File.Exists(path + ".bak"), "fresh state unexpectedly has a backup");
        File.WriteAllText(path, "{invalid");
        var recovered = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq(0, recovered.SessionsForDate(new DateOnly(2026, 9, 1)).Count);
        Contains(recovered.LastLoadWarning, "estado novo");
        True(Directory.GetFiles(Path.GetDirectoryName(path)!, "desktop-state.corrupt-*.json").Length == 1, "corrupt state was not preserved");
    });
}

static void PreferencesRejectUnsafeText()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        var before = repo.Settings.ObjectiveName;
        Throws(() => repo.SavePreferences(new string('x', 121), "", 5, 60, true, true, true), "no máximo");
        Throws(() => repo.SavePreferences("Objetivo\nquebrado", "", 5, 60, true, true, true), "controle");
        Eq(before, repo.Settings.ObjectiveName);
    });
}

static void PreferencesPersistAndReload()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        repo.SavePreferences("Vestibular", "2026-12-15", 8, 75, false, true, false);
        var loaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq("Vestibular", loaded.Settings.ObjectiveName);
        Eq("2026-12-15", loaded.Settings.ObjectiveDate);
        Eq(8, loaded.Settings.DailyHours);
        Eq(75, loaded.Settings.BlockMinutes);
        True(!loaded.Settings.ReviewD1 && loaded.Settings.ReviewD3 && !loaded.Settings.ReviewD7);
    });
}

static void StoredStateRejectsUnknownProperties()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        var original = File.ReadAllText(path);
        var json = "{\"Unexpected\":true," + original[1..];
        File.WriteAllText(path, json);
        var recovered = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Contains(recovered.LastLoadWarning, "estado novo");
        True(Directory.GetFiles(Path.GetDirectoryName(path)!, "desktop-state.corrupt-*.json").Length == 1);
    });
}

static void ExportedBackupReloads()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson("plan-a", 1, "2026-09-30", SessionJson("a1", "2026-09-01", 60)))).Success);
        var backup = Path.Combine(Path.GetDirectoryName(path)!, "export.json");
        repo.ExportBackup(backup);
        var loaded = new StudyRepository(backup, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq("plan-a", loaded.Settings.ActivePlanId);
        Eq(1, loaded.SessionsForDate(new DateOnly(2026, 9, 1)).Count);
    });
}

static void DesktopWindowsLoad()
{
    var dir = Path.Combine(Path.GetTempPath(), "RotaDesktopUiTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        App? app = null;
        try
        {
            app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            var repo = new StudyRepository(Path.Combine(dir, "state.json"), () => new DateTime(2026, 9, 1, 9, 0, 0));
            var assistant = new TestAiAssistantController();
            var installation = new TestAiInstallationController();
            var application = new TestAiProposalApplicationService();
            var assistantWindow = new AiAssistantWindow(assistant, installation, application, repo);
            Eq(0, assistant.InitializeCalls);
            Eq(0, installation.PrepareCalls);
            var windows = new Window[]
            {
                new MainWindow(repo, assistant, installation, application),
                assistantWindow,
                new AiProposalConfirmationWindow(new AiPreparedApplication
                {
                    ConfirmationId = Guid.NewGuid(),
                    ProposalId = Guid.NewGuid(),
                    Summary = "Prévia de teste",
                    Preview = new AiProposalPreview
                    {
                        ProposalId = Guid.NewGuid(),
                        Kind = AiProposalKind.StudyPlan,
                        State = AiProposalPreviewState.Ready,
                        Message = "Pronta para revisão."
                    }
                }),
                new AiInstallationWindow(installation),
                new AiPromptWindow(repo),
                new ImportPlanWindow(repo),
                new SettingsWindow(repo)
            };
            foreach (var window in windows)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20_000;
                window.Top = -20_000;
                window.ShowInTaskbar = false;
                window.Show();
                window.UpdateLayout();
                if (window is MainWindow mainWindow)
                {
                    var previousMonth = mainWindow.FindName("PreviousMonthButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("previous-month icon button was not created");
                    var nextMonth = mainWindow.FindName("NextMonthButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("next-month icon button was not created");
                    var todayNavigation = mainWindow.FindName("TodayNavigationButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("today sidebar button was not created");
                    True(previousMonth.MinWidth >= 40 && previousMonth.MinHeight >= 40, "previous-month click target is too small");
                    True(nextMonth.MinWidth >= 40 && nextMonth.MinHeight >= 40, "next-month click target is too small");
                    True(todayNavigation.MinHeight >= 44, "sidebar click target is too small");
                    var aiNavigation = mainWindow.FindName("AiNavigationButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI assistant sidebar button was not created");
                    True(aiNavigation.MinHeight >= 44, "AI assistant sidebar click target is too small");

                    var iconStyle = app.FindResource("IconGlyphText") as Style
                        ?? throw new InvalidOperationException("shared icon style was not loaded");
                    True(iconStyle.Setters.OfType<Setter>().Any(setter => setter.Property == System.Windows.Controls.TextBlock.FontFamilyProperty),
                        "shared icon style does not define a stable icon font");
                }
                if (window is AiAssistantWindow localAssistant)
                {
                    Eq(1, assistant.InitializeCalls);
                    var request = localAssistant.FindName("RequestBox") as System.Windows.Controls.TextBox
                        ?? throw new InvalidOperationException("AI assistant request box was not created");
                    var send = localAssistant.FindName("SendButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI assistant send button was not created");
                    var quickAction = localAssistant.FindName("QuickBuildPlanButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI assistant quick action was not created");
                    var externalPrompt = localAssistant.FindName("OpenExternalPromptButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("existing external AI prompt entry point was not preserved");
                    True(!send.IsEnabled, "empty AI request must not be sent");
                    request.Text = "Tenho duas horas por dia.";
                    True(send.IsEnabled, "ready local AI should enable a non-empty request");
                    quickAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Contains(request.Text, "Monte um plano");
                    True(externalPrompt.MinHeight >= 36, "external AI prompt action is too small");
                    True(!VisualDescendants<System.Windows.Controls.Button>(localAssistant)
                        .Any(button => (button.Content?.ToString() ?? "").Contains("Aplicar", StringComparison.OrdinalIgnoreCase)),
                        "AI preview screen must not expose an apply action");
                }
                if (window is AiInstallationWindow installationWindow)
                {
                    Eq(1, installation.PrepareCalls);
                    Eq(0, installation.InstallCalls);
                    var planPanel = installationWindow.FindName("PlanPanel") as System.Windows.Controls.Border
                        ?? throw new InvalidOperationException("AI installation plan was not created");
                    var installButton = installationWindow.FindName("InstallButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI install button was not created");
                    Eq(Visibility.Visible, planPanel.Visibility);
                    True(installButton.IsEnabled, "reviewed AI installation should be ready for explicit confirmation");
                    True(installButton.MinHeight >= 40, "AI install click target is too small");
                }
                window.Close();
            }

            ThemeManager.Apply(ThemeManager.Light);
            var unavailable = new TestAiAssistantController(AiInstallationState.NotInstalled);
            var unavailableWindow = new AiAssistantWindow(unavailable, installation, application, repo)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            unavailableWindow.Show();
            unavailableWindow.UpdateLayout();
            var unavailableRequest = unavailableWindow.FindName("RequestBox") as System.Windows.Controls.TextBox
                ?? throw new InvalidOperationException("unavailable AI request box was not created");
            var unavailableSend = unavailableWindow.FindName("SendButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("unavailable AI send button was not created");
            var installationNotice = unavailableWindow.FindName("InstallationNotice") as System.Windows.Controls.Border
                ?? throw new InvalidOperationException("AI installation notice was not created");
            unavailableRequest.Text = "Monte um plano.";
            True(!unavailableSend.IsEnabled, "AI request must stay disabled without a local installation");
            Eq(Visibility.Visible, installationNotice.Visibility);
            unavailableWindow.Close();
            ThemeManager.Apply(ThemeManager.Dark);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            app?.Shutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    // Hosted Windows runners can take noticeably longer than a warm local machine
    // to initialize the first WPF window and load its font resources.
    if (!thread.Join(TimeSpan.FromSeconds(45)))
        throw new TimeoutException("desktop window smoke test timed out");
    try
    {
        if (failure is not null) throw new InvalidOperationException("desktop window smoke failed", failure);
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

static void PromptExplainsExecutionModel()
{
    var settings = new AppSettings { ObjectiveName = "IFF", ObjectiveDate = "2026-11-22", DailyHours = 5, BlockMinutes = 60, ActivePlanId = "iff-2026", ActivePlanRevision = 3 };
    var prompt = AiPromptBuilder.Build("Tenho dificuldade em frações.", new DateOnly(2026, 9, 1), settings);
    Contains(prompt, "CADA objeto de sessions em uma sessão literal");
    Contains(prompt, "não completa, adivinha nem cria sessões");
    Contains(prompt, "DATA REAL da conclusão");
    Contains(prompt, "::review::");
    Contains(prompt, "histórico protegido");
    Contains(prompt, "Tenho dificuldade em frações.");
    Contains(prompt, "AUDITORIA OBRIGATÓRIA");
}

static void AiProfilesAreStable()
{
    Eq("Automatic,Lightweight,Balanced,Performance", string.Join(',', Enum.GetNames<AiProfile>()));
    var defaults = new AiConfiguration();
    Eq(AiProfile.Automatic, defaults.Profile);
    Eq(AiComputePreference.Automatic, defaults.ComputePreference);
    Eq(AiInstallationState.NotInstalled, defaults.InstallationState);
    Eq(4096, defaults.ContextSize);
}

static void AiConfigurationSerializationRoundTrips()
{
    WithAiStore((store, path) =>
    {
        var directory = Path.GetDirectoryName(path)!;
        var expected = new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ModelId = "qwen-balanced-q4",
            ModelPath = Path.Combine(directory, "models", "qwen-balanced-q4.gguf"),
            RuntimePath = Path.Combine(directory, "runtime", "llama-server.exe"),
            ContextSize = 8192,
            ComputePreference = AiComputePreference.Gpu,
            InstallationState = AiInstallationState.Ready
        };

        store.SaveAsync(expected).GetAwaiter().GetResult();
        var serialized = File.ReadAllText(path);
        Contains(serialized, "\"Profile\": \"Balanced\"");
        Contains(serialized, "\"ComputePreference\": \"Gpu\"");

        var loaded = store.LoadAsync().GetAwaiter().GetResult();
        Eq(expected, loaded);
    });
}

static void AiConfigurationUsesSeparateStorage()
{
    using var store = new AiConfigurationStore();
    Eq("config.json", Path.GetFileName(store.ConfigurationPath));
    Eq("AI", new DirectoryInfo(Path.GetDirectoryName(store.ConfigurationPath)!).Name);
    True(!store.ConfigurationPath.EndsWith("desktop-state.json", StringComparison.OrdinalIgnoreCase));
}

static void AiConfigurationRecoversAtomicBackup()
{
    WithAiStore((store, path) =>
    {
        var first = new AiConfiguration { Profile = AiProfile.Lightweight, ContextSize = 2048 };
        var second = new AiConfiguration { Profile = AiProfile.Performance, ContextSize = 16384 };
        store.SaveAsync(first).GetAwaiter().GetResult();
        store.SaveAsync(second).GetAwaiter().GetResult();
        True(File.Exists(path + ".bak"), "AI configuration backup was not created");
        File.WriteAllText(path, "{invalid");

        var recovered = store.LoadAsync().GetAwaiter().GetResult();
        Eq(first, recovered);
        Contains(store.LastLoadWarning, "recuperou");
        True(Directory.GetFiles(Path.GetDirectoryName(path)!, "config.corrupt-*.json").Length == 1,
            "invalid AI configuration was not preserved");
    });
}

static void AiValidationRejectsInvalidContracts()
{
    Throws(() => AiContractValidator.ValidateInput(new AiAssistantInput()), "ao menos");
    Throws(() => AiContractValidator.ValidateInput(new AiAssistantInput
    {
        FreeText = "Monte um plano.",
        AvailableHoursPerDay = -1
    }), "maiores que zero");
    Throws(() => AiContractValidator.ValidateInput(new AiAssistantInput
    {
        FreeText = "Monte um plano.",
        ExamDate = "2030-02-30"
    }), "data válida");
    Throws(() => AiContractValidator.ValidateConfiguration(new AiConfiguration
    {
        Profile = (AiProfile)999
    }), "perfil");
    Throws(() => AiContractValidator.ValidateConfiguration(new AiConfiguration
    {
        InstallationState = AiInstallationState.Ready
    }), "runtime");
}

static void FakeAiBuildsStudyPlanProposal()
{
    WithAiStore((store, _) =>
    {
        var backend = new FakeLocalAiBackend();
        var service = new AiPlanningService(backend, store);
        var proposal = service.CreateProposalAsync(
            ValidAiInput(),
            AiProposalKind.StudyPlan).GetAwaiter().GetResult();

        Eq(1, backend.CallCount);
        Eq(AiProposalStatus.Pending, proposal.Status);
        Eq(AiProposalKind.StudyPlan, proposal.Kind);
        True(proposal.StudyPlan is not null && proposal.Changes is null);
        var parsed = StudyPlanImporter.Parse(proposal.StudyPlan!.StudyPlanJson);
        Eq("fake-local-ai-plan", parsed.PlanId);
        Eq(1, parsed.Sessions.Count);
    });
}

static void FakeAiBuildsChangeProposal()
{
    WithAiStore((store, _) =>
    {
        var backend = new FakeLocalAiBackend();
        var service = new AiPlanningService(backend, store);
        var proposal = service.CreateProposalAsync(
            ValidAiInput(),
            AiProposalKind.PlanChanges).GetAwaiter().GetResult();

        Eq(AiProposalKind.PlanChanges, proposal.Kind);
        True(proposal.Changes is not null && proposal.StudyPlan is null);
        Eq(1, proposal.Changes!.Operations.Count);
        Eq(AiPlanOperationType.RedistributeLoad, proposal.Changes.Operations[0].Type);
        Eq(3d, proposal.Changes.Operations[0].MaxHoursPerDay);
    });
}

static void FakeAiIsDeterministic()
{
    var backend = new FakeLocalAiBackend();
    var first = backend.CreateProposalAsync(
        ValidAiInput(),
        AiProposalKind.StudyPlan,
        new AiConfiguration()).GetAwaiter().GetResult();
    var second = backend.CreateProposalAsync(
        ValidAiInput(),
        AiProposalKind.StudyPlan,
        new AiConfiguration()).GetAwaiter().GetResult();

    Eq(first.Id, second.Id);
    Eq(first.CreatedAtUtc, second.CreatedAtUtc);
    Eq(first.Summary, second.Summary);
    Eq(first.StudyPlan!.StudyPlanJson, second.StudyPlan!.StudyPlanJson);
}

static void AiPlanningHonorsCancellation()
{
    WithAiStore((store, _) =>
    {
        var backend = new FakeLocalAiBackend();
        var service = new AiPlanningService(backend, store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ThrowsType<OperationCanceledException>(() => service.CreateProposalAsync(
            ValidAiInput(),
            AiProposalKind.StudyPlan,
            cancellation.Token).GetAwaiter().GetResult());
        Eq(0, backend.CallCount);
    });
}

static void AiBackendFailureIsControlled()
{
    WithAiStore((store, _) =>
    {
        var backend = new FakeLocalAiBackend { SimulateFailure = true };
        var service = new AiPlanningService(backend, store);
        try
        {
            service.CreateProposalAsync(ValidAiInput(), AiProposalKind.StudyPlan).GetAwaiter().GetResult();
        }
        catch (AiPlanningException ex)
        {
            Contains(ex.Message, "não conseguiu preparar");
            True(ex.InnerException is InvalidOperationException, "controlled failure did not preserve its cause");
            return;
        }

        throw new InvalidOperationException("expected controlled AI planning failure was not thrown");
    });
}

static void AiProposalDoesNotMutateStudyState()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repository, statePath, _) =>
    {
        True(repository.ApplyPlan(StudyPlanImporter.Parse(PlanJson(
            "protected-plan",
            1,
            "2026-09-30",
            SessionJson("protected-session", "2026-09-02", 60)))).Success);
        var before = File.ReadAllText(statePath);

        WithAiStore((store, _) =>
        {
            var service = new AiPlanningService(new FakeLocalAiBackend(), store);
            var proposal = service.CreateProposalAsync(
                ValidAiInput(),
                AiProposalKind.PlanChanges).GetAwaiter().GetResult();
            Eq(AiProposalStatus.Pending, proposal.Status);
        });

        Eq(before, File.ReadAllText(statePath));
        Eq("protected-plan", repository.Settings.ActivePlanId);
        Eq(1, repository.SessionsForDate(new DateOnly(2026, 9, 2)).Count);
    });
}

static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
{
    for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
        if (child is T match) yield return match;
        foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
    }
}

static void HardwarePolicySelectsLightweight()
{
    var hardware = HardwareSnapshot(systemMemoryGiB: 8, logicalProcessors: 8, gpuMemoryGiB: null);
    Eq(AiProfile.Lightweight, AiProfileRecommendationPolicy.Recommend(hardware));
}

static void HardwarePolicySelectsBalancedCpuFallback()
{
    var hardware = HardwareSnapshot(systemMemoryGiB: 16, logicalProcessors: 8, gpuMemoryGiB: null);
    Eq(AiProfile.Balanced, AiProfileRecommendationPolicy.Recommend(hardware));
}

static void HardwarePolicySelectsPerformanceForTargetPc()
{
    var hardware = HardwareSnapshot(
        systemMemoryGiB: 16,
        logicalProcessors: 16,
        gpuMemoryGiB: 8,
        cpuName: "AMD Ryzen 7 5700X",
        gpuName: "NVIDIA GeForce RTX 3070");
    Eq(AiProfile.Performance, AiProfileRecommendationPolicy.Recommend(hardware));
}

static void HardwarePolicyCapsAtPerformance()
{
    var hardware = HardwareSnapshot(systemMemoryGiB: 64, logicalProcessors: 32, gpuMemoryGiB: 24);
    Eq(AiProfile.Performance, AiProfileRecommendationPolicy.Recommend(hardware));
    True(Enum.GetValues<AiProfile>().Max() == AiProfile.Performance, "a profile above Performance was introduced");
}

static void HardwarePolicyHonorsBoundaries()
{
    var exactBalanced = new WindowsHardwareSnapshot(
        "CPU de teste",
        AiProfileRecommendationPolicy.BalancedMinimumLogicalProcessors,
        AiProfileRecommendationPolicy.BalancedMinimumSystemMemoryBytes,
        "",
        null,
        Array.Empty<string>());
    Eq(AiProfile.Balanced, AiProfileRecommendationPolicy.Recommend(exactBalanced));

    var belowBalancedMemory = exactBalanced with
    {
        LogicalProcessorCount = 64,
        SystemMemoryBytes = AiProfileRecommendationPolicy.BalancedMinimumSystemMemoryBytes - 1,
        GpuName = "GPU de teste",
        DedicatedGpuMemoryBytes = 24 * AiProfileRecommendationPolicy.Gibibyte
    };
    Eq(AiProfile.Lightweight, AiProfileRecommendationPolicy.Recommend(belowBalancedMemory));

    var exactPerformance = new WindowsHardwareSnapshot(
        "CPU de teste",
        AiProfileRecommendationPolicy.PerformanceMinimumLogicalProcessors,
        AiProfileRecommendationPolicy.PerformanceMinimumSystemMemoryBytes,
        "GPU de teste",
        AiProfileRecommendationPolicy.PerformanceMinimumGpuMemoryBytes,
        Array.Empty<string>());
    Eq(AiProfile.Performance, AiProfileRecommendationPolicy.Recommend(exactPerformance));

    var belowPerformanceGpu = exactPerformance with
    {
        DedicatedGpuMemoryBytes = AiProfileRecommendationPolicy.PerformanceMinimumGpuMemoryBytes - 1
    };
    Eq(AiProfile.Balanced, AiProfileRecommendationPolicy.Recommend(belowPerformanceGpu));
}

static void HardwarePolicyRejectsImpossibleSnapshots()
{
    Throws(() => AiProfileRecommendationPolicy.Recommend(new WindowsHardwareSnapshot(
        "CPU inválida", 0, 8 * AiProfileRecommendationPolicy.Gibibyte, "", null, Array.Empty<string>())),
        "processadores lógicos");
    Throws(() => AiProfileRecommendationPolicy.Recommend(new WindowsHardwareSnapshot(
        "CPU inválida", 4, 0, "", null, Array.Empty<string>())),
        "memória física");
    Throws(() => AiProfileRecommendationPolicy.Recommend(new WindowsHardwareSnapshot(
        "CPU inválida", 4, 8 * AiProfileRecommendationPolicy.Gibibyte, "GPU inválida", -1, Array.Empty<string>())),
        "não pode ser negativa");
}

static void HardwarePolicyResolvesAutomaticAndManual()
{
    var detected = new AiHardwareProfile(
        "CPU de teste",
        16,
        16 * AiProfileRecommendationPolicy.Gibibyte,
        "GPU de teste",
        8 * AiProfileRecommendationPolicy.Gibibyte,
        AiProfile.Performance,
        Array.Empty<string>());

    Eq(AiProfile.Performance, AiProfileRecommendationPolicy.Resolve(AiProfile.Automatic, detected));
    Eq(AiProfile.Lightweight, AiProfileRecommendationPolicy.Resolve(AiProfile.Lightweight, detected));
    Eq(AiProfile.Balanced, AiProfileRecommendationPolicy.Resolve(AiProfile.Balanced, detected));
    Eq(AiProfile.Performance, AiProfileRecommendationPolicy.Resolve(AiProfile.Performance, detected));
}

static void HardwareDetectorMapsSnapshot()
{
    var snapshot = HardwareSnapshot(
        systemMemoryGiB: 16,
        logicalProcessors: 12,
        gpuMemoryGiB: 6,
        cpuName: "CPU intermediária",
        gpuName: "GPU intermediária",
        warnings: new[] { "Aviso controlado." });
    var probe = new FakeWindowsHardwareProbe(snapshot);
    var detector = new WindowsAiHardwareProfileDetector(probe);

    var detected = detector.DetectAsync().GetAwaiter().GetResult();
    Eq(1, probe.CallCount);
    Eq(snapshot.CpuName, detected.CpuName);
    Eq(snapshot.LogicalProcessorCount, detected.LogicalProcessorCount);
    Eq(snapshot.SystemMemoryBytes, detected.SystemMemoryBytes);
    Eq(snapshot.GpuName, detected.GpuName);
    Eq(snapshot.DedicatedGpuMemoryBytes, detected.DedicatedGpuMemoryBytes);
    Eq(AiProfile.Balanced, detected.RecommendedProfile);
    Eq("Aviso controlado.", detected.Warnings.Single());
}

static void HardwareDetectorHonorsCancellation()
{
    var probe = new FakeWindowsHardwareProbe(HardwareSnapshot(16, 16, 8));
    var detector = new WindowsAiHardwareProfileDetector(probe);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    ThrowsType<OperationCanceledException>(() =>
        detector.DetectAsync(cancellation.Token).GetAwaiter().GetResult());
    Eq(0, probe.CallCount);
}

static void HardwareDetectorReadsRealSnapshot()
{
    var detected = new WindowsAiHardwareProfileDetector().DetectAsync().GetAwaiter().GetResult();
    True(!string.IsNullOrWhiteSpace(detected.CpuName), "CPU name was not detected");
    True(detected.LogicalProcessorCount > 0, "logical processor count was not detected");
    True(detected.SystemMemoryBytes > 0, "physical memory was not detected");
    True(detected.DedicatedGpuMemoryBytes is null or >= 0, "GPU memory is invalid");
    True(detected.RecommendedProfile is AiProfile.Lightweight or AiProfile.Balanced or AiProfile.Performance,
        "automatic hardware recommendation is invalid");
}

static void AiModelCatalogExposesRecommendations()
{
    var catalog = AiModelCatalog.Default;
    Eq(2, catalog.Version);
    Eq(3, catalog.Models.Count);
    Eq(3, catalog.Models.Select(model => model.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    Eq(3, catalog.Models.Select(model => model.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    foreach (var profile in new[] { AiProfile.Lightweight, AiProfile.Balanced, AiProfile.Performance })
    {
        var compatible = catalog.GetCompatibleModels(profile);
        Eq(1, compatible.Count);
        Eq(profile, compatible[0].Profile);
        Eq(compatible[0], catalog.GetRecommendedModel(profile));
        True(compatible[0].IsRecommended, $"{profile} does not have a recommended model");
        Eq(profile == AiProfile.Lightweight ? "Q8_0" : "Q4_K_M", compatible[0].Quantization);
    }

    Eq("qwen3-8b-q4-k-m", catalog.GetRecommendedModel(AiProfile.Performance).Id);
    Throws(() => catalog.GetRecommendedModel(AiProfile.Automatic), "perfil");
    Throws(() => catalog.GetById("unknown-model"), "não pertence");
}

static void AiModelCatalogRejectsUnsafeArtifactNames()
{
    var models = AiModelCatalog.Default.Models.ToArray();
    models[0] = models[0] with { FileName = @"..\outside.gguf" };
    Throws(() => _ = new AiModelCatalog(models), "inseguro");
}

static void AiModelManagerResolvesAutomaticProfile()
{
    WithAiModelRoot(root =>
    {
        var probe = new FakeWindowsHardwareProbe(HardwareSnapshot(
            systemMemoryGiB: 16,
            logicalProcessors: 16,
            gpuMemoryGiB: 8,
            cpuName: "AMD Ryzen 7 5700X",
            gpuName: "NVIDIA GeForce RTX 3070"));
        var manager = new LocalAiModelManager(
            root,
            hardwareDetector: new WindowsAiHardwareProfileDetector(probe));

        var info = manager.GetInstallationInfoAsync(new AiConfiguration()).GetAwaiter().GetResult();
        Eq(1, probe.CallCount);
        Eq(AiProfile.Performance, info.EffectiveProfile);
        Eq("qwen3-8b-q4-k-m", info.Model.Id);
        Eq(AiInstallationState.NotInstalled, info.State);
        True(!info.RuntimeAvailable && !info.ModelAvailable);
        Eq(Path.Combine(root, "runtime", LocalAiModelManager.RuntimeFileName), info.RuntimePath);
        Eq(Path.Combine(root, "models", info.Model.FileName), info.ModelPath);
        True(!Directory.Exists(root), "inspection unexpectedly created the AI directory");
    });
}

static void AiModelManagerRespectsManualProfile()
{
    WithAiModelRoot(root =>
    {
        var probe = new FakeWindowsHardwareProbe(HardwareSnapshot(16, 16, 8));
        var manager = new LocalAiModelManager(
            root,
            hardwareDetector: new WindowsAiHardwareProfileDetector(probe));

        var info = manager.GetInstallationInfoAsync(new AiConfiguration
        {
            Profile = AiProfile.Lightweight
        }).GetAwaiter().GetResult();

        Eq(0, probe.CallCount);
        Eq(AiProfile.Lightweight, info.EffectiveProfile);
        Eq("qwen3-1.7b-q8-0", info.Model.Id);
    });
}

static void AiModelManagerDerivesInstallationState()
{
    WithAiModelRoot(root =>
    {
        var manager = new LocalAiModelManager(root);
        var configuration = new AiConfiguration { Profile = AiProfile.Balanced };

        var missing = manager.GetInstallationInfoAsync(configuration).GetAwaiter().GetResult();
        Eq(AiInstallationState.NotInstalled, missing.State);

        Directory.CreateDirectory(Path.GetDirectoryName(missing.RuntimePath)!);
        File.WriteAllBytes(missing.RuntimePath, new byte[] { 1 });
        var runtimeOnly = manager.GetInstallationInfoAsync(configuration).GetAwaiter().GetResult();
        Eq(AiInstallationState.RuntimeInstalled, runtimeOnly.State);
        True(runtimeOnly.RuntimeAvailable && !runtimeOnly.ModelAvailable);

        Directory.CreateDirectory(Path.GetDirectoryName(missing.ModelPath)!);
        File.WriteAllBytes(missing.ModelPath, new byte[] { 2 });
        var ready = manager.GetInstallationInfoAsync(configuration).GetAwaiter().GetResult();
        Eq(AiInstallationState.Ready, ready.State);
        True(ready.RuntimeAvailable && ready.ModelAvailable);
        True(ready.Warnings.Any(warning => warning.Contains("desatualizado", StringComparison.Ordinal)),
            "stale persisted state was not reported");

        File.Delete(missing.RuntimePath);
        var modelOnly = manager.GetInstallationInfoAsync(configuration).GetAwaiter().GetResult();
        Eq(AiInstallationState.NotInstalled, modelOnly.State);
        True(!modelOnly.RuntimeAvailable && modelOnly.ModelAvailable);
        True(modelOnly.Warnings.Any(warning => warning.Contains("modelo existe", StringComparison.Ordinal)),
            "model-only installation was not explained");

        File.WriteAllBytes(missing.RuntimePath, Array.Empty<byte>());
        var emptyRuntime = manager.GetInstallationInfoAsync(configuration).GetAwaiter().GetResult();
        Eq(AiInstallationState.NotInstalled, emptyRuntime.State);
        True(emptyRuntime.Warnings.Any(warning => warning.Contains("runtime está vazio", StringComparison.Ordinal)),
            "empty runtime was not rejected");
    });
}

static void AiModelManagerRejectsRelativeRoot()
{
    Throws(() => _ = new LocalAiModelManager("relative-ai-root"), "caminho absoluto");
}

static void AiModelManagerRejectsIncompatibleModel()
{
    WithAiModelRoot(root =>
    {
        var manager = new LocalAiModelManager(root);
        Throws(() => manager.GetInstallationInfoAsync(new AiConfiguration
        {
            Profile = AiProfile.Lightweight,
            ModelId = "qwen3-4b-q4-k-m"
        }).GetAwaiter().GetResult(), "não é compatível");
    });
}

static void AiModelManagerHonorsCancellation()
{
    WithAiModelRoot(root =>
    {
        var probe = new FakeWindowsHardwareProbe(HardwareSnapshot(16, 16, 8));
        var manager = new LocalAiModelManager(
            root,
            hardwareDetector: new WindowsAiHardwareProfileDetector(probe));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ThrowsType<OperationCanceledException>(() => manager.GetInstallationInfoAsync(
            new AiConfiguration(),
            cancellation.Token).GetAwaiter().GetResult());
        Eq(0, probe.CallCount);
        True(!Directory.Exists(root), "cancelled inspection unexpectedly created the AI directory");
    });
}

static void AiInstallationManifestPinsArtifacts()
{
    var manifest = AiInstallationManifest.Default;
    Eq(AiInstallationManifest.CurrentManifestVersion, manifest.Version);
    Eq(2, manifest.RuntimePackages.Count);
    Eq(3, manifest.ModelPackages.Count);
    Eq("llama.cpp-b10795-win-cpu-x64", manifest.GetRuntimePackage(AiComputePreference.Cpu).Archive.Id);
    Eq("llama.cpp-b10795-win-vulkan-x64", manifest.GetRuntimePackage(AiComputePreference.Gpu).Archive.Id);
    Eq("d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785",
        manifest.GetModelPackage("qwen3-8b-q4-k-m").Artifact.Sha256);

    var artifacts = manifest.RuntimePackages.Select(package => package.Archive)
        .Concat(manifest.ModelPackages.Select(package => package.Artifact))
        .ToArray();
    Eq(5, artifacts.Select(artifact => artifact.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    foreach (var artifact in artifacts)
    {
        Eq(Uri.UriSchemeHttps, artifact.Source.Scheme);
        True(artifact.ExpectedSizeBytes > 0, "manifest contains an empty artifact");
        Eq(64, artifact.Sha256.Length);
        True(!artifact.Source.AbsolutePath.Contains("/main/", StringComparison.OrdinalIgnoreCase),
            "manifest contains an unpinned model URL");
        True(!artifact.Source.AbsolutePath.Contains("/latest/", StringComparison.OrdinalIgnoreCase),
            "manifest contains an unpinned runtime URL");
    }
}

static void AiInstallationManifestRejectsUntrustedSources()
{
    var source = AiInstallationManifest.Default;
    var insecureRuntimes = source.RuntimePackages.ToArray();
    insecureRuntimes[0] = insecureRuntimes[0] with
    {
        Archive = insecureRuntimes[0].Archive with
        {
            Source = new Uri("http://github.com/ggml-org/llama.cpp/releases/download/b10795/runtime.zip")
        }
    };
    Throws(() => _ = new AiInstallationManifest(
        source.Version,
        insecureRuntimes,
        source.ModelPackages), "origem");

    var mutableModels = source.ModelPackages.ToArray();
    mutableModels[0] = mutableModels[0] with
    {
        Artifact = mutableModels[0].Artifact with
        {
            Source = new Uri("https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q8_0.gguf")
        }
    };
    Throws(() => _ = new AiInstallationManifest(
        source.Version,
        source.RuntimePackages,
        mutableModels), "origem");
}

static void AiInstallerActivatesVerifiedFiles()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        var downloader = new FakeAiArtifactDownloader(fixture.Artifacts);
        using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
        var manager = new LocalAiModelManager(root);
        using var installer = new LocalAiInstaller(root, store, manager, downloader, fixture.Manifest);
        var progress = new InlineProgress<AiInstallationProgress>();

        var result = installer.InstallAsync(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ComputePreference = AiComputePreference.Cpu
        }, progress).GetAwaiter().GetResult();

        Eq(AiInstallationState.Ready, result.Configuration.InstallationState);
        Eq(AiInstallationState.Ready, result.InstallationInfo.State);
        Eq("qwen3-4b-q4-k-m", result.Configuration.ModelId);
        True(File.Exists(result.Configuration.RuntimePath), "installed runtime is missing");
        True(File.Exists(result.Configuration.ModelPath), "installed model is missing");
        True(result.Configuration.RuntimePath.StartsWith(result.InstallationDirectory, StringComparison.OrdinalIgnoreCase));
        True(result.Configuration.ModelPath.StartsWith(result.InstallationDirectory, StringComparison.OrdinalIgnoreCase));
        var receiptPath = Path.Combine(result.InstallationDirectory, "installation.json");
        True(File.Exists(receiptPath), "installation receipt is missing");
        var receipt = JsonSerializer.Deserialize<AiInstallationReceipt>(File.ReadAllText(receiptPath))
            ?? throw new InvalidOperationException("installation receipt is empty");
        Eq(AiInstallationReceipt.CurrentSchemaVersion, receipt.SchemaVersion);
        Eq("test-runtime-cpu", receipt.RuntimeArtifactId);
        Eq("qwen3-4b-q4-k-m", receipt.ModelId);
        Eq("test-model-balanced", receipt.ModelArtifactId);
        Eq(2, downloader.CallCount);
        Eq(fixture.Manifest.GetRuntimePackage(AiComputePreference.Cpu).Archive.Source, downloader.RequestedSources[0]);
        Eq(fixture.Manifest.GetModelPackage("qwen3-4b-q4-k-m").Artifact.Source, downloader.RequestedSources[1]);
        Eq(AiInstallationStage.Completed, progress.Values[^1].Stage);
        True(!Directory.EnumerateDirectories(installer.StagingDirectory).Any(), "staging was not cleaned after success");

        var persisted = store.LoadAsync().GetAwaiter().GetResult();
        Eq(result.Configuration, persisted);
    });
}

static void AiInstallerSelectsVulkanForAutomaticPerformance()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        var downloader = new FakeAiArtifactDownloader(fixture.Artifacts);
        var probe = new FakeWindowsHardwareProbe(HardwareSnapshot(16, 16, 8));
        using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
        var manager = new LocalAiModelManager(
            root,
            hardwareDetector: new WindowsAiHardwareProfileDetector(probe));
        using var installer = new LocalAiInstaller(root, store, manager, downloader, fixture.Manifest);

        var result = installer.InstallAsync(new AiConfiguration()).GetAwaiter().GetResult();
        Eq(AiProfile.Performance, result.InstallationInfo.EffectiveProfile);
        Eq("qwen3-8b-q4-k-m", result.Configuration.ModelId);
        Eq(fixture.Manifest.GetRuntimePackage(AiComputePreference.Gpu).Archive.Source, downloader.RequestedSources[0]);
    });
}

static void AiInstallerRejectsCorruptedArtifact()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        var corruptArtifacts = fixture.Artifacts.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var modelSource = fixture.Manifest.GetModelPackage("qwen3-4b-q4-k-m").Artifact.Source;
        corruptArtifacts[modelSource][0] ^= 0xFF;
        var downloader = new FakeAiArtifactDownloader(corruptArtifacts);
        var configurationPath = Path.Combine(root, "config.json");
        using var store = new AiConfigurationStore(configurationPath);
        var manager = new LocalAiModelManager(root);
        using var installer = new LocalAiInstaller(root, store, manager, downloader, fixture.Manifest);

        Throws(() => installer.InstallAsync(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ComputePreference = AiComputePreference.Cpu
        }).GetAwaiter().GetResult(), "SHA-256");
        True(!File.Exists(configurationPath), "corrupt installation changed the active configuration");
        True(!Directory.Exists(installer.InstallationsDirectory), "corrupt installation was activated");
        True(!Directory.EnumerateDirectories(installer.StagingDirectory).Any(), "corrupt staging was not cleaned");
    });
}

static void AiInstallerCleansCancelledStaging()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        using var cancellation = new CancellationTokenSource();
        var downloader = new FakeAiArtifactDownloader(fixture.Artifacts)
        {
            CancelBeforeCall = 2,
            CancelAction = cancellation.Cancel
        };
        var configurationPath = Path.Combine(root, "config.json");
        using var store = new AiConfigurationStore(configurationPath);
        var manager = new LocalAiModelManager(root);
        using var installer = new LocalAiInstaller(root, store, manager, downloader, fixture.Manifest);

        ThrowsType<OperationCanceledException>(() => installer.InstallAsync(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ComputePreference = AiComputePreference.Cpu
        }, cancellationToken: cancellation.Token).GetAwaiter().GetResult());
        Eq(2, downloader.CallCount);
        True(!File.Exists(configurationPath), "cancelled installation changed the active configuration");
        True(!Directory.Exists(installer.InstallationsDirectory), "cancelled installation was activated");
        True(!Directory.EnumerateDirectories(installer.StagingDirectory).Any(), "cancelled staging was not cleaned");
    });
}

static void AiInstallerRejectsArchiveTraversal()
{
    WithAiModelRoot(root =>
    {
        var maliciousRuntime = CreateRuntimeArchive(("../outside.exe", new byte[] { 1, 2, 3 }));
        var fixture = CreateInstallationFixture(maliciousRuntime);
        var downloader = new FakeAiArtifactDownloader(fixture.Artifacts);
        using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
        var manager = new LocalAiModelManager(root);
        using var installer = new LocalAiInstaller(root, store, manager, downloader, fixture.Manifest);

        Throws(() => installer.InstallAsync(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ComputePreference = AiComputePreference.Cpu
        }).GetAwaiter().GetResult(), "inseguro");
        True(!File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "outside.exe")),
            "runtime archive escaped the staging directory");
        True(!Directory.Exists(installer.InstallationsDirectory), "unsafe runtime was activated");
    });
}

static void AiInstallerRollsBackFailedActivation()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        var downloader = new FakeAiArtifactDownloader(fixture.Artifacts);
        var manager = new LocalAiModelManager(root);
        var store = new FailingAiConfigurationStore(Path.Combine(root, "config.json"));
        using var installer = new LocalAiInstaller(root, store, manager, downloader, fixture.Manifest);

        Throws(() => installer.InstallAsync(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ComputePreference = AiComputePreference.Cpu
        }).GetAwaiter().GetResult(), "sem alterar");
        True(!Directory.EnumerateDirectories(installer.InstallationsDirectory).Any(),
            "failed configuration activation left an active installation");
        True(!Directory.EnumerateDirectories(installer.StagingDirectory).Any(),
            "failed configuration activation left staging files");
    });
}

static void HttpAiDownloaderStreamsToDisk()
{
    WithAiModelRoot(root =>
    {
        Directory.CreateDirectory(root);
        var payload = Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251)).ToArray();
        using var client = new HttpClient(new StaticHttpMessageHandler(payload));
        var downloader = new HttpAiArtifactDownloader(client);
        var path = Path.Combine(root, "artifact.bin");
        var progress = new InlineProgress<AiDownloadProgress>();

        downloader.DownloadAsync(new Uri("https://github.com/Rota/test/artifact.bin"), path, payload.LongLength, progress)
            .GetAwaiter().GetResult();
        True(payload.SequenceEqual(File.ReadAllBytes(path)), "HTTP downloader changed the response bytes");
        Eq(payload.LongLength, progress.Values[^1].BytesReceived);
        Eq<long?>(payload.LongLength, progress.Values[^1].TotalBytes);
    });
}

static void AiInstallerPreservesActiveInstallation()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
        var manager = new LocalAiModelManager(root);
        using var firstInstaller = new LocalAiInstaller(root, store, manager,
            new FakeAiArtifactDownloader(fixture.Artifacts), fixture.Manifest);
        var original = firstInstaller.InstallAsync(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ComputePreference = AiComputePreference.Cpu
        }).GetAwaiter().GetResult();
        var before = File.ReadAllBytes(store.ConfigurationPath);

        var corrupted = fixture.Artifacts.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        corrupted[fixture.Manifest.GetModelPackage(original.Configuration.ModelId).Artifact.Source][0] ^= 0xFF;
        using var failedInstaller = new LocalAiInstaller(root, store, manager,
            new FakeAiArtifactDownloader(corrupted), fixture.Manifest);
        Throws(() => failedInstaller.InstallAsync(original.Configuration).GetAwaiter().GetResult(), "SHA-256");
        True(before.SequenceEqual(File.ReadAllBytes(store.ConfigurationPath)), "failed update changed config");
        Eq(1, Directory.GetDirectories(firstInstaller.InstallationsDirectory).Length);
        True(File.Exists(original.Configuration.RuntimePath) && File.Exists(original.Configuration.ModelPath));
        True(!Directory.EnumerateDirectories(firstInstaller.StagingDirectory).Any());
    });
}

static void OfficialRuntimeArchivesStage()
{
    var archiveRoot = Environment.GetEnvironmentVariable("ROTA_TEST_RUNTIME_ARCHIVES")!;
    foreach (var package in AiInstallationManifest.Default.RuntimePackages)
    {
        var bytes = File.ReadAllBytes(Path.Combine(archiveRoot, package.Archive.FileName));
        Eq(package.Archive.ExpectedSizeBytes, bytes.LongLength);
        Eq(package.Archive.Sha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        WithAiModelRoot(root =>
        {
            var fixture = CreateInstallationFixture(bytes);
            using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
            using var installer = new LocalAiInstaller(root, store, new LocalAiModelManager(root),
                new FakeAiArtifactDownloader(fixture.Artifacts), fixture.Manifest);
            var result = installer.InstallAsync(new AiConfiguration
            {
                Profile = AiProfile.Balanced,
                ComputePreference = package.ComputePreference
            }).GetAwaiter().GetResult();
            using var archiveStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0))
            {
                using var expected = entry.Open();
                using var actual = File.OpenRead(Path.Combine(
                    Path.GetDirectoryName(result.Configuration.RuntimePath)!, entry.FullName));
                True(SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual)),
                    $"runtime extraction changed {entry.FullName}");
            }
            True(Directory.GetFiles(Path.GetDirectoryName(result.Configuration.RuntimePath)!, "*.dll").Length > 0);
            Eq(AiInstallationState.Ready, result.InstallationInfo.State);

            var process = new SystemAiRuntimeProcessFactory().Start(new AiRuntimeLaunchCommand(
                result.Configuration.RuntimePath,
                Path.GetDirectoryName(result.Configuration.RuntimePath)!,
                Array.AsReadOnly(new[] { "--version" })));
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                Eq(0, process.ExitCode);
                Contains(process.RecentOutput, "build 10795");
            }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                process.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}

static void AiInstallerCompletionObserverCannotFailCommit()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
        using var installer = new LocalAiInstaller(root, store, new LocalAiModelManager(root),
            new FakeAiArtifactDownloader(fixture.Artifacts), fixture.Manifest);
        using var cancellation = new CancellationTokenSource();
        var result = installer.InstallAsync(new AiConfiguration { Profile = AiProfile.Balanced },
            new CallbackInstallationProgress(value =>
            {
                if (value.Stage != AiInstallationStage.Completed) return;
                cancellation.Cancel();
                throw new OperationCanceledException("observer disposed after commit");
            }), cancellation.Token).GetAwaiter().GetResult();
        Eq(result.Configuration, store.LoadAsync().GetAwaiter().GetResult());
        True(File.Exists(result.Configuration.RuntimePath));
        Eq(AiInstallationState.Ready, result.InstallationInfo.State);
    });
}

static void AiInstallerRejectsCompetingInstance()
{
    WithAiModelRoot(root =>
    {
        var fixture = CreateInstallationFixture();
        using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
        var manager = new LocalAiModelManager(root);
        var competitorDownloads = new FakeAiArtifactDownloader(fixture.Artifacts);
        using var competitor = new LocalAiInstaller(root, store, manager, competitorDownloads, fixture.Manifest);
        using var installer = new LocalAiInstaller(root, store, manager,
            new FakeAiArtifactDownloader(fixture.Artifacts), fixture.Manifest);
        var attempted = false;
        var result = installer.InstallAsync(new AiConfiguration { Profile = AiProfile.Balanced },
            new CallbackInstallationProgress(value =>
            {
                if (attempted || value.Stage != AiInstallationStage.DownloadingRuntime) return;
                attempted = true;
                ThrowsType<AiInstallationException>(() => competitor.InstallAsync(
                    new AiConfiguration { Profile = AiProfile.Balanced }).GetAwaiter().GetResult());
            })).GetAwaiter().GetResult();
        True(attempted);
        Eq(0, competitorDownloads.CallCount);
        Eq(result.Configuration, store.LoadAsync().GetAwaiter().GetResult());
        Eq(1, Directory.GetDirectories(installer.InstallationsDirectory).Length);
    });
}

static WindowsHardwareSnapshot HardwareSnapshot(
    int systemMemoryGiB,
    int logicalProcessors,
    int? gpuMemoryGiB,
    string cpuName = "CPU de teste",
    string gpuName = "",
    IReadOnlyList<string>? warnings = null) => new(
        cpuName,
        logicalProcessors,
        systemMemoryGiB * AiProfileRecommendationPolicy.Gibibyte,
        gpuName,
        gpuMemoryGiB.HasValue ? gpuMemoryGiB.Value * AiProfileRecommendationPolicy.Gibibyte : null,
        warnings ?? Array.Empty<string>());

static AiAssistantInput ValidAiInput() => new()
{
    ObjectiveOrExam = "ENEM",
    ExamDate = "2030-11-03",
    AvailableHoursPerDay = 3,
    AvailableDays = new List<DayOfWeek>
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    },
    StrongSubjects = new List<string> { "História" },
    WeakSubjects = new List<string> { "Matemática", "Química" },
    Goal = "Preparar um plano equilibrado.",
    FreeText = "Tenho três horas por dia para estudar."
};

static void WithAiStore(Action<AiConfigurationStore, string> action)
{
    var directory = Path.Combine(Path.GetTempPath(), "RotaDesktopAiTests", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "AI", "config.json");
    Directory.CreateDirectory(directory);
    try
    {
        using var store = new AiConfigurationStore(path);
        action(store, path);
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void WithAiModelRoot(Action<string> action)
{
    var parent = Path.Combine(Path.GetTempPath(), "RotaDesktopAiModelTests", Guid.NewGuid().ToString("N"));
    var root = Path.Combine(parent, "AI");
    Directory.CreateDirectory(parent);
    try
    {
        action(root);
    }
    finally
    {
        try { Directory.Delete(parent, recursive: true); } catch { }
    }
}

static InstallationFixture CreateInstallationFixture(byte[]? runtimeArchive = null)
{
    runtimeArchive ??= CreateRuntimeArchive(
        (LocalAiModelManager.RuntimeFileName, new byte[] { 1, 2, 3, 4 }),
        ("ggml-test.dll", new byte[] { 5, 6, 7 }));
    var lightweightModel = new byte[] { 11, 12, 13 };
    var balancedModel = new byte[] { 21, 22, 23, 24 };
    var performanceModel = new byte[] { 31, 32, 33, 34, 35 };

    var cpu = TestArtifact(
        "test-runtime-cpu",
        "https://github.com/Rota/test/releases/download/v1/runtime-cpu.zip",
        "runtime-cpu.zip",
        runtimeArchive);
    var gpu = TestArtifact(
        "test-runtime-gpu",
        "https://github.com/Rota/test/releases/download/v1/runtime-gpu.zip",
        "runtime-gpu.zip",
        runtimeArchive);
    var lightweight = TestArtifact(
        "test-model-lightweight",
        "https://huggingface.co/Qwen/test/resolve/1111111111111111111111111111111111111111/Qwen3-1.7B-Q8_0.gguf",
        "Qwen3-1.7B-Q8_0.gguf",
        lightweightModel);
    var balanced = TestArtifact(
        "test-model-balanced",
        "https://huggingface.co/Qwen/test/resolve/2222222222222222222222222222222222222222/Qwen3-4B-Q4_K_M.gguf",
        "Qwen3-4B-Q4_K_M.gguf",
        balancedModel);
    var performance = TestArtifact(
        "test-model-performance",
        "https://huggingface.co/Qwen/test/resolve/3333333333333333333333333333333333333333/Qwen3-8B-Q4_K_M.gguf",
        "Qwen3-8B-Q4_K_M.gguf",
        performanceModel);

    var manifest = new AiInstallationManifest(
        AiInstallationManifest.CurrentManifestVersion,
        new[]
        {
            new AiRuntimePackage(AiComputePreference.Cpu, cpu, LocalAiModelManager.RuntimeFileName),
            new AiRuntimePackage(AiComputePreference.Gpu, gpu, LocalAiModelManager.RuntimeFileName)
        },
        new[]
        {
            new AiModelPackage("qwen3-1.7b-q8-0", lightweight),
            new AiModelPackage("qwen3-4b-q4-k-m", balanced),
            new AiModelPackage("qwen3-8b-q4-k-m", performance)
        });
    var artifacts = new Dictionary<Uri, byte[]>
    {
        [cpu.Source] = runtimeArchive,
        [gpu.Source] = runtimeArchive,
        [lightweight.Source] = lightweightModel,
        [balanced.Source] = balancedModel,
        [performance.Source] = performanceModel
    };
    return new InstallationFixture(manifest, artifacts);
}

static AiDownloadArtifact TestArtifact(string id, string source, string fileName, byte[] content) => new(
    id,
    new Uri(source),
    fileName,
    content.LongLength,
    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

static byte[] CreateRuntimeArchive(params (string Path, byte[] Content)[] entries)
{
    using var output = new MemoryStream();
    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Path, CompressionLevel.Fastest);
            using var stream = entry.Open();
            stream.Write(item.Content);
        }
    }
    return output.ToArray();
}

static void WithRepository(DateTime initialNow, Action<StudyRepository, string, MutableClock> action)
{
    var dir = Path.Combine(Path.GetTempPath(), "RotaDesktopTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "state.json");
    var clock = new MutableClock(initialNow);
    try
    {
        var repo = new StudyRepository(path, () => clock.Value);
        action(repo, path, clock);
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

static string PlanJson(string planId, int revision, string objectiveDate, params string[] sessions)
{
    return "{" +
        "\"format\":\"studyplan\"," +
        "\"format_version\":\"0.2\"," +
        "\"plan\":{" +
            "\"id\":" + JsonSerializer.Serialize(planId) + "," +
            "\"revision\":" + revision + "," +
            "\"title\":\"Plano de teste\"}," +
        "\"objective\":{" +
            "\"name\":\"Objetivo de teste\"," +
            "\"date\":" + JsonSerializer.Serialize(objectiveDate) + "}," +
        "\"sessions\":[" + string.Join(",", sessions) + "]}";
}

static string SessionJson(string id, string date, int minutes, string kind = "study")
{
    return "{" +
        "\"id\":" + JsonSerializer.Serialize(id) + "," +
        "\"date\":" + JsonSerializer.Serialize(date) + "," +
        "\"subject\":\"Matemática\"," +
        "\"topic\":\"Frações e proporções\"," +
        "\"minutes\":" + minutes + "," +
        "\"target\":\"Resolver 10 questões e corrigir os erros\"," +
        "\"kind\":" + JsonSerializer.Serialize(kind) + "}";
}

static void True(bool condition, string message = "assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Eq<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected [{expected}] but got [{actual}]");
}

static void Contains(string text, string fragment)
{
    if (!text.Contains(fragment, StringComparison.Ordinal))
        throw new InvalidOperationException($"missing fragment: {fragment}");
}

static void Throws(Action action, string fragment)
{
    try { action(); }
    catch (Exception ex)
    {
        if (ex.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException($"wrong exception: {ex.Message}");
    }
    throw new InvalidOperationException("expected exception was not thrown");
}

static void ThrowsType<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    catch (Exception ex) { throw new InvalidOperationException($"wrong exception type: {ex.GetType().Name}: {ex.Message}"); }
    throw new InvalidOperationException($"expected {typeof(T).Name} was not thrown");
}

sealed class MutableClock
{
    public MutableClock(DateTime value) => Value = value;
    public DateTime Value { get; set; }
}

sealed record InstallationFixture(
    AiInstallationManifest Manifest,
    IReadOnlyDictionary<Uri, byte[]> Artifacts);

sealed class InlineProgress<T> : IProgress<T>
{
    public List<T> Values { get; } = new();
    public void Report(T value) => Values.Add(value);
}

sealed class CallbackInstallationProgress(Action<AiInstallationProgress> callback) : IProgress<AiInstallationProgress>
{
    public void Report(AiInstallationProgress value) => callback(value);
}

sealed class StaticHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[] _content;

    public StaticHttpMessageHandler(byte[] content)
    {
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = new ByteArrayContent(_content);
        content.Headers.ContentLength = _content.LongLength;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
            RequestMessage = request
        });
    }
}

sealed class FailingAiConfigurationStore : IAiConfigurationStore
{
    public string ConfigurationPath { get; }
    public string LastLoadWarning => "";

    public FailingAiConfigurationStore(string configurationPath)
    {
        ConfigurationPath = configurationPath;
    }

    public Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiConfiguration());

    public Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.FromException(new IOException("simulated configuration activation failure"));
}

sealed class TestAiProposalApplicationService : IAiProposalApplicationService
{
    public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AiPreparedApplication> PrepareAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AiProposalApplicationResult> ApplyAsync(Guid confirmationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AiProposalApplicationResult> UndoAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AiStoredProposal> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public CalendarApplicationState GetCalendarState(Guid proposalId) =>
        new(false, false, false, false, "");
}

sealed class TestAiAssistantController : IAiAssistantController
{
    private readonly AiInstallationState _installationState;

    public TestAiAssistantController(AiInstallationState installationState = AiInstallationState.Ready)
    {
        _installationState = installationState;
    }

    public AiAssistantState State { get; private set; } = new();
    public int InitializeCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public event EventHandler? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCalls++;
        State = new AiAssistantState
        {
            IsInitialized = true,
            EffectiveProfile = AiProfile.Performance,
            InstallationState = _installationState,
            StatusMessage = _installationState == AiInstallationState.Ready
                ? "IA local pronta e offline."
                : "IA local ainda não instalada."
        };
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<AiStoredProposal?> SendAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default) => Task.FromResult<AiStoredProposal?>(null);

    public void CancelCurrentOperation() => CancelCalls++;

    public AiAssistantInput ApplyQuickAction(AiAssistantInput input, AiAssistantQuickAction action) =>
        input with { FreeText = input.FreeText.TrimEnd() + Environment.NewLine + "Monte um plano de estudos completo." };

    public AiProposalKind SuggestedKind(AiAssistantQuickAction action) => AiProposalKind.StudyPlan;
}

sealed class TestAiInstallationController : IAiInstallationController
{
    public AiInstallationUiState State { get; private set; } = new();
    public int PrepareCalls { get; private set; }
    public int InstallCalls { get; private set; }
    public event EventHandler? StateChanged;

    public Task<AiPreparedInstallation?> PrepareAsync(
        AiProfile profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrepareCalls++;
        var plan = new AiPreparedInstallation
        {
            ConfirmationId = Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            RequestedProfile = profile,
            EffectiveProfile = AiProfile.Performance,
            ComputePreference = AiComputePreference.Gpu,
            ModelName = "Qwen3 8B Q4_K_M",
            DownloadBytes = 5_062_991_684,
            RecommendedFreeBytes = 7_294_967_296,
            InstallationDirectory = Path.Combine(Path.GetTempPath(), "Rota", "AI")
        };
        State = new AiInstallationUiState
        {
            Activity = AiInstallationActivity.Idle,
            StatusMessage = "Instalação analisada.",
            Plan = plan,
            TotalDownloadBytes = plan.DownloadBytes
        };
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult<AiPreparedInstallation?>(plan);
    }

    public Task<AiInstallationResult?> InstallAsync(
        Guid confirmationId,
        CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        return Task.FromResult<AiInstallationResult?>(null);
    }

    public void CancelCurrentOperation() { }
}
