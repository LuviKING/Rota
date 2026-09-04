using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class ProposalPreviewTests
{
    private static readonly DateTime Today = new(2031, 2, 3, 9, 0, 0, DateTimeKind.Local);
    private static readonly DateTimeOffset CreatedAt = new(2031, 2, 3, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI preview simulates move add and remove without writing state", PreviewSimulatesConcreteChanges),
        ("AI preview blocks direct changes to protected reviews", PreviewBlocksProtectedReview),
        ("AI preview blocks daily capacity conflicts", PreviewBlocksCapacityConflict),
        ("AI preview redistributes deterministically around protected load", PreviewRedistributesDeterministically),
        ("AI preview validates a new StudyPlan without applying it", PreviewValidatesStudyPlanWithoutApplying),
        ("AI preview blocks truncated and underspecified change proposals", PreviewBlocksUnsafeScope)
    };

    private static void PreviewSimulatesConcreteChanges()
    {
        WithService((service, repository) =>
        {
            var before = File.ReadAllBytes(repository.DataPath);
            var proposal = ChangesProposal(
                Operation(AiPlanOperationType.MoveSession, "Mover Matemática") with
                {
                    SessionId = "session-1",
                    DestinationDate = "2031-02-07"
                },
                Operation(AiPlanOperationType.RemoveFutureSession, "Remover Física") with
                {
                    SessionId = "session-2"
                },
                Operation(AiPlanOperationType.AddSession, "Adicionar Química") with
                {
                    Subject = "Química",
                    DestinationDate = "2031-02-08",
                    Minutes = 30
                });

            var preview = service.Preview(proposal, Context());

            Require(preview.CanProceed);
            Require(preview.BeforeSessionCount == 3 && preview.AfterSessionCount == 3);
            Require(preview.BeforeMinutes == 140 && preview.AfterMinutes == 110);
            Require(preview.AddedSessionCount == 1);
            Require(preview.RemovedSessionCount == 1);
            Require(preview.MovedSessionCount == 1);
            Require(preview.Operations.Count == 3);
            Require(File.ReadAllBytes(repository.DataPath).SequenceEqual(before));
        });
    }

    private static void PreviewBlocksProtectedReview()
    {
        WithService((service, _) =>
        {
            var proposal = ChangesProposal(Operation(AiPlanOperationType.RemoveFutureSession, "Remover revisão") with
            {
                SessionId = "review-1"
            });
            var preview = service.Preview(proposal, Context());
            Require(!preview.CanProceed);
            Require(preview.Message.Contains("protegida", StringComparison.OrdinalIgnoreCase));
            Require(preview.AfterSessionCount == preview.BeforeSessionCount);
        });
    }

    private static void PreviewBlocksCapacityConflict()
    {
        WithService((service, _) =>
        {
            var context = Context() with { DailyMinutesLimit = 90 };
            var proposal = ChangesProposal(Operation(AiPlanOperationType.MoveSession, "Juntar sessões") with
            {
                SessionId = "session-1",
                DestinationDate = "2031-02-05"
            });
            var preview = service.Preview(proposal, context);
            Require(!preview.CanProceed);
            Require(preview.Message.Contains("excede", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void PreviewRedistributesDeterministically()
    {
        WithService((service, _) =>
        {
            var proposal = ChangesProposal(Operation(AiPlanOperationType.RedistributeLoad, "Redistribuir carga") with
            {
                MaxHoursPerDay = 1,
                AvailableDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday }
            });
            var preview = service.Preview(proposal, Context());
            Require(preview.CanProceed);
            Require(preview.MovedSessionCount == 2);
            var movements = preview.Operations.Where(item => item.Type == AiPlanOperationType.MoveSession).ToList();
            Require(movements.Count == 2);
            Require(movements[0].ProposedDate == "2031-02-03");
            Require(movements[1].ProposedDate == "2031-02-10");
            Require(movements.All(item => item.SessionId != "review-1"));
        });
    }

    private static void PreviewValidatesStudyPlanWithoutApplying()
    {
        WithService((service, repository) =>
        {
            var current = new PlanPackage(
                "old-plan",
                1,
                "Plano antigo",
                "ENEM",
                "2031-12-31",
                new List<SessionItem>
                {
                    StoredSession("old-session", "old-plan", "2031-02-04", 60)
                });
            Require(repository.ApplyPlan(current).Success);
            var before = File.ReadAllBytes(repository.DataPath);
            var proposal = new AiProposal
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                CreatedAtUtc = CreatedAt,
                Summary = "Novo plano.",
                Kind = AiProposalKind.StudyPlan,
                StudyPlan = new AiStudyPlanDraft { StudyPlanJson = ValidStudyPlanJson() },
                Warnings = new List<string>()
            };

            var preview = service.Preview(proposal);

            Require(preview.CanProceed);
            Require(preview.AddedSessionCount == 1 && preview.RemovedSessionCount == 1);
            Require(repository.Settings.ActivePlanId == "old-plan");
            Require(File.ReadAllBytes(repository.DataPath).SequenceEqual(before));
        });
    }

    private static void PreviewBlocksUnsafeScope()
    {
        WithService((service, _) =>
        {
            var move = ChangesProposal(Operation(AiPlanOperationType.MoveSession, "Mover") with
            {
                SessionId = "session-1",
                DestinationDate = "2031-02-07"
            });
            var truncated = service.Preview(move, Context() with { HasMoreFutureSessions = true });
            Require(!truncated.CanProceed);
            Require(truncated.Message.Contains("200", StringComparison.Ordinal));

            var rebuild = service.Preview(
                ChangesProposal(Operation(AiPlanOperationType.RebuildFuturePlan, "Refazer tudo")),
                Context());
            Require(!rebuild.CanProceed);
            Require(rebuild.Message.Contains("StudyPlan", StringComparison.Ordinal));
        });
    }

    private static AiPlanningContext Context() => new()
    {
        SnapshotDate = "2031-02-03",
        ObjectiveName = "ENEM",
        ObjectiveDate = "2031-12-31",
        ActivePlanId = "plan-1",
        ActivePlanRevision = 1,
        ActivePlanTitle = "Plano atual",
        DailyMinutesLimit = 120,
        BlockMinutes = 60,
        FutureSessions = new List<AiPlanningSessionContext>
        {
            ContextSession("session-1", "2031-02-04", "Matemática", 60),
            ContextSession("session-2", "2031-02-05", "Física", 60),
            ContextSession("review-1", "2031-02-05", "História", 20, isProtected: true)
        }
    };

    private static AiPlanningSessionContext ContextSession(
        string id,
        string date,
        string subject,
        int minutes,
        bool isProtected = false) => new()
        {
            SessionId = id,
            PlanId = "plan-1",
            PlanRevision = 1,
            Date = date,
            Subject = subject,
            Topic = "Tópico",
            Minutes = minutes,
            Kind = isProtected ? "review" : "study",
            Origin = isProtected ? "runtime" : "plan",
            ProtectedFromDirectRemoval = isProtected
        };

    private static AiProposal ChangesProposal(params AiPlanOperation[] operations) => new()
    {
        Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        CreatedAtUtc = CreatedAt,
        Summary = "Alterações propostas.",
        Kind = AiProposalKind.PlanChanges,
        Changes = new AiPlanChangeDraft { Operations = operations.ToList() },
        Warnings = new List<string>()
    };

    private static AiPlanOperation Operation(AiPlanOperationType type, string summary) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Summary = summary
    };

    private static SessionItem StoredSession(
        string id,
        string planId,
        string date,
        int minutes) => new()
        {
            Id = id,
            PlanId = planId,
            PlanRevision = 1,
            Date = date,
            Subject = "Matemática",
            Topic = "Funções",
            Minutes = minutes,
            Target = "Resolver exercícios",
            Kind = "study",
            Status = "planned",
            Origin = "plan"
        };

    private static string ValidStudyPlanJson() => """
        {
          "format":"studyplan",
          "format_version":"0.2",
          "plan":{"id":"new-plan","revision":1,"title":"Plano novo"},
          "objective":{"name":"ENEM","date":"2031-12-31"},
          "sessions":[{
            "id":"new-session",
            "date":"2031-02-06",
            "subject":"Química",
            "topic":"Estequiometria",
            "minutes":60,
            "target":"Resolver 10 questões",
            "kind":"study"
          }]
        }
        """;

    private static void WithService(Action<AiProposalPreviewService, StudyRepository> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaProposalPreviewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new StudyRepository(Path.Combine(directory, "state.json"), () => Today);
            action(new AiProposalPreviewService(repository, new StudyPlanningContextProvider(repository, () => Today)), repository);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Proposal preview assertion failed.");
    }
}
