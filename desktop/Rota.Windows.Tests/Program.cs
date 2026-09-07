using Rota.Desktop;
using Rota.Desktop.LocalAI;
using Rota.Desktop.Tests.Fakes;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

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
    ("Available study days persist in weekday order", AvailableStudyDaysPersistAndValidate),
    ("Windows reminder preferences persist and validate", ReminderPreferencesPersistAndValidate),
    ("Windows reminder scheduler creates a bounded daily task", ReminderSchedulerCreatesDailyTask),
    ("Windows reminder scheduler removes only its own task", ReminderSchedulerRemovesOwnedTask),
    ("Fresh repository starts at the onboarding welcome", OnboardingStartsAtWelcome),
    ("Onboarding progress persists and stays idempotent", OnboardingProgressPersists),
    ("Onboarding steps reject invalid or skipped progress", OnboardingRejectsInvalidProgress),
    ("Existing study state adopts onboarding without migration", ExistingStateAdoptsOnboarding),
    ("Onboarding persistence failure leaves progress unchanged", OnboardingPersistenceIsTransactional),
    ("Onboarding routine saves preferences and progress atomically", OnboardingRoutinePersistsAtomically),
    ("Onboarding routine rejects invalid input without mutation", OnboardingRoutineRejectsInvalidInput),
    ("Onboarding routine persistence failure is transactional", OnboardingRoutinePersistenceIsTransactional),
    ("First-plan input carries the saved routine and difficulties", FirstPlanInputUsesSavedRoutine),
    ("First-plan input requires difficulties or an explicit unknown choice", FirstPlanInputValidatesDifficulties),
    ("First-plan onboarding completion persists as the last step", FirstPlanCompletionPersists),
    ("Overdue analysis identifies only unfinished past sessions", OverdueAnalysisFindsOnlyPastPending),
    ("Overdue analysis stays bounded without losing totals", OverdueAnalysisBoundsDetails),
    ("Overdue analysis never mutates repository state", OverdueAnalysisIsReadOnly),
    ("Overdue recovery request is bounded and requires a preview", OverdueRecoveryRequestIsSafe),
    ("Weekly summary counts completed minutes and sessions", WeeklySummaryCountsProgress),
    ("Weekly summary uses Monday boundaries without mutation", WeeklySummaryIsReadOnlyAndMondayBased),
    ("Subject progress groups completed minutes and sessions", SubjectProgressGroupsSessions),
    ("Subject progress stays bounded without mutation", SubjectProgressIsBoundedAndReadOnly),
    ("Progress history aggregates months and compares weeks", ProgressHistoryAggregatesAndCompares),
    ("Progress history stays bounded without mutation", ProgressHistoryIsBoundedAndReadOnly),
    ("Stored state rejects unknown properties", StoredStateRejectsUnknownProperties),
    ("Exported backup can be loaded independently", ExportedBackupReloads),
    ("Desktop windows load without XAML or binding failures", DesktopWindowsLoad),
    ("Month summaries count distinct remaining subjects", MonthSummaryCountsDistinctSubjects),
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
tests = tests.Concat(Rota.Desktop.Tests.AiConversationTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.EnemCatalogTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiProfileResourceBudgetTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiHardwareDiagnosticsTests.Cases).ToArray();
tests = tests.Concat(Rota.Desktop.Tests.AiPerformanceDiagnosticsTests.Cases).ToArray();
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROTA_TEST_RUNTIME_ARCHIVES")))
    tests = tests.Append(("Official CPU and Vulkan archives pass the real staging pipeline", (Action)OfficialRuntimeArchivesStage)).ToArray();
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROTA_TEST_LOCAL_AI_PERFORMANCE")))
    tests = tests.Append(("Installed local AI completes a disposable real performance diagnostic", (Action)InstalledLocalAiPerformanceDiagnostic)).ToArray();

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

static void AvailableStudyDaysPersistAndValidate()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        repo.SavePreferences(
            "Vestibular",
            "2026-12-15",
            6,
            60,
            true,
            true,
            true,
            new[] { DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Wednesday });

        var loaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq("Monday,Wednesday,Sunday", string.Join(',', loaded.Settings.AvailableStudyDays));

        var before = File.ReadAllText(path);
        Throws(() => repo.SavePreferences("Vestibular", "2026-12-15", 6, 60, true, true, true, Array.Empty<DayOfWeek>()), "dia");
        Throws(() => repo.SavePreferences("Vestibular", "2026-12-15", 6, 60, true, true, true, new[] { (DayOfWeek)99 }), "válidos");
        Eq(before, File.ReadAllText(path));
        Eq("Monday,Wednesday,Sunday", string.Join(',', repo.Settings.AvailableStudyDays));
    });
}

static void ReminderPreferencesPersistAndValidate()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        repo.SavePreferences(
            "Vestibular", "2026-12-15", 6, 60, true, true, true,
            new[] { DayOfWeek.Monday, DayOfWeek.Friday },
            reminderEnabled: true,
            reminderTime: "07:30");

        var loaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        True(loaded.Settings.ReminderEnabled, "enabled reminder was not persisted");
        Eq("07:30", loaded.Settings.ReminderTime);

        var before = File.ReadAllText(path);
        Throws(() => repo.SavePreferences(
            "Vestibular", "2026-12-15", 6, 60, true, true, true,
            reminderEnabled: true,
            reminderTime: "25:99"), "horário válido");
        Eq(before, File.ReadAllText(path));
    });
}

