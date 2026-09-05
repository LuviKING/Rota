using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiProposalApplicationTests
{
    private static readonly DateTimeOffset CreatedAt = new(2031, 2, 3, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI application preparation is read-only and revalidates the current calendar", PreparationIsReadOnly),
        ("AI application persists calendar receipt and proposal status atomically", ApplicationPersistsReceiptAndStatus),
        ("AI application rejects an expired confirmation after preferences change", ConfirmationExpiresAfterCalendarChange),
        ("AI application confirmation is one-time and prevents duplicate apply", ConfirmationIsOneTime),
        ("AI application undo survives restart and keeps plan revisions monotonic", UndoSurvivesRestart),
        ("AI application undo refuses to erase a later completion", UndoProtectsLaterCompletion),
        ("AI application executes structured changes and persists planning preferences", StructuredChangesAreApplied),
        ("AI application blocks a protected runtime review", ProtectedRuntimeReviewIsBlocked),
        ("AI application final validation includes completed future load", CompletedFutureLoadIsCounted),
        ("AI application reconciles proposal history from the calendar receipt", ReceiptReconcilesHistory),
        ("AI application reconciles an undo rolled back by state recovery", RecoveredUndoIsReconciled),
        ("AI application rejects a confirmation when the calendar date changes", ConfirmationExpiresAtMidnight),
        ("AI application persistence failure leaves calendar and receipt unchanged", PersistenceFailureIsAtomic)
    };

    private static void PreparationIsReadOnly()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(1);
            harness.Save(proposal);
            var stateBefore = File.ReadAllBytes(harness.Repository.DataPath);
            var historyBefore = File.ReadAllBytes(harness.Store.StorePath);

            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();

            Require(prepared.CanApply, prepared.Preview.Message);
            Require(prepared.Preview.CanProceed, "current preview should be ready");
            Require(File.ReadAllBytes(harness.Repository.DataPath).SequenceEqual(stateBefore), "preparation changed calendar state");
            Require(File.ReadAllBytes(harness.Store.StorePath).SequenceEqual(historyBefore), "preparation changed proposal history");
        });
    }

    private static void ApplicationPersistsReceiptAndStatus()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(2);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            var applied = harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(applied.Success && applied.HistorySynchronized, applied.Message);
            Require(harness.Repository.Settings.ActivePlanId == "ai-plan-2", "new plan was not active");
            var state = harness.Repository.AiApplicationState(proposal.Id);
            Require(state.Exists && state.IsApplied && state.CanUndo, state.Message);
            Require(harness.Store.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Applied,
                "history was not marked applied");

            var reloaded = new StudyRepository(harness.Repository.DataPath, () => harness.Clock.Value);
            Require(reloaded.AiApplicationState(proposal.Id).IsApplied, "receipt did not survive restart");
            Require(reloaded.Settings.ActivePlanId == "ai-plan-2", "calendar did not survive restart");
        });
    }

    private static void ConfirmationExpiresAfterCalendarChange()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(3);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            harness.Repository.SavePreferences("Objetivo alterado", "2031-12-31", 4.5, 60, true, true, true);

            var applied = harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(!applied.Success, "stale confirmation was accepted");
            Require(harness.Repository.Settings.ActivePlanId == "base-plan", "stale proposal changed active plan");
            Require(harness.Repository.Settings.ObjectiveName == "Objetivo alterado", "later preference was lost");
            Require(!harness.Repository.AiApplicationState(proposal.Id).Exists, "stale proposal created a receipt");
        });
    }

    private static void ConfirmationIsOneTime()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(4);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            Require(harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult().Success,
                "first confirmation failed");
            var stateAfterFirst = File.ReadAllBytes(harness.Repository.DataPath);

            var second = harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(!second.Success, "confirmation was reused");
            Require(File.ReadAllBytes(harness.Repository.DataPath).SequenceEqual(stateAfterFirst), "second click changed calendar");
            Require(harness.Repository.AiApplicationReceipts().Count == 1, "duplicate receipt was created");
        });
    }

    private static void UndoSurvivesRestart()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(5);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            Require(harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult().Success,
                "application failed");

            harness.Service.Dispose();
            harness.ServiceDisposed = true;
            using var reopenedStore = new AiProposalStore(harness.Store.StorePath, () => CreatedAt.AddMinutes(5));
            var reopenedRepository = new StudyRepository(harness.Repository.DataPath, () => harness.Clock.Value);
            var preview = new AiProposalPreviewService(reopenedRepository,
                new StudyPlanningContextProvider(reopenedRepository, () => harness.Clock.Value));
            using var reopenedService = new AiProposalApplicationService(reopenedRepository, preview, reopenedStore);
            reopenedService.ReconcileAsync().GetAwaiter().GetResult();

            var undone = reopenedService.UndoAsync(proposal.Id).GetAwaiter().GetResult();

            Require(undone.Success, undone.Message);
            Require(reopenedRepository.Settings.ActivePlanId == "base-plan", "previous active plan was not restored");
            Require(reopenedRepository.UpcomingSessions(new DateOnly(2031, 2, 3), 20)
                .Any(session => session.Id == "base-1"), "previous sessions were not restored");
            Require(!reopenedRepository.UpcomingSessions(new DateOnly(2031, 2, 3), 20)
                .Any(session => session.Id == "ai-session-5"), "AI session remained after undo");
            Require(reopenedRepository.Settings.PlanRevisions["ai-plan-5"] == 1,
                "undo reduced the monotonic revision index");
            Require(reopenedStore.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Undone,
                "history was not marked undone");
            Require(reopenedRepository.AiApplicationState(proposal.Id).IsUndone, "receipt was not marked undone");
        });
    }

    private static void UndoProtectsLaterCompletion()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(6);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            Require(harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult().Success,
                "application failed");
            Require(harness.Repository.MarkCompleted("ai-plan-6", "ai-session-6"), "completion failed");

            var undone = harness.Service.UndoAsync(proposal.Id).GetAwaiter().GetResult();

            Require(!undone.Success, "undo erased a later completion");
            var completed = harness.Repository.SessionsForDate(new DateOnly(2031, 2, 6))
                .Single(session => session.Id == "ai-session-6");
            Require(completed.IsCompleted, "completed session was changed");
            Require(harness.Repository.UpcomingSessions(new DateOnly(2031, 2, 3), 20)
                .Any(session => session.Origin == "runtime"), "runtime reviews were lost");
        });
    }

    private static void StructuredChangesAreApplied()
    {
        WithHarness(harness =>
        {
            var proposal = ChangesProposal(7,
                Operation(71, AiPlanOperationType.MoveSession, "Mover Matemática") with
                {
                    SessionId = "base-1",
                    DestinationDate = "2031-02-07"
                },
                Operation(72, AiPlanOperationType.RemoveFutureSession, "Remover Física") with
                {
                    SessionId = "base-2"
                },
                Operation(73, AiPlanOperationType.AddSession, "Adicionar Química") with
                {
                    Subject = "Química",
                    DestinationDate = "2031-02-08",
                    Minutes = 30
                },
                Operation(74, AiPlanOperationType.ChangeSubjectPriority, "Priorizar Matemática") with
                {
                    Subject = "Matemática",
                    Priority = 90
                },
                Operation(75, AiPlanOperationType.SetAvailability, "Atualizar disponibilidade") with
                {
                    MaxHoursPerDay = 2.5,
                    AvailableDays = Enum.GetValues<DayOfWeek>().ToList()
                });
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            Require(prepared.CanApply, prepared.Preview.Message);

            var result = harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(result.Success, result.Message);
            Require(harness.Repository.SessionsForDate(new DateOnly(2031, 2, 7)).Any(session => session.Id == "base-1"),
                "move was not applied");
            Require(!harness.Repository.UpcomingSessions(new DateOnly(2031, 2, 3), 30).Any(session => session.Id == "base-2"),
                "remove was not applied");
            Require(harness.Repository.SessionsForDate(new DateOnly(2031, 2, 8)).Any(session =>
                    session.Id == $"ai-{proposal.Id:N}-{OperationId(73):N}" && session.Subject == "Química"),
                "addition was not applied with a deterministic ID");
            Require(Math.Abs(harness.Repository.Settings.DailyHours - 2.5) < 0.001, "fractional availability was not persisted");
            Require(harness.Repository.Settings.SubjectPriorities["Matemática"] == 90, "priority was not persisted");
        });
    }

    private static void ProtectedRuntimeReviewIsBlocked()
    {
        WithHarness(harness =>
        {
            Require(harness.Repository.MarkCompleted("base-plan", "base-1"), "could not create runtime review");
            var review = harness.Repository.UpcomingSessions(new DateOnly(2031, 2, 3), 20)
                .First(session => session.Origin == "runtime");
            var proposal = ChangesProposal(8, Operation(81, AiPlanOperationType.RemoveFutureSession, "Remover revisão") with
            {
                SessionId = review.Id
            });
            harness.Store.SavePreviewAsync(proposal, new AiProposalPreview
            {
                ProposalId = proposal.Id,
                Kind = AiProposalKind.PlanChanges,
                State = AiProposalPreviewState.Ready,
                Message = "Prévia antiga pronta.",
                BeforeSessionCount = 2,
                AfterSessionCount = 1,
                BeforeMinutes = 80,
                AfterMinutes = 60,
                RemovedSessionCount = 1,
                Operations = new List<AiPreviewOperation>
                {
                    new()
                    {
                        OperationId = OperationId(81),
                        Type = AiPlanOperationType.RemoveFutureSession,
                        Summary = "Remover revisão",
                        SessionId = review.Id,
                        OriginalDate = review.Date
                    }
                }
            }).GetAwaiter().GetResult();

            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();

            Require(!prepared.CanApply, "protected review became applicable");
            Require(prepared.Preview.Message.Contains("protegida", StringComparison.OrdinalIgnoreCase),
                prepared.Preview.Message);
        });
    }

    private static void CompletedFutureLoadIsCounted()
    {
        WithHarness(harness =>
        {
            harness.Repository.SavePreferences("ENEM", "2031-12-31", 1, 60, false, false, false);
            Require(harness.Repository.MarkCompleted("base-plan", "base-1"), "future completion failed");
            var proposal = ChangesProposal(9, Operation(91, AiPlanOperationType.AddSession, "Adicionar carga") with
            {
                Subject = "Química",
                DestinationDate = "2031-02-04",
                Minutes = 30
            });
            harness.Save(proposal);

            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();

            Require(!prepared.CanApply, "completed future load was ignored");
            Require(prepared.Preview.Message.Contains("limite", StringComparison.OrdinalIgnoreCase), prepared.Preview.Message);
        });
    }

    private static void ReceiptReconcilesHistory()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(10);
            harness.Save(proposal);
            var faulting = new FaultingProposalStore(harness.Store);
            using var service = new AiProposalApplicationService(harness.Repository, harness.Preview, faulting);
            var prepared = service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            var applied = service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(applied.Success && !applied.HistorySynchronized, "calendar commit should survive history failure");
            Require(harness.Repository.AiApplicationState(proposal.Id).IsApplied, "receipt was not committed");
            Require(harness.Store.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Validated,
                "faulting store unexpectedly changed status");

            harness.Service.ReconcileAsync().GetAwaiter().GetResult();
            Require(harness.Store.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Applied,
                "receipt did not reconcile history");
        });
    }

    private static void ConfirmationExpiresAtMidnight()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(11);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            harness.Clock.Value = harness.Clock.Value.AddDays(1);

            var applied = harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(!applied.Success, "yesterday's confirmation was accepted");
            Require(!harness.Repository.AiApplicationState(proposal.Id).Exists, "expired confirmation created receipt");
        });
    }

    private static void RecoveredUndoIsReconciled()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(13);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            Require(harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult().Success,
                "application failed");
            Require(harness.Service.UndoAsync(proposal.Id).GetAwaiter().GetResult().Success, "undo failed");
            Require(harness.Store.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Undone,
                "undo status was not stored");

            File.WriteAllText(harness.Repository.DataPath, "{invalid");
            var recoveredRepository = new StudyRepository(harness.Repository.DataPath, () => harness.Clock.Value);
            Require(recoveredRepository.AiApplicationState(proposal.Id).IsApplied,
                "atomic backup did not restore the applied receipt");
            var preview = new AiProposalPreviewService(recoveredRepository,
                new StudyPlanningContextProvider(recoveredRepository, () => harness.Clock.Value));
            using var service = new AiProposalApplicationService(recoveredRepository, preview, harness.Store);

            service.ReconcileAsync().GetAwaiter().GetResult();

            Require(harness.Store.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Applied,
                "recovered receipt did not restore applied history status");
            Require(service.UndoAsync(proposal.Id).GetAwaiter().GetResult().Success,
                "recovered application could not be safely undone again");
        });
    }

    private static void PersistenceFailureIsAtomic()
    {
        WithHarness(harness =>
        {
            var proposal = NewPlanProposal(12);
            harness.Save(proposal);
            var prepared = harness.Service.PrepareAsync(proposal.Id).GetAwaiter().GetResult();
            var before = File.ReadAllBytes(harness.Repository.DataPath);
            using var locked = new FileStream(
                harness.Repository.DataPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var applied = harness.Service.ApplyAsync(prepared.ConfirmationId).GetAwaiter().GetResult();

            Require(!applied.Success, "application reported success while state was locked");
            Require(!harness.Repository.AiApplicationState(proposal.Id).Exists, "failed persistence changed memory receipt");
            locked.Dispose();
            Require(File.ReadAllBytes(harness.Repository.DataPath).SequenceEqual(before), "failed persistence changed disk state");
        });
    }

    private static AiProposal NewPlanProposal(int suffix) => new()
    {
        Id = ProposalId(suffix),
        CreatedAtUtc = CreatedAt,
        Summary = $"Novo plano seguro {suffix}.",
        Kind = AiProposalKind.StudyPlan,
        StudyPlan = new AiStudyPlanDraft { StudyPlanJson = StudyPlanJson(suffix) },
        Warnings = new List<string>()
    };

    private static AiProposal ChangesProposal(int suffix, params AiPlanOperation[] operations) => new()
    {
        Id = ProposalId(suffix),
        CreatedAtUtc = CreatedAt,
        Summary = $"Ajustes seguros {suffix}.",
        Kind = AiProposalKind.PlanChanges,
        Changes = new AiPlanChangeDraft { Operations = operations.ToList() },
        Warnings = new List<string>()
    };

    private static AiPlanOperation Operation(int suffix, AiPlanOperationType type, string summary) => new()
    {
        Id = OperationId(suffix),
        Type = type,
        Summary = summary
    };

    private static Guid ProposalId(int suffix) =>
        Guid.Parse($"20000000-0000-0000-0000-{suffix:000000000000}");

    private static Guid OperationId(int suffix) =>
        Guid.Parse($"30000000-0000-0000-0000-{suffix:000000000000}");

    private static string StudyPlanJson(int suffix) => $$"""
        {
          "format":"studyplan",
          "format_version":"0.2",
          "plan":{"id":"ai-plan-{{suffix}}","revision":1,"title":"Plano IA {{suffix}}"},
          "objective":{"name":"ENEM","date":"2031-12-31"},
          "sessions":[{
            "id":"ai-session-{{suffix}}",
            "date":"2031-02-06",
            "subject":"Química",
            "topic":"Estequiometria",
            "minutes":60,
            "target":"Resolver 10 questões",
            "kind":"study"
          }]
        }
        """;

    private static PlanPackage BasePlan() => new(
        "base-plan",
        1,
        "Plano base",
        "ENEM",
        "2031-12-31",
        new List<SessionItem>
        {
            Session("base-1", "2031-02-04", "Matemática", 60),
            Session("base-2", "2031-02-05", "Física", 60)
        });

    private static SessionItem Session(string id, string date, string subject, int minutes) => new()
    {
        Id = id,
        PlanId = "base-plan",
        PlanRevision = 1,
        Date = date,
        Subject = subject,
        Topic = "Tópico",
        Minutes = minutes,
        Target = "Resolver exercícios",
        Kind = "study",
        Status = "planned",
        Origin = "plan"
    };

    private static void WithHarness(Action<Harness> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaAiApplicationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var harness = new Harness(directory);
            action(harness);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class MutableClock
    {
        public DateTime Value { get; set; } = new(2031, 2, 3, 9, 0, 0, DateTimeKind.Local);
    }

    private sealed class Harness : IDisposable
    {
        public Harness(string directory)
        {
            Clock = new MutableClock();
            Repository = new StudyRepository(Path.Combine(directory, "state.json"), () => Clock.Value);
            Require(Repository.ApplyPlan(BasePlan()).Success, "base plan failed");
            Store = new AiProposalStore(Path.Combine(directory, "AI", "proposals.json"), () => CreatedAt.AddMinutes(1));
            Store.LoadAsync().GetAwaiter().GetResult();
            Preview = new AiProposalPreviewService(Repository,
                new StudyPlanningContextProvider(Repository, () => Clock.Value));
            Service = new AiProposalApplicationService(Repository, Preview, Store);
        }

        public MutableClock Clock { get; }
        public StudyRepository Repository { get; }
        public AiProposalStore Store { get; }
        public AiProposalPreviewService Preview { get; }
        public AiProposalApplicationService Service { get; set; }
        public bool ServiceDisposed { get; set; }

        public void Save(AiProposal proposal)
        {
            var context = proposal.Kind == AiProposalKind.PlanChanges
                ? new StudyPlanningContextProvider(Repository, () => Clock.Value).Capture()
                : null;
            var preview = Preview.Preview(proposal, context);
            Store.SavePreviewAsync(proposal, preview).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!ServiceDisposed) Service.Dispose();
            Store.Dispose();
        }
    }

    private sealed class FaultingProposalStore : IAiProposalStore
    {
        private readonly IAiProposalStore _inner;

        public FaultingProposalStore(IAiProposalStore inner) => _inner = inner;
        public string StorePath => _inner.StorePath;
        public string LastLoadWarning => _inner.LastLoadWarning;
        public Task<IReadOnlyList<AiStoredProposal>> LoadAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadAsync(cancellationToken);
        public Task<AiStoredProposal> SavePreviewAsync(AiProposal proposal, AiProposalPreview preview, CancellationToken cancellationToken = default) =>
            _inner.SavePreviewAsync(proposal, preview, cancellationToken);
        public Task<AiStoredProposal> AcceptAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            _inner.AcceptAsync(proposalId, cancellationToken);
        public Task<AiStoredProposal> MarkAppliedAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada no histórico.");
        public Task<AiStoredProposal> MarkUndoneAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            _inner.MarkUndoneAsync(proposalId, cancellationToken);
        public Task<AiStoredProposal> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            _inner.RejectAsync(proposalId, cancellationToken);
    }
}