static void ReminderSchedulerCreatesDailyTask()
{
    var dir = Path.Combine(Path.GetTempPath(), "RotaReminderTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var executable = Path.Combine(dir, "Rota.exe");
        File.WriteAllBytes(executable, Array.Empty<byte>());
        var runner = new RecordingReminderTaskRunner(new ReminderTaskResult(0, "SUCCESS"));
        var scheduler = new WindowsStudyReminderScheduler(runner);

        scheduler.Apply(StudyReminderConfiguration.Parse(true, "19:45"), executable);

        Eq(1, runner.Calls.Count);
        var arguments = runner.Calls.Single();
        Eq("/Create", arguments[0]);
        Contains(string.Join('|', arguments), "/SC|DAILY");
        Contains(string.Join('|', arguments), "/ST|19:45");
        Contains(string.Join('|', arguments), $"\"{Path.GetFullPath(executable)}\" --reminder");
        True(!arguments.Any(argument => argument.Contains('\n') || argument.Contains('\r')),
            "reminder command contains an unsafe control character");
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}

static void ReminderSchedulerRemovesOwnedTask()
{
    var runner = new RecordingReminderTaskRunner(
        new ReminderTaskResult(0, "task exists"),
        new ReminderTaskResult(0, "deleted"));
    var scheduler = new WindowsStudyReminderScheduler(runner);

    scheduler.Apply(StudyReminderConfiguration.Parse(false, "19:00"), @"C:\Program Files\Rota\Rota.exe");

    Eq(2, runner.Calls.Count);
    Eq("/Query", runner.Calls[0][0]);
    Eq("/Delete", runner.Calls[1][0]);
    Eq(runner.Calls[0][2], runner.Calls[1][2]);
    True(runner.Calls[1].Contains("/F"), "reminder deletion must be explicit and non-interactive");
}

static void WeeklySummaryCountsProgress()
{
    var sessions = new[]
    {
        WeeklySession("outside-before", "2026-08-30", 120, completed: true),
        WeeklySession("monday", "2026-08-31", 60, completed: true),
        WeeklySession("tuesday", "2026-09-01", 45),
        WeeklySession("sunday", "2026-09-06", 30, completed: true),
        WeeklySession("outside-after", "2026-09-07", 90, completed: true)
    };
    var summary = WeeklyProgressAnalyzer.Analyze(new RepositoryApplicationSnapshot(
        7, "2026-09-02", new AppSettings(), sessions));

    Eq(new DateOnly(2026, 8, 31), summary.WeekStart);
    Eq(new DateOnly(2026, 9, 6), summary.WeekEnd);
    Eq(3, summary.PlannedSessions);
    Eq(2, summary.CompletedSessions);
    Eq(135, summary.PlannedMinutes);
    Eq(90, summary.CompletedMinutes);
    Eq(1, summary.RemainingSessions);
    Eq(45, summary.RemainingMinutes);
    Eq(67, summary.CompletionPercent);
    Eq(7, summary.Days.Count);
}

static void WeeklySummaryIsReadOnlyAndMondayBased()
{
    var sessions = new[] { WeeklySession("sunday", "2026-09-06", 30, completed: true) };
    var snapshot = new RepositoryApplicationSnapshot(4, "2026-09-06", new AppSettings(), sessions);
    var before = JsonSerializer.Serialize(snapshot);

    var summary = WeeklyProgressAnalyzer.Analyze(snapshot);

    Eq(new DateOnly(2026, 8, 31), summary.WeekStart);
    Eq(new DateOnly(2026, 9, 6), summary.WeekEnd);
    Eq(1, summary.Days[^1].CompletedSessions);
    Eq(before, JsonSerializer.Serialize(snapshot));
}

static void SubjectProgressGroupsSessions()
{
    var sessions = new[]
    {
        WeeklySession("math-complete", "2026-08-31", 60, completed: true, subject: "Matemática"),
        WeeklySession("math-pending", "2026-09-02", 45, subject: "matemática"),
        WeeklySession("physics-complete", "2026-09-01", 30, completed: true, subject: "Física")
    };
    var progress = SubjectProgressAnalyzer.Analyze(new RepositoryApplicationSnapshot(
        8, "2026-09-02", new AppSettings(), sessions));

    Eq(2, progress.TotalSubjects);
    Eq(3, progress.PlannedSessions);
    Eq(2, progress.CompletedSessions);
    Eq(135, progress.PlannedMinutes);
    Eq(90, progress.CompletedMinutes);
    Eq(67, progress.CompletionPercent);
    Eq("Matemática", progress.Subjects[0].Subject);
    Eq(2, progress.Subjects[0].PlannedSessions);
    Eq(1, progress.Subjects[0].CompletedSessions);
    Eq(60, progress.Subjects[0].CompletedMinutes);
    Eq(45, progress.Subjects[0].RemainingMinutes);
    Eq(50, progress.Subjects[0].CompletionPercent);
}

static void SubjectProgressIsBoundedAndReadOnly()
{
    var sessions = Enumerable.Range(0, 120)
        .Select(index => WeeklySession(
            $"subject-{index}",
            "2026-09-01",
            30,
            completed: index % 2 == 0,
            subject: $"Matéria {index:000}"))
        .ToArray();
    var snapshot = new RepositoryApplicationSnapshot(9, "2026-09-02", new AppSettings(), sessions);
    var before = JsonSerializer.Serialize(snapshot);

    var progress = SubjectProgressAnalyzer.Analyze(snapshot, itemLimit: 10);

    Eq(120, progress.TotalSubjects);
    Eq(10, progress.Subjects.Count);
    Eq(110, progress.HiddenSubjectCount);
    Eq(120, progress.PlannedSessions);
    Eq(60, progress.CompletedSessions);
    Eq(before, JsonSerializer.Serialize(snapshot));
    ThrowsType<ArgumentOutOfRangeException>(() => SubjectProgressAnalyzer.Analyze(snapshot, 0));
}

static SessionItem WeeklySession(
    string id,
    string date,
    int minutes,
    bool completed = false,
    string subject = "Matemática") => new()
    {
        Id = id,
        PlanId = "weekly-plan",
        PlanRevision = 1,
        Date = date,
        Subject = subject,
        Topic = id,
        Minutes = minutes,
        Target = "Estudar",
        Kind = "study",
        Status = completed ? "completed" : "planned",
        Origin = "plan",
        CompletedAtUnixMs = completed ? 1_788_200_000_000 : 0
    };

static void ProgressHistoryAggregatesAndCompares()
{
    var sessions = new[]
    {
        WeeklySession("august", "2026-08-20", 30, completed: true),
        WeeklySession("previous-week", "2026-08-27", 40, completed: true),
        WeeklySession("current-done", "2026-09-01", 60, completed: true),
        WeeklySession("current-pending", "2026-09-02", 30)
    };
    var history = ProgressHistoryAnalyzer.Analyze(new RepositoryApplicationSnapshot(
        10, "2026-09-02", new AppSettings(), sessions), monthCount: 2, weekCount: 2);

    Eq(3, history.TotalCompletedSessions);
    Eq(130, history.TotalCompletedMinutes);
    Eq(2, history.ActiveMonths);
    Eq(new DateOnly(2026, 9, 1), history.Months[0].Month);
    Eq(2, history.Months[0].PlannedSessions);
    Eq(1, history.Months[0].CompletedSessions);
    Eq(50, history.Months[0].CompletionPercent);
    Eq(2, history.Weeks.Count);
    Eq(20, history.Weeks[0].CompletedMinutesChange);
}

static void ProgressHistoryIsBoundedAndReadOnly()
{
    var snapshot = new RepositoryApplicationSnapshot(
        11,
        "2026-09-02",
        new AppSettings(),
        new[] { WeeklySession("one", "2026-09-01", 30, completed: true) });
    var before = JsonSerializer.Serialize(snapshot);

    var history = ProgressHistoryAnalyzer.Analyze(
        snapshot,
        ProgressHistoryAnalyzer.MaximumMonthCount,
        ProgressHistoryAnalyzer.MaximumWeekCount);

    Eq(ProgressHistoryAnalyzer.MaximumMonthCount, history.Months.Count);
    Eq(ProgressHistoryAnalyzer.MaximumWeekCount, history.Weeks.Count);
    Eq(before, JsonSerializer.Serialize(snapshot));
    ThrowsType<ArgumentOutOfRangeException>(() => ProgressHistoryAnalyzer.Analyze(snapshot, 0, 2));
    ThrowsType<ArgumentOutOfRangeException>(() => ProgressHistoryAnalyzer.Analyze(snapshot, 1, 1));
}

static void OnboardingStartsAtWelcome()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, _, _) =>
    {
        Eq(0, repo.CompletedOnboardingStep);
        True(repo.CompletedOnboardingStep < OnboardingSteps.Welcome,
            "a fresh profile must still require the welcome step");
    });
}

static void OnboardingProgressPersists()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson(
            "onboarding-existing-plan",
            1,
            "2026-09-30",
            SessionJson("protected-session", "2026-09-02", 60)))).Success);
        True(repo.CompleteOnboardingStep(OnboardingSteps.Welcome),
            "the first welcome completion was ignored");
        Eq(OnboardingSteps.Welcome, repo.CompletedOnboardingStep);

        var afterFirstCompletion = File.ReadAllText(path);
        True(!repo.CompleteOnboardingStep(OnboardingSteps.Welcome),
            "repeating a completed onboarding step must be idempotent");
        Eq(afterFirstCompletion, File.ReadAllText(path));

        var loaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq(OnboardingSteps.Welcome, loaded.CompletedOnboardingStep);
        Eq("onboarding-existing-plan", loaded.Settings.ActivePlanId);
        Eq(1, loaded.SessionsForDate(new DateOnly(2026, 9, 2)).Count);
    });
}

static void OnboardingRejectsInvalidProgress()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        var before = File.ReadAllText(path);
        ThrowsType<ArgumentOutOfRangeException>(() => repo.CompleteOnboardingStep(0));
        ThrowsType<ArgumentOutOfRangeException>(() => repo.CompleteOnboardingStep(OnboardingSteps.Last + 1));
        ThrowsType<InvalidOperationException>(() => repo.CompleteOnboardingStep(OnboardingSteps.Routine));
        Eq(0, repo.CompletedOnboardingStep);
        Eq(before, File.ReadAllText(path));
    });
}

static void ExistingStateAdoptsOnboarding()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        var json = File.ReadAllText(path);
        var oldState = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("test fixture did not contain a JSON object");
        True(oldState.Remove("CompletedOnboardingStep"),
            "test fixture did not remove the additive onboarding field");
        File.WriteAllText(path, oldState.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var loaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq(0, loaded.CompletedOnboardingStep);
        True(loaded.CompleteOnboardingStep(OnboardingSteps.Welcome));
        Eq(OnboardingSteps.Welcome, new StudyRepository(path).CompletedOnboardingStep);
    });
}

static void OnboardingPersistenceIsTransactional()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        var before = File.ReadAllText(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ThrowsType<IOException>(() => repo.CompleteOnboardingStep(OnboardingSteps.Welcome));
        }
        Eq(0, repo.CompletedOnboardingStep);
        Eq(before, File.ReadAllText(path));
        Eq(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    });
}

static void OnboardingRoutinePersistsAtomically()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson(
            "routine-existing-plan",
            1,
            "2026-12-20",
            SessionJson("protected-session", "2026-09-02", 60)))).Success);
        True(repo.CompleteOnboardingStep(OnboardingSteps.Welcome));
        True(repo.SaveOnboardingRoutine(
            "  ENEM 2027  ",
            "2027-11-07",
            2.5,
            new[] { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday }));

        Eq(OnboardingSteps.Routine, repo.CompletedOnboardingStep);
        Eq("ENEM 2027", repo.Settings.ObjectiveName);
        Eq("2027-11-07", repo.Settings.ObjectiveDate);
        Eq(2.5, repo.Settings.DailyHours);
        Eq("Monday,Wednesday,Friday", string.Join(',', repo.Settings.AvailableStudyDays));
        Eq("routine-existing-plan", repo.Settings.ActivePlanId);
        Eq(1, repo.SessionsForDate(new DateOnly(2026, 9, 2)).Count);

        var afterCompletion = File.ReadAllText(path);
        True(!repo.SaveOnboardingRoutine("Outro", "2028-01-01", 8, new[] { DayOfWeek.Saturday }));
        Eq(afterCompletion, File.ReadAllText(path));

        var loaded = new StudyRepository(path, () => new DateTime(2026, 9, 1, 9, 0, 0));
        Eq(OnboardingSteps.Routine, loaded.CompletedOnboardingStep);
        Eq("ENEM 2027", loaded.Settings.ObjectiveName);
        Eq("Monday,Wednesday,Friday", string.Join(',', loaded.Settings.AvailableStudyDays));
        Eq("routine-existing-plan", loaded.Settings.ActivePlanId);
    });
}

static void OnboardingRoutineRejectsInvalidInput()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        ThrowsType<InvalidOperationException>(() => repo.SaveOnboardingRoutine(
            "ENEM", "2027-11-07", 2, new[] { DayOfWeek.Monday }));
        True(repo.CompleteOnboardingStep(OnboardingSteps.Welcome));
        var before = File.ReadAllText(path);

        Throws(() => repo.SaveOnboardingRoutine("", "2027-11-07", 2, new[] { DayOfWeek.Monday }), "obrigatório");
        Throws(() => repo.SaveOnboardingRoutine("ENEM", "2026-09-01", 2, new[] { DayOfWeek.Monday }), "posterior");
        Throws(() => repo.SaveOnboardingRoutine("ENEM", "2027-11-07", 0, new[] { DayOfWeek.Monday }), "1 e 12");
        Throws(() => repo.SaveOnboardingRoutine("ENEM", "2027-11-07", 2, Array.Empty<DayOfWeek>()), "dia");
        Throws(() => repo.SaveOnboardingRoutine("ENEM", "2027-11-07", 2, new[] { (DayOfWeek)99 }), "válidos");

        Eq(OnboardingSteps.Welcome, repo.CompletedOnboardingStep);
        Eq("Meu objetivo", repo.Settings.ObjectiveName);
        Eq(before, File.ReadAllText(path));
    });
}

static void OnboardingRoutinePersistenceIsTransactional()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.CompleteOnboardingStep(OnboardingSteps.Welcome));
        var before = File.ReadAllText(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ThrowsType<IOException>(() => repo.SaveOnboardingRoutine(
                "ENEM 2027", "2027-11-07", 2.5, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }));
        }

        Eq(OnboardingSteps.Welcome, repo.CompletedOnboardingStep);
        Eq("Meu objetivo", repo.Settings.ObjectiveName);
        Eq(before, File.ReadAllText(path));
        Eq(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    });
}

static void FirstPlanInputUsesSavedRoutine()
{
    var settings = new AppSettings
    {
        ObjectiveName = "ENEM 2027",
        ObjectiveDate = "2027-11-07",
        DailyHours = 2.5,
        AvailableStudyDays = new List<DayOfWeek>
        {
            DayOfWeek.Monday,
            DayOfWeek.Wednesday,
            DayOfWeek.Friday
        }
    };
    var input = OnboardingFirstPlanInputBuilder.Build(
        settings,
        " matemática, Química; matemática\nInterpretação de texto ",
        difficultiesUnknown: false,
        "Começar pela base.");

    Eq("ENEM 2027", input.ObjectiveOrExam);
    Eq("2027-11-07", input.ExamDate);
    Eq(2.5, input.AvailableHoursPerDay);
    Eq("Monday,Wednesday,Friday", string.Join(',', input.AvailableDays));
    Eq("matemática,Química,Interpretação de texto", string.Join(',', input.WeakSubjects));
    Eq("Começar pela base.", input.Notes);
    Contains(input.Goal, "primeiro plano");
}

static void FirstPlanInputValidatesDifficulties()
{
    var settings = new AppSettings
    {
        ObjectiveName = "Concurso",
        ObjectiveDate = "2027-02-20",
        DailyHours = 3,
        AvailableStudyDays = new List<DayOfWeek> { DayOfWeek.Saturday }
    };
    Throws(
        () => OnboardingFirstPlanInputBuilder.Build(settings, "  ", difficultiesUnknown: false, ""),
        "dificuldade");
    Throws(
        () => OnboardingFirstPlanInputBuilder.Build(
            settings,
            new string('x', OnboardingFirstPlanInputBuilder.MaximumDifficultiesLength + 1),
            difficultiesUnknown: false,
            ""),
        "no máximo");

    var unknown = OnboardingFirstPlanInputBuilder.Build(
        settings,
        "",
        difficultiesUnknown: true,
        "Prefiro sessões curtas.");
    Eq(0, unknown.WeakSubjects.Count);
    Contains(unknown.Notes, "Ainda não identifiquei");
    Contains(unknown.Notes, "sessões curtas");
}

static void FirstPlanCompletionPersists()
{
    WithRepository(new DateTime(2026, 9, 1, 9, 0, 0), (repo, path, _) =>
    {
        True(repo.CompleteOnboardingStep(OnboardingSteps.Welcome));
        True(repo.SaveOnboardingRoutine(
            "ENEM 2027",
            "2027-11-07",
            2.5,
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }));
        True(repo.CompleteOnboardingStep(OnboardingSteps.FirstPlan));
        Eq(OnboardingSteps.Last, repo.CompletedOnboardingStep);

        var completed = File.ReadAllText(path);
        True(!repo.CompleteOnboardingStep(OnboardingSteps.FirstPlan));
        Eq(completed, File.ReadAllText(path));
        Eq(OnboardingSteps.Last, new StudyRepository(path).CompletedOnboardingStep);
    });
}

static void OverdueAnalysisFindsOnlyPastPending()
{
    var sessions = new List<SessionItem>
    {
        OverdueSession("old-study", "2026-08-29", 60),
        OverdueSession("old-review", "2026-08-31", 30, "review", "runtime"),
        OverdueSession("today", "2026-09-01", 45),
        OverdueSession("future", "2026-09-02", 50),
        OverdueSession("completed-old", "2026-08-28", 40, completed: true)
    };
    var snapshot = new RepositoryApplicationSnapshot(
        7,
        "2026-09-01",
        new AppSettings(),
        sessions);

    var overdue = OverdueStudyAnalyzer.Analyze(snapshot);

    Eq(2, overdue.TotalCount);
    Eq(90, overdue.TotalMinutes);
    Eq(new DateOnly(2026, 8, 29), overdue.OldestDate);
    Eq(3, overdue.MostDelayedDays);
    Eq(1, overdue.RuntimeProtectedCount);
    Eq("old-study", overdue.Items[0].SessionId);
    Eq("old-review", overdue.Items[1].SessionId);
    True(overdue.Items[1].IsRuntimeProtected);
}

static void OverdueAnalysisBoundsDetails()
{
    var sessions = Enumerable.Range(1, 8)
        .Select(index => OverdueSession($"old-{index}", $"2026-08-{index + 10:00}", 25))
        .ToList();
    var snapshot = new RepositoryApplicationSnapshot(
        0,
        "2026-09-01",
        new AppSettings(),
        sessions);

    var overdue = OverdueStudyAnalyzer.Analyze(snapshot, itemLimit: 3);

    Eq(8, overdue.TotalCount);
    Eq(200, overdue.TotalMinutes);
    Eq(3, overdue.Items.Count);
    ThrowsType<ArgumentOutOfRangeException>(() => OverdueStudyAnalyzer.Analyze(snapshot, 0));
    ThrowsType<ArgumentOutOfRangeException>(() =>
        OverdueStudyAnalyzer.Analyze(snapshot, OverdueStudyAnalyzer.MaximumItemLimit + 1));
}

static void OverdueAnalysisIsReadOnly()
{
    WithRepository(new DateTime(2026, 8, 31, 9, 0, 0), (repo, path, clock) =>
    {
        True(repo.ApplyPlan(StudyPlanImporter.Parse(PlanJson(
            "overdue-plan",
            1,
            "2026-09-30",
            SessionJson("overdue", "2026-09-01", 60),
            SessionJson("future", "2026-09-04", 60)))).Success);
        clock.Value = new DateTime(2026, 9, 3, 9, 0, 0);
        var beforeMemory = JsonSerializer.Serialize(repo.CaptureApplicationSnapshot());
        var beforeDisk = File.ReadAllText(path);

        var result = OverdueStudyAnalyzer.Analyze(repo.CaptureApplicationSnapshot());

        Eq(1, result.TotalCount);
        Eq(beforeMemory, JsonSerializer.Serialize(repo.CaptureApplicationSnapshot()));
        Eq(beforeDisk, File.ReadAllText(path));
    });
}

static void OverdueRecoveryRequestIsSafe()
{
    var sessions = Enumerable.Range(1, 30)
        .Select(index => OverdueSession(
            $"recovery-{index}",
            $"2026-08-{(index % 28) + 1:00}",
            30,
            index == 1 ? "review" : "study",
            index == 1 ? "runtime" : "plan"))
        .ToList();
    var source = new RepositoryApplicationSnapshot(
        5,
        "2026-09-01",
        new AppSettings(),
        sessions);
    var before = JsonSerializer.Serialize(source);

    var snapshot = OverdueStudyAnalyzer.Analyze(source);
    var request = OverdueRecoveryPresentation.BuildAiRequest(snapshot);

    True(request.Length <= OverdueRecoveryPresentation.MaximumRequestLength);
    Contains(request, "30 blocos atrasados");
    Contains(request, "somente datas futuras");
    Contains(request, "prévia e confirmação");
    Contains(request, "revisão automática protegida");
    Contains(request, "mais 10 blocos");
    Eq(before, JsonSerializer.Serialize(source));
    ThrowsType<InvalidOperationException>(() => OverdueRecoveryPresentation.BuildAiRequest(
        new OverdueStudySnapshot(
            new DateOnly(2026, 9, 1),
            0,
            0,
            null,
            0,
            0,
            Array.Empty<OverdueStudyItem>())));
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
            ThemeManager.Apply(ThemeManager.Dark);
            AssertThemeContrast();
            var repo = new StudyRepository(Path.Combine(dir, "state.json"), () => new DateTime(2026, 9, 1, 9, 0, 0));
            var assistant = new TestAiAssistantController();
            var diagnostics = new TestAiAssistantController();
            var hardwareDiagnostics = new TestAiHardwareDiagnosticsService();
            var performanceDiagnostics = new TestAiPerformanceDiagnosticsService();
            var installation = new TestAiInstallationController();
            var application = new TestAiProposalApplicationService();
            var firstPlanProposalId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
            var firstPlanStored = new AiStoredProposal
            {
                Proposal = new AiProposal
                {
                    Id = firstPlanProposalId,
                    CreatedAtUtc = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                    Summary = "Primeiro plano equilibrado para o ENEM.",
                    Kind = AiProposalKind.StudyPlan,
                    Status = AiProposalStatus.Validated,
                    StudyPlan = new AiStudyPlanDraft { StudyPlanJson = "{}" }
                },
                Preview = new AiProposalPreview
                {
                    ProposalId = firstPlanProposalId,
                    Kind = AiProposalKind.StudyPlan,
                    State = AiProposalPreviewState.Ready,
                    Message = "Plano pronto para revisão.",
                    BeforeSessionCount = 0,
                    AfterSessionCount = 12,
                    BeforeMinutes = 0,
                    AfterMinutes = 900,
                    AddedSessionCount = 12
                },
                UpdatedAtUtc = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)
            };
            var firstPlanAssistant = new TestAiAssistantController(AiInstallationState.Ready, firstPlanStored);
            var firstPlanPrepared = new AiPreparedApplication
            {
                ConfirmationId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444"),
                ProposalId = firstPlanProposalId,
                Summary = firstPlanStored.Proposal.Summary,
                Kind = AiProposalKind.StudyPlan,
                Preview = firstPlanStored.Preview,
                Message = "Revise o primeiro plano antes de aplicar."
            };
            var firstPlanApplication = new TestAiProposalApplicationService(
                firstPlanPrepared,
                new AiProposalApplicationResult { Success = true, Message = "Plano aplicado." });
            var assistantWindow = new AiAssistantWindow(assistant, installation, application, repo);
            var recoveryWindowSnapshot = OverdueStudyAnalyzer.Analyze(new RepositoryApplicationSnapshot(
                0,
                "2026-09-03",
                new AppSettings(),
                new[]
                {
                    OverdueSession("recovery-window-study", "2026-09-01", 60),
                    OverdueSession("recovery-window-review", "2026-09-02", 30, "review", "runtime")
                }));
            var weeklyWindowSnapshot = new RepositoryApplicationSnapshot(
                5,
                "2026-09-02",
                new AppSettings(),
                new[]
                {
                    WeeklySession("weekly-complete", "2026-08-31", 60, completed: true),
                    WeeklySession("weekly-pending", "2026-09-02", 45)
                });
            Eq(0, assistant.InitializeCalls);
            Eq(0, installation.PrepareCalls);
            var windows = new Window[]
            {
                new MainWindow(repo, assistant, installation, application),
                assistantWindow,
                new AiDiagnosticsWindow(diagnostics, hardwareDiagnostics, performanceDiagnostics),
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
                new SettingsWindow(repo, new RecordingStudyReminderScheduler()),
                new WelcomeWindow(repo),
                new OnboardingRoutineWindow(repo),
                new OnboardingFirstPlanWindow(repo, firstPlanAssistant, installation, firstPlanApplication),
                new OverdueRecoveryWindow(recoveryWindowSnapshot),
                new ReminderWindow(repo),
                new WeeklySummaryWindow(weeklyWindowSnapshot),
                new SubjectProgressWindow(weeklyWindowSnapshot),
                new ProgressHistoryWindow(weeklyWindowSnapshot)
            };
            foreach (var window in windows)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20_000;
                window.Top = -20_000;
                window.ShowInTaskbar = false;
                window.Show();
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                window.UpdateLayout();
                var expectedBackground = ThemeManager.ResourceBrush("BackgroundBrush") as System.Windows.Media.SolidColorBrush
                    ?? throw new InvalidOperationException("active window background resource was not created");
                var actualBackground = window.Background as System.Windows.Media.SolidColorBrush
                    ?? throw new InvalidOperationException($"{window.GetType().Name} does not expose a solid theme background");
                Eq(expectedBackground.Color, actualBackground.Color);
                var undersizedButton = VisualDescendants<System.Windows.Controls.Button>(window)
                    .FirstOrDefault(button => button.IsVisible && button.ActualHeight < 36);
                True(undersizedButton is null,
                    $"{window.GetType().Name} contains a visible click target shorter than 36 px: " +
                    $"{undersizedButton?.Name ?? "unnamed"} ({undersizedButton?.ActualWidth:0.#} × {undersizedButton?.ActualHeight:0.#})");
                SaveWindowSnapshot(window, "dark-" + window.GetType().Name);
                if (window is MainWindow mainWindow)
                {
                    True(mainWindow.MinWidth <= 1_000, "main window cannot fit a 1024 px work area with margins");
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
                    var weeklySummary = mainWindow.FindName("WeeklySummaryButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("weekly summary entry point was not created");
                    True(weeklySummary.MinHeight >= 36, "weekly summary click target is too small");

                    var iconStyle = app.FindResource("IconGlyphText") as Style
                        ?? throw new InvalidOperationException("shared icon style was not loaded");
                    True(iconStyle.Setters.OfType<Setter>().Any(setter => setter.Property == System.Windows.Controls.TextBlock.FontFamilyProperty),
                        "shared icon style does not define a stable icon font");
                    var textStyle = app.FindResource(typeof(System.Windows.Controls.TextBlock)) as Style
                        ?? throw new InvalidOperationException("shared text contrast style was not loaded");
                    True(textStyle.Setters.OfType<Setter>().Any(setter =>
                            setter.Property == System.Windows.Controls.TextBlock.ForegroundProperty),
                        "shared text style does not follow the active theme foreground");
                    var checkBoxStyle = app.FindResource(typeof(System.Windows.Controls.CheckBox)) as Style
                        ?? throw new InvalidOperationException("shared checkbox style was not loaded");
                    True(checkBoxStyle.Setters.OfType<Setter>().Any(setter =>
                            setter.Property == System.Windows.Controls.Control.ForegroundProperty),
                        "checkbox labels do not follow the active theme foreground");
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
                    var changePlan = localAssistant.FindName("ChangePlanChoice") as System.Windows.Controls.RadioButton
                        ?? throw new InvalidOperationException("AI change-plan choice was not created");
                    var reorganize = localAssistant.FindName("QuickReorganizeWeekButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI reorganize action was not created");
                    var externalPrompt = localAssistant.FindName("OpenExternalPromptButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("existing external AI prompt entry point was not preserved");
                    var diagnosticsButton = localAssistant.FindName("OpenAiDiagnosticsButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI diagnostics entry point was not created");
                    True(!send.IsEnabled, "empty AI request must not be sent");
                    Eq(8_000, request.MaxLength);
                    True(!changePlan.IsEnabled, "change-plan choice must stay disabled without a future plan");
                    True(!reorganize.IsEnabled, "change-plan quick actions must stay disabled without a future plan");
                    request.Text = "Tenho duas horas por dia.";
                    True(send.IsEnabled, "ready local AI should enable a non-empty request");
                    var sendHint = localAssistant.FindName("SendHintText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI request length hint was not created");
                    Contains(sendHint.Text, "/8.000 caracteres");
                    quickAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Contains(request.Text, "Monte um plano");
                    True(externalPrompt.MinHeight >= 36, "external AI prompt action is too small");
                    True(diagnosticsButton.IsEnabled && diagnosticsButton.MinHeight >= 38,
                        "AI diagnostics action is unavailable or too small");
                    True(!VisualDescendants<System.Windows.Controls.Button>(localAssistant)
                        .Any(button => (button.Content?.ToString() ?? "").Contains("Aplicar", StringComparison.OrdinalIgnoreCase)),
                        "AI preview screen must not expose an apply action");
                }
                if (window is AiDiagnosticsWindow diagnosticsWindow)
                {
                    Eq(1, diagnostics.InitializeCalls);
                    var badge = diagnosticsWindow.FindName("OverallBadgeText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI diagnostics summary badge was not created");
                    var profile = diagnosticsWindow.FindName("ProfileText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI diagnostics profile was not created");
                    var installationStatus = diagnosticsWindow.FindName("InstallationStatusText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI diagnostics installation status was not created");
                    var hardwareStatus = diagnosticsWindow.FindName("HardwareStatusText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI diagnostics hardware stage was not created");
                    var performanceStatus = diagnosticsWindow.FindName("PerformanceStatusText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI diagnostics performance stage was not created");
                    var refresh = diagnosticsWindow.FindName("RefreshButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI diagnostics refresh action was not created");
                    var runPerformance = diagnosticsWindow.FindName("RunPerformanceTestButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("AI performance test action was not created");
                    var hardwareDetails = diagnosticsWindow.FindName("HardwareDetailsPanel") as System.Windows.Controls.Border
                        ?? throw new InvalidOperationException("AI hardware details were not created");
                    var performanceDetails = diagnosticsWindow.FindName("PerformanceDetailsPanel") as System.Windows.Controls.Border
                        ?? throw new InvalidOperationException("AI performance details were not created");
                    var processor = diagnosticsWindow.FindName("CpuValueText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI processor details were not created");
                    var memory = diagnosticsWindow.FindName("MemoryValueText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI memory details were not created");
                    var gpu = diagnosticsWindow.FindName("GpuValueText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI GPU details were not created");
                    var storage = diagnosticsWindow.FindName("StorageValueText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI storage details were not created");
                    var recommended = diagnosticsWindow.FindName("RecommendedProfileText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI recommended profile was not created");
                    Eq("PRONTA", badge.Text);
                    Eq("Desempenho", profile.Text);
                    Contains(installationStatus.Text, "disponíveis");
                    Contains(hardwareStatus.Text, "perfil Desempenho");
                    Eq("Pronto para medir.", performanceStatus.Text);
                    Eq(Visibility.Visible, hardwareDetails.Visibility);
                    Eq(Visibility.Collapsed, performanceDetails.Visibility);
                    Contains(processor.Text, "Ryzen 7 5700X");
                    Contains(memory.Text, "16 GB");
                    Contains(gpu.Text, "RTX 3070");
                    Contains(storage.Text, "400 GB livres");
                    Contains(recommended.Text, "DESEMPENHO");
                    Eq(1, hardwareDiagnostics.AnalyzeCalls);
                    diagnosticsWindow.Width = 760;
                    diagnosticsWindow.Height = 720;
                    diagnosticsWindow.UpdateLayout();
                    SaveWindowSnapshot(diagnosticsWindow, "dark-AiDiagnosticsWindow-expanded");
                    True(refresh.IsEnabled && refresh.MinHeight >= 40,
                        "AI diagnostics refresh action is unavailable or too small");
                    True(runPerformance.IsEnabled && runPerformance.MinHeight >= 38,
                        "AI performance test action is unavailable or too small");
                    runPerformance.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    diagnosticsWindow.UpdateLayout();
                    Eq(1, performanceDiagnostics.MeasureCalls);
                    Eq(Visibility.Visible, performanceDetails.Visibility);
                    Contains(performanceStatus.Text, "20");
                    Contains(performanceStatus.Text, "tokens/s");
                    var tokenRate = diagnosticsWindow.FindName("TokenRateValueText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI token-rate result was not created");
                    var performanceRating = diagnosticsWindow.FindName("PerformanceRatingText") as System.Windows.Controls.TextBlock
                        ?? throw new InvalidOperationException("AI performance rating was not created");
                    Contains(tokenRate.Text, "20");
                    Contains(tokenRate.Text, "tokens/s");
                    Eq("EXCELENTE", performanceRating.Text);
                    performanceDetails.BringIntoView();
                    diagnosticsWindow.UpdateLayout();
                    SaveWindowSnapshot(diagnosticsWindow, "dark-AiDiagnosticsWindow-performance");
                    refresh.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Eq(2, diagnostics.InitializeCalls);
                    Eq(2, hardwareDiagnostics.AnalyzeCalls);
                    Eq(Visibility.Collapsed, performanceDetails.Visibility);
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
                if (window is SettingsWindow settingsWindow)
                {
                    var reminderEnabled = settingsWindow.FindName("ReminderEnabled") as System.Windows.Controls.CheckBox
                        ?? throw new InvalidOperationException("Windows reminder option was not created");
                    var reminderTime = settingsWindow.FindName("ReminderTimeBox") as System.Windows.Controls.TextBox
                        ?? throw new InvalidOperationException("Windows reminder time was not created");
                    Eq("19:00", reminderTime.Text);
                    True(reminderTime.MaxLength == 5, "reminder time input is not bounded");
                    reminderEnabled.IsChecked = true;
                    reminderTime.Text = "19:45";
                    reminderTime.BringIntoView();
                    settingsWindow.UpdateLayout();
                    SaveWindowSnapshot(settingsWindow, "dark-SettingsWindow-reminder");
                }
                if (window is ReminderWindow reminderWindow)
                {
                    var openCalendar = reminderWindow.FindName("OpenRotaButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("reminder open-calendar action was not created");
                    True(openCalendar.IsDefault && openCalendar.MinHeight >= 40,
                        "reminder open-calendar action is unavailable or too small");
                    Contains(reminderWindow.TodaySummary, "Nenhum bloco");
                }
                if (window is WeeklySummaryWindow weeklySummaryWindow)
                {
                    var close = weeklySummaryWindow.FindName("CloseButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("weekly summary close action was not created");
                    True(close.IsDefault && close.MinHeight >= 40,
                        "weekly summary close action is unavailable or too small");
                    Eq("60 min", weeklySummaryWindow.CompletedMinutesLabel);
                    Eq("1", weeklySummaryWindow.CompletedSessionsLabel);
                    Eq("50%", weeklySummaryWindow.CompletionLabel);
                    Eq(7, weeklySummaryWindow.Days.Count);
                    var subjectProgress = weeklySummaryWindow.FindName("SubjectProgressButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("subject progress entry point was not created");
                    True(subjectProgress.MinHeight >= 40, "subject progress entry point is too small");
                    var progressHistory = weeklySummaryWindow.FindName("ProgressHistoryButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("progress history entry point was not created");
                    True(progressHistory.MinHeight >= 40, "progress history entry point is too small");
                    _ = weeklySummaryWindow.Dispatcher.BeginInvoke(() =>
                    {
                        var subjectWindow = app.Windows.OfType<SubjectProgressWindow>()
                            .Single(candidate => candidate.IsVisible);
                        subjectWindow.Close();
                    });
                    subjectProgress.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    _ = weeklySummaryWindow.Dispatcher.BeginInvoke(() =>
                    {
                        var historyWindow = app.Windows.OfType<ProgressHistoryWindow>()
                            .Single(candidate => candidate.IsVisible);
                        historyWindow.Close();
                    });
                    progressHistory.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                if (window is SubjectProgressWindow subjectProgressWindow)
                {
                    var close = subjectProgressWindow.FindName("CloseButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("subject progress close action was not created");
                    True(close.IsDefault && close.MinHeight >= 40,
                        "subject progress close action is unavailable or too small");
                    Eq("1", subjectProgressWindow.TotalSubjectsLabel);
                    Eq("60 min", subjectProgressWindow.CompletedMinutesLabel);
                    Eq("1", subjectProgressWindow.CompletedSessionsLabel);
                    Eq("50%", subjectProgressWindow.CompletionLabel);
                    Eq(1, subjectProgressWindow.Subjects.Count);
                }
                if (window is ProgressHistoryWindow progressHistoryWindow)
                {
                    var close = progressHistoryWindow.FindName("CloseButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("progress history close action was not created");
                    var months = progressHistoryWindow.FindName("MonthlyHistoryItems") as System.Windows.Controls.ItemsControl
                        ?? throw new InvalidOperationException("monthly history list was not created");
                    var weeks = progressHistoryWindow.FindName("WeeklyComparisonItems") as System.Windows.Controls.ItemsControl
                        ?? throw new InvalidOperationException("weekly comparison list was not created");
                    True(close.IsDefault && close.MinHeight >= 40,
                        "progress history close action is unavailable or too small");
                    Eq(12, months.Items.Count);
                    Eq(8, weeks.Items.Count);
                    Eq("60 min", progressHistoryWindow.TotalCompletedMinutesLabel);
                }
                if (window is WelcomeWindow welcomeWindow)
                {
                    var continueButton = welcomeWindow.FindName("ContinueButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("welcome continue action was not created");
                    var notNowButton = welcomeWindow.FindName("NotNowButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("welcome defer action was not created");
                    True(continueButton.IsDefault && continueButton.MinHeight >= 40,
                        "welcome primary action is unavailable or too small");
                    True(notNowButton.MinHeight >= 40, "welcome defer action is too small");
                    Eq(0, repo.CompletedOnboardingStep);
                    continueButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    True(welcomeWindow.WelcomeCompleted, "welcome action did not report completion");
                    Eq(OnboardingSteps.Welcome, repo.CompletedOnboardingStep);
                }
                if (window is OnboardingRoutineWindow routineWindow)
                {
                    var objective = routineWindow.FindName("ObjectiveBox") as System.Windows.Controls.TextBox
                        ?? throw new InvalidOperationException("routine objective input was not created");
                    var deadline = routineWindow.FindName("DeadlinePicker") as System.Windows.Controls.DatePicker
                        ?? throw new InvalidOperationException("routine deadline input was not created");
                    var hours = routineWindow.FindName("HoursSlider") as System.Windows.Controls.Slider
                        ?? throw new InvalidOperationException("routine hours input was not created");
                    var save = routineWindow.FindName("SaveButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("routine save action was not created");
                    var dayChoices = VisualDescendants<System.Windows.Controls.Primitives.ToggleButton>(routineWindow)
                        .Where(choice => choice.Name.EndsWith("Choice", StringComparison.Ordinal))
                        .ToList();
                    Eq(7, dayChoices.Count);
                    True(dayChoices.All(choice => choice.MinHeight >= 40), "routine day targets are too small");
                    True(!save.IsEnabled, "incomplete routine must not be saved");

                    objective.Text = "ENEM 2027";
                    deadline.SelectedDate = new DateTime(2027, 11, 7);
                    hours.Value = 2.5;
                    foreach (var day in dayChoices)
                        day.IsChecked = day.Name is "MondayChoice" or "WednesdayChoice" or "FridayChoice";
                    routineWindow.UpdateLayout();
                    True(save.IsEnabled && save.IsDefault && save.MinHeight >= 40,
                        "complete routine is not ready for saving");
                    SaveWindowSnapshot(routineWindow, "dark-OnboardingRoutineWindow-configured");
                    save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    True(routineWindow.RoutineCompleted, "routine action did not report completion");
                    Eq(OnboardingSteps.Routine, repo.CompletedOnboardingStep);
                    Eq("ENEM 2027", repo.Settings.ObjectiveName);
                    Eq("2027-11-07", repo.Settings.ObjectiveDate);
                    Eq(2.5, repo.Settings.DailyHours);
                    Eq("Monday,Wednesday,Friday", string.Join(',', repo.Settings.AvailableStudyDays));
                }
                if (window is OnboardingFirstPlanWindow firstPlanWindow)
                {
                    var difficulties = firstPlanWindow.FindName("DifficultiesBox") as System.Windows.Controls.TextBox
                        ?? throw new InvalidOperationException("first-plan difficulties input was not created");
                    var generate = firstPlanWindow.FindName("GenerateButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("first-plan generation action was not created");
                    var preview = firstPlanWindow.FindName("PreviewPanel") as System.Windows.Controls.Border
                        ?? throw new InvalidOperationException("first-plan preview was not created");
                    var review = firstPlanWindow.FindName("ReviewAndApplyButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("first-plan review action was not created");
                    Eq(1, firstPlanAssistant.InitializeCalls);
                    True(!generate.IsEnabled, "first plan must require difficulties or an explicit unknown choice");
                    difficulties.Text = "Matemática, Química";
                    True(generate.IsEnabled && generate.MinHeight >= 40, "ready first plan cannot be generated");
                    generate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Eq(1, firstPlanAssistant.SendCalls);
                    Eq(AiProposalKind.StudyPlan, firstPlanAssistant.LastKind);
                    Eq("ENEM 2027", firstPlanAssistant.LastInput?.ObjectiveOrExam);
                    Eq("Matemática,Química", string.Join(',', firstPlanAssistant.LastInput?.WeakSubjects ?? new List<string>()));
                    Eq(Visibility.Visible, preview.Visibility);
                    Eq(Visibility.Visible, review.Visibility);
                    preview.BringIntoView();
                    firstPlanWindow.UpdateLayout();
                    SaveWindowSnapshot(firstPlanWindow, "dark-OnboardingFirstPlanWindow-preview");

                    _ = firstPlanWindow.Dispatcher.BeginInvoke(() =>
                    {
                        var confirmation = app.Windows.OfType<AiProposalConfirmationWindow>()
                            .Single(candidate => candidate.IsVisible);
                        var confirmButton = confirmation.FindName("ConfirmButton") as System.Windows.Controls.Button
                            ?? throw new InvalidOperationException("first-plan final confirmation was not created");
                        confirmButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    });
                    review.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Eq(1, firstPlanApplication.PrepareCalls);
                    Eq(1, firstPlanApplication.ApplyCalls);
                    True(firstPlanWindow.FirstPlanCompleted, "first-plan application did not complete onboarding");
                    Eq(OnboardingSteps.FirstPlan, repo.CompletedOnboardingStep);
                }
                if (window is OverdueRecoveryWindow recoveryWindow)
                {
                    var recoveryItems = recoveryWindow.FindName("OverdueItemsControl") as System.Windows.Controls.ItemsControl
                        ?? throw new InvalidOperationException("overdue recovery list was not created");
                    var prepareWithAi = recoveryWindow.FindName("PrepareWithAiButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("overdue AI recovery action was not created");
                    var viewOldest = recoveryWindow.FindName("ViewOldestButton") as System.Windows.Controls.Button
                        ?? throw new InvalidOperationException("view-oldest recovery action was not created");
                    Eq(2, recoveryItems.Items.Count);
                    True(prepareWithAi.MinHeight >= 40 && viewOldest.MinHeight >= 40,
                        "overdue recovery actions are too small");
                }
                window.Close();
            }
            Eq(1, assistant.CancelCalls);
            Eq(1, diagnostics.CancelCalls);
            Eq(1, firstPlanAssistant.CancelCalls);

            ThemeManager.Apply(ThemeManager.Light);
            AssertThemeContrast();
            var lightWeeklyWindow = new WeeklySummaryWindow(weeklyWindowSnapshot)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightWeeklyWindow.Show();
            lightWeeklyWindow.Width = lightWeeklyWindow.MinWidth;
            lightWeeklyWindow.Height = lightWeeklyWindow.MinHeight;
            lightWeeklyWindow.UpdateLayout();
            SaveWindowSnapshot(lightWeeklyWindow, "light-WeeklySummaryWindow");
            lightWeeklyWindow.Close();

            var lightSubjectWindow = new SubjectProgressWindow(weeklyWindowSnapshot)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightSubjectWindow.Show();
            lightSubjectWindow.Width = lightSubjectWindow.MinWidth;
            lightSubjectWindow.Height = lightSubjectWindow.MinHeight;
            lightSubjectWindow.UpdateLayout();
            SaveWindowSnapshot(lightSubjectWindow, "light-SubjectProgressWindow");
            lightSubjectWindow.Close();

            var lightHistoryWindow = new ProgressHistoryWindow(weeklyWindowSnapshot)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightHistoryWindow.Show();
            lightHistoryWindow.Width = lightHistoryWindow.MinWidth;
            lightHistoryWindow.Height = lightHistoryWindow.MinHeight;
            lightHistoryWindow.UpdateLayout();
            SaveWindowSnapshot(lightHistoryWindow, "light-ProgressHistoryWindow");
            lightHistoryWindow.Close();

            var deferredRepo = new StudyRepository(Path.Combine(dir, "deferred-state.json"), () => new DateTime(2026, 9, 1, 9, 0, 0));
            var lightWelcomeWindow = new WelcomeWindow(deferredRepo)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightWelcomeWindow.Show();
            lightWelcomeWindow.Width = lightWelcomeWindow.MinWidth;
            lightWelcomeWindow.Height = lightWelcomeWindow.MinHeight;
            lightWelcomeWindow.UpdateLayout();
            SaveWindowSnapshot(lightWelcomeWindow, "light-WelcomeWindow");
            var deferWelcome = lightWelcomeWindow.FindName("NotNowButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("welcome defer action was not created in the light theme");
            deferWelcome.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            True(!lightWelcomeWindow.WelcomeCompleted, "deferring welcome unexpectedly completed onboarding");
            Eq(0, deferredRepo.CompletedOnboardingStep);

            True(deferredRepo.CompleteOnboardingStep(OnboardingSteps.Welcome));
            var lightRoutineWindow = new OnboardingRoutineWindow(deferredRepo)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightRoutineWindow.Show();
            lightRoutineWindow.Width = lightRoutineWindow.MinWidth;
            lightRoutineWindow.Height = lightRoutineWindow.MinHeight;
            lightRoutineWindow.UpdateLayout();
            SaveWindowSnapshot(lightRoutineWindow, "light-OnboardingRoutineWindow");
            var deferRoutine = lightRoutineWindow.FindName("NotNowButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("routine defer action was not created in the light theme");
            deferRoutine.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            True(!lightRoutineWindow.RoutineCompleted, "deferring routine unexpectedly completed onboarding");
            Eq(OnboardingSteps.Welcome, deferredRepo.CompletedOnboardingStep);

            True(deferredRepo.SaveOnboardingRoutine(
                "Concurso 2027",
                "2027-10-10",
                3,
                new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }));
            var unavailableFirstPlan = new TestAiAssistantController(AiInstallationState.NotInstalled);
            var lightFirstPlanWindow = new OnboardingFirstPlanWindow(
                deferredRepo,
                unavailableFirstPlan,
                installation,
                new TestAiProposalApplicationService())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightFirstPlanWindow.Show();
            lightFirstPlanWindow.Width = lightFirstPlanWindow.MinWidth;
            lightFirstPlanWindow.Height = lightFirstPlanWindow.MinHeight;
            lightFirstPlanWindow.UpdateLayout();
            var unavailablePanel = lightFirstPlanWindow.FindName("AiUnavailablePanel") as System.Windows.Controls.Border
                ?? throw new InvalidOperationException("first-plan unavailable notice was not created");
            var unavailableGenerate = lightFirstPlanWindow.FindName("GenerateButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("first-plan generation action was not created in the light theme");
            Eq(Visibility.Visible, unavailablePanel.Visibility);
            True(!unavailableGenerate.IsEnabled, "first plan must not generate without a local installation");
            unavailablePanel.BringIntoView();
            lightFirstPlanWindow.UpdateLayout();
            SaveWindowSnapshot(lightFirstPlanWindow, "light-OnboardingFirstPlanWindow-unavailable");
            var deferFirstPlan = lightFirstPlanWindow.FindName("NotNowButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("first-plan defer action was not created in the light theme");
            deferFirstPlan.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            True(!lightFirstPlanWindow.FirstPlanCompleted, "deferring first plan unexpectedly completed onboarding");
            Eq(OnboardingSteps.Routine, deferredRepo.CompletedOnboardingStep);

            var existingPlanRepo = new StudyRepository(
                Path.Combine(dir, "existing-plan-state.json"),
                () => new DateTime(2026, 9, 1, 9, 0, 0));
            True(existingPlanRepo.CompleteOnboardingStep(OnboardingSteps.Welcome));
            True(existingPlanRepo.SaveOnboardingRoutine(
                "Concurso 2027",
                "2027-10-10",
                3,
                new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }));
            True(existingPlanRepo.ApplyPlan(StudyPlanImporter.Parse(PlanJson(
                "existing-plan",
                1,
                "2027-10-10",
                SessionJson("existing-session", "2026-09-01", 60)))).Success);
            var existingPlanAssistant = new TestAiAssistantController(AiInstallationState.NotInstalled);
            var existingPlanWindow = new OnboardingFirstPlanWindow(
                existingPlanRepo,
                existingPlanAssistant,
                installation,
                new TestAiProposalApplicationService())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            existingPlanWindow.Show();
            existingPlanWindow.Width = existingPlanWindow.MinWidth;
            existingPlanWindow.Height = existingPlanWindow.MinHeight;
            existingPlanWindow.UpdateLayout();
            var existingPlanPanel = existingPlanWindow.FindName("ExistingPlanPanel") as System.Windows.Controls.Border
                ?? throw new InvalidOperationException("existing-plan choice was not created");
            var newPlanPanel = existingPlanWindow.FindName("NewPlanPanel") as System.Windows.Controls.StackPanel
                ?? throw new InvalidOperationException("new-plan form was not created");
            var useExistingPlan = existingPlanWindow.FindName("UseExistingPlanButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("use-existing-plan action was not created");
            Eq(Visibility.Visible, existingPlanPanel.Visibility);
            Eq(Visibility.Collapsed, newPlanPanel.Visibility);
            Eq(0, existingPlanAssistant.InitializeCalls);
            True(useExistingPlan.MinHeight >= 40, "use-existing-plan click target is too small");
            SaveWindowSnapshot(existingPlanWindow, "light-OnboardingFirstPlanWindow-existing-plan");
            useExistingPlan.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            True(existingPlanWindow.FirstPlanCompleted, "keeping the current plan did not complete onboarding");
            Eq(OnboardingSteps.FirstPlan, existingPlanRepo.CompletedOnboardingStep);
            Eq("existing-plan", existingPlanRepo.Settings.ActivePlanId);
            True(existingPlanRepo.CaptureApplicationSnapshot().Sessions.Any(session => session.Id == "existing-session"),
                "keeping the current plan replaced its existing session");
            Eq(0, existingPlanAssistant.InitializeCalls);

            var overdueUiClock = new MutableClock(new DateTime(2026, 8, 31, 9, 0, 0));
            var overdueUiRepo = new StudyRepository(
                Path.Combine(dir, "overdue-ui-state.json"),
                () => overdueUiClock.Value);
            True(overdueUiRepo.ApplyPlan(StudyPlanImporter.Parse(PlanJson(
                "overdue-ui-plan",
                1,
                "2026-09-30",
                SessionJson("overdue-ui", "2026-09-01", 75),
                SessionJson("future-ui", "2026-09-04", 45)))).Success);
            overdueUiClock.Value = new DateTime(2026, 9, 3, 9, 0, 0);
            var overdueUiAssistant = new TestAiAssistantController();
            var overdueUiWindow = new MainWindow(
                overdueUiRepo,
                overdueUiAssistant,
                installation,
                application)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            overdueUiWindow.Show();
            overdueUiWindow.Width = overdueUiWindow.MinWidth;
            overdueUiWindow.Height = overdueUiWindow.MinHeight;
            overdueUiWindow.UpdateLayout();
            var overdueStatus = overdueUiWindow.FindName("OverdueStatusPanel") as System.Windows.Controls.Border
                ?? throw new InvalidOperationException("overdue status panel was not created");
            var overdueRecovery = overdueUiWindow.FindName("OverdueRecoveryButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("overdue recovery entry point was not created");
            Eq(Visibility.Visible, overdueStatus.Visibility);
            var overdueTitle = overdueUiWindow.OverdueStatusTitle;
            Contains(overdueTitle, "1 bloco atrasado");
            Contains(overdueTitle, "75 min");
            SaveWindowSnapshot(overdueUiWindow, "light-MainWindow-overdue-status");
            _ = overdueUiWindow.Dispatcher.BeginInvoke(() =>
            {
                var recovery = app.Windows.OfType<OverdueRecoveryWindow>()
                    .Single(candidate => candidate.IsVisible);
                SaveWindowSnapshot(recovery, "light-OverdueRecoveryWindow");
                var viewOldest = recovery.FindName("ViewOldestButton") as System.Windows.Controls.Button
                    ?? throw new InvalidOperationException("view-oldest action was not created");
                viewOldest.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            });
            overdueRecovery.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Contains(overdueUiWindow.SelectedDateHeading, "01 de Setembro");

            var recoveryRequest = OverdueRecoveryPresentation.BuildAiRequest(
                OverdueStudyAnalyzer.Analyze(overdueUiRepo.CaptureApplicationSnapshot()));
            var recoveryAssistant = new AiAssistantWindow(
                new TestAiAssistantController(),
                installation,
                application,
                overdueUiRepo,
                initialRequest: recoveryRequest,
                initialProposalKind: AiProposalKind.PlanChanges)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            recoveryAssistant.Show();
            recoveryAssistant.UpdateLayout();
            var recoveryRequestBox = recoveryAssistant.FindName("RequestBox") as System.Windows.Controls.TextBox
                ?? throw new InvalidOperationException("recovery assistant request was not created");
            var recoveryChangeChoice = recoveryAssistant.FindName("ChangePlanChoice") as System.Windows.Controls.RadioButton
                ?? throw new InvalidOperationException("recovery assistant proposal choice was not created");
            Contains(recoveryRequestBox.Text, "1 bloco atrasado");
            True(recoveryChangeChoice.IsChecked == true,
                "recovery assistant did not start as a plan-change proposal");
            recoveryAssistant.Close();
            overdueUiWindow.Close();

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
            SaveWindowSnapshot(unavailableWindow, "light-AiAssistantWindow");
            unavailableWindow.Close();

            var lightDiagnosticsController = new TestAiAssistantController();
            var lightHardwareDiagnostics = new TestAiHardwareDiagnosticsService();
            var lightPerformanceDiagnostics = new TestAiPerformanceDiagnosticsService();
            var lightDiagnosticsWindow = new AiDiagnosticsWindow(
                lightDiagnosticsController,
                lightHardwareDiagnostics,
                lightPerformanceDiagnostics)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            lightDiagnosticsWindow.Show();
            lightDiagnosticsWindow.Width = lightDiagnosticsWindow.MinWidth;
            lightDiagnosticsWindow.Height = lightDiagnosticsWindow.MinHeight;
            lightDiagnosticsWindow.UpdateLayout();
            var lightBackground = lightDiagnosticsWindow.Background as SolidColorBrush
                ?? throw new InvalidOperationException("AI diagnostics light background was not created");
            Eq((ThemeManager.ResourceBrush("BackgroundBrush") as SolidColorBrush)!.Color, lightBackground.Color);
            SaveWindowSnapshot(lightDiagnosticsWindow, "light-AiDiagnosticsWindow");
            var lightPerformanceButton = lightDiagnosticsWindow.FindName("RunPerformanceTestButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("AI performance test action was not created in the light theme");
            var lightPerformanceDetails = lightDiagnosticsWindow.FindName("PerformanceDetailsPanel") as System.Windows.Controls.Border
                ?? throw new InvalidOperationException("AI performance result was not created in the light theme");
            True(lightPerformanceButton.IsEnabled, "AI performance test must be enabled in the light theme");
            lightPerformanceButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            lightPerformanceDetails.BringIntoView();
            lightDiagnosticsWindow.UpdateLayout();
            Eq(Visibility.Visible, lightPerformanceDetails.Visibility);
            SaveWindowSnapshot(lightDiagnosticsWindow, "light-AiDiagnosticsWindow-performance");
            lightDiagnosticsWindow.Close();
            Eq(1, lightDiagnosticsController.InitializeCalls);
            Eq(1, lightDiagnosticsController.CancelCalls);
            Eq(1, lightHardwareDiagnostics.AnalyzeCalls);
            Eq(1, lightPerformanceDiagnostics.MeasureCalls);

            var unavailableDiagnosticsController = new TestAiAssistantController(AiInstallationState.NotInstalled);
            var unavailableDiagnosticsPerformance = new TestAiPerformanceDiagnosticsService();
            var unavailableDiagnosticsWindow = new AiDiagnosticsWindow(
                unavailableDiagnosticsController,
                new TestAiHardwareDiagnosticsService(),
                unavailableDiagnosticsPerformance)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                ShowInTaskbar = false
            };
            unavailableDiagnosticsWindow.Show();
            unavailableDiagnosticsWindow.UpdateLayout();
            var unavailablePerformanceButton = unavailableDiagnosticsWindow.FindName("RunPerformanceTestButton") as System.Windows.Controls.Button
                ?? throw new InvalidOperationException("unavailable AI performance action was not created");
            var unavailablePerformanceStatus = unavailableDiagnosticsWindow.FindName("PerformanceStatusText") as System.Windows.Controls.TextBlock
                ?? throw new InvalidOperationException("unavailable AI performance status was not created");
            True(!unavailablePerformanceButton.IsEnabled,
                "AI performance test must stay disabled without a local installation");
            Contains(unavailablePerformanceStatus.Text, "Instale a IA");
            Eq(0, unavailableDiagnosticsPerformance.MeasureCalls);
            unavailableDiagnosticsWindow.Close();
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
        if (failure is not null)
            throw new InvalidOperationException("desktop window smoke failed: " + failure.Message, failure);
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

static void SaveWindowSnapshot(Window window, string name)
{
    var outputDirectory = Environment.GetEnvironmentVariable("ROTA_TEST_UI_SNAPSHOTS");
    if (string.IsNullOrWhiteSpace(outputDirectory)) return;

    Directory.CreateDirectory(outputDirectory);
    var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
        width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    bitmap.Render(window);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    using var output = new FileStream(
        Path.Combine(outputDirectory, name + ".png"), FileMode.Create, FileAccess.Write, FileShare.None);
    encoder.Save(output);
}

static void AssertThemeContrast()
{
    AssertContrast("TextBrush", "BackgroundBrush", 7.0);
    AssertContrast("TextBrush", "SurfaceRaisedBrush", 7.0);
    AssertContrast("MutedBrush", "BackgroundBrush", 4.5);
    AssertContrast("MutedBrush", "SurfaceRaisedBrush", 4.5);
    AssertContrast("PrimaryTextBrush", "PrimarySoftBrush", 4.5);
    AssertContrast("SuccessBrush", "SuccessSoftBrush", 4.5);
    AssertContrast("ReviewBrush", "ReviewSoftBrush", 4.5);
    AssertContrast("AssessmentBrush", "AssessmentSoftBrush", 4.5);
    AssertContrast("ErrorBrush", "ErrorSoftBrush", 4.5);
}

static void AssertContrast(string foregroundKey, string backgroundKey, double minimum)
{
    var foreground = ThemeManager.ResourceBrush(foregroundKey) as SolidColorBrush
        ?? throw new InvalidOperationException($"{foregroundKey} is not a solid color");
    var background = ThemeManager.ResourceBrush(backgroundKey) as SolidColorBrush
        ?? throw new InvalidOperationException($"{backgroundKey} is not a solid color");
    var lighter = Math.Max(RelativeLuminance(foreground.Color), RelativeLuminance(background.Color));
    var darker = Math.Min(RelativeLuminance(foreground.Color), RelativeLuminance(background.Color));
    var ratio = (lighter + 0.05) / (darker + 0.05);
    True(ratio >= minimum,
        $"{foregroundKey} on {backgroundKey} has contrast {ratio:F2}:1; expected at least {minimum:F1}:1");
}

static double RelativeLuminance(Color color) =>
    0.2126 * LinearChannel(color.R) +
    0.7152 * LinearChannel(color.G) +
    0.0722 * LinearChannel(color.B);

static double LinearChannel(byte channel)
{
    var value = channel / 255d;
    return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
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

static void MonthSummaryCountsDistinctSubjects()
{
    var sessions = new[]
    {
        new SessionItem { Subject = "Matemática" },
        new SessionItem { Subject = "Matemática" },
        new SessionItem { Subject = "Física" },
        new SessionItem { Subject = "Física" },
        new SessionItem { Subject = "Química" }
    };
    var view = new MonthDayCardView(
        new DateOnly(2031, 2, 3), sessions, currentMonth: true, selected: false, today: false);
    Eq("Matemática • Física • +1", view.Summary);
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

static SessionItem OverdueSession(
    string id,
    string date,
    int minutes,
    string kind = "study",
    string origin = "plan",
    bool completed = false) => new()
    {
        Id = id,
        PlanId = "overdue-test-plan",
        PlanRevision = 1,
        Date = date,
        Subject = "Matemática",
        Topic = "Frações",
        Minutes = minutes,
        Target = "Resolver exercícios",
        Kind = kind,
        ReviewLabel = kind == "review" ? "D+1" : "",
        Status = completed ? "completed" : "planned",
        Origin = origin,
        CompletedAtUnixMs = completed ? 1 : 0
    };

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

static void InstalledLocalAiPerformanceDiagnostic()
{
    var aiRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rota",
        "AI");
    var protectedFiles = new[] { "config.json", "conversation.json", "proposals.json" }
        .Select(name => Path.Combine(aiRoot, name))
        .Where(File.Exists)
        .ToDictionary(path => path, FileSha256, StringComparer.OrdinalIgnoreCase);
    var testDirectory = Path.Combine(
        Path.GetTempPath(),
        "RotaRealPerformanceTests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    try
    {
        var repository = new StudyRepository(Path.Combine(testDirectory, "state.json"));
        var services = LocalAiServices.Create(repository, aiRoot);
        try
        {
            var report = services.PerformanceDiagnostics.MeasureAsync().GetAwaiter().GetResult();
            True(report.StartupDuration > TimeSpan.Zero, "real local AI startup was not measured");
            True(report.ResponseDuration > TimeSpan.Zero, "real local AI response was not measured");
            True(report.GeneratedTokens > 0, "real local AI returned no measured tokens");
            True(report.TokensPerSecond > 0, "real local AI returned no token rate");
            True(services.RuntimeHost.Status.State == AiRuntimeState.Stopped,
                "temporary real local AI runtime was not stopped");
            Console.WriteLine(
                $"      Real diagnostic: startup {report.StartupDuration.TotalSeconds:0.0}s, " +
                $"response {report.ResponseDuration.TotalSeconds:0.0}s, " +
                $"{report.TokensPerSecond:0.0} tokens/s via {report.ComputePreference}.");
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        foreach (var protectedFile in protectedFiles)
        {
            True(File.Exists(protectedFile.Key), $"protected AI file disappeared: {protectedFile.Key}");
            Eq(protectedFile.Value, FileSha256(protectedFile.Key));
        }
    }
    finally
    {
        try { Directory.Delete(testDirectory, recursive: true); } catch { }
    }
}

static string FileSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
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
    private readonly AiPreparedApplication? _prepared;
    private readonly AiProposalApplicationResult? _applicationResult;

    public TestAiProposalApplicationService(
        AiPreparedApplication? prepared = null,
        AiProposalApplicationResult? applicationResult = null)
    {
        _prepared = prepared;
        _applicationResult = applicationResult;
    }

    public int ReconcileCalls { get; private set; }
    public int PrepareCalls { get; private set; }
    public int ApplyCalls { get; private set; }

    public Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReconcileCalls++;
        return Task.CompletedTask;
    }

    public Task<AiPreparedApplication> PrepareAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrepareCalls++;
        if (_prepared is null || _prepared.ProposalId != proposalId) throw new NotSupportedException();
        return Task.FromResult(_prepared);
    }

    public Task<AiProposalApplicationResult> ApplyAsync(Guid confirmationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCalls++;
        if (_prepared is null || _applicationResult is null || _prepared.ConfirmationId != confirmationId)
            throw new NotSupportedException();
        return Task.FromResult(_applicationResult);
    }

    public Task<AiProposalApplicationResult> UndoAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AiStoredProposal> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public CalendarApplicationState GetCalendarState(Guid proposalId) =>
        new(false, false, false, false, "");
}

sealed class TestAiHardwareDiagnosticsService : IAiHardwareDiagnosticsService
{
    public int AnalyzeCalls { get; private set; }

    public Task<AiHardwareDiagnosticReport> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnalyzeCalls++;
        return Task.FromResult(new AiHardwareDiagnosticReport(
            new AiHardwareProfile(
                "AMD Ryzen 7 5700X",
                16,
                16L * AiProfileRecommendationPolicy.Gibibyte,
                "NVIDIA GeForce RTX 3070",
                8L * AiProfileRecommendationPolicy.Gibibyte,
                AiProfile.Performance,
                Array.Empty<string>()),
            new AiStorageSnapshot(
                @"C:\Rota\AI",
                1_000L * AiProfileRecommendationPolicy.Gibibyte,
                400L * AiProfileRecommendationPolicy.Gibibyte),
            new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero)));
    }
}

sealed class TestAiPerformanceDiagnosticsService : IAiPerformanceDiagnosticsService
{
    public int MeasureCalls { get; private set; }

    public Task<AiPerformanceDiagnosticReport> MeasureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MeasureCalls++;
        return Task.FromResult(new AiPerformanceDiagnosticReport(
            TimeSpan.FromSeconds(3.4),
            TimeSpan.FromSeconds(1.2),
            24,
            20,
            AiProfile.Performance,
            AiComputePreference.Gpu,
            RuntimeWasAlreadyReady: false,
            AiPerformanceRating.Excellent,
            new DateTimeOffset(2026, 9, 6, 15, 0, 0, TimeSpan.Zero)));
    }
}

sealed class RecordingReminderTaskRunner : IReminderTaskRunner
{
    private readonly Queue<ReminderTaskResult> _results;

    public RecordingReminderTaskRunner(params ReminderTaskResult[] results) =>
        _results = new Queue<ReminderTaskResult>(results);

    public List<IReadOnlyList<string>> Calls { get; } = new();

    public ReminderTaskResult Run(IReadOnlyList<string> arguments)
    {
        Calls.Add(arguments.ToArray());
        return _results.Count == 0 ? new ReminderTaskResult(0, "") : _results.Dequeue();
    }
}

sealed class RecordingStudyReminderScheduler : IStudyReminderScheduler
{
    public List<StudyReminderConfiguration> Configurations { get; } = new();

    public void Apply(StudyReminderConfiguration configuration, string executablePath) =>
        Configurations.Add(configuration);
}

sealed class TestAiAssistantController : IAiAssistantController
{
    private readonly AiInstallationState _installationState;
    private readonly AiStoredProposal? _sendResult;

    public TestAiAssistantController(
        AiInstallationState installationState = AiInstallationState.Ready,
        AiStoredProposal? sendResult = null)
    {
        _installationState = installationState;
        _sendResult = sendResult;
    }

    public AiAssistantState State { get; private set; } = new();
    public int InitializeCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public int SendCalls { get; private set; }
    public AiAssistantInput? LastInput { get; private set; }
    public AiProposalKind? LastKind { get; private set; }
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
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendCalls++;
        LastInput = input;
        LastKind = kind;
        return Task.FromResult(_sendResult);
    }

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
