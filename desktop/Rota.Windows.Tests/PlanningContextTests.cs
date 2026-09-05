using Rota.Desktop.LocalAI;
using Rota.Desktop.Tests.Fakes;
using System.Text.Json;

namespace Rota.Desktop.Tests;

public static class PlanningContextTests
{
    private static readonly DateTime Today = new(2031, 2, 3, 9, 0, 0, DateTimeKind.Local);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI planning context is bounded, private, future-only and read-only", ContextIsBoundedPrivateAndReadOnly),
        ("AI planning context rejects stale or inconsistent sessions", ContextRejectsInvalidSessions),
        ("AI planning service scopes context and applies configured plan defaults", ServiceScopesContextToChanges)
    };

    private static void ContextIsBoundedPrivateAndReadOnly()
    {
        WithRepository(repository =>
        {
            repository.SavePreferences("ENEM", "2032-11-07", 12, 60, false, false, false);
            var sessions = Enumerable.Range(0, 202)
                .Select(index => Session(index, DateOnly.FromDateTime(Today).AddDays(index)))
                .ToList();
            var applied = repository.ApplyPlan(new PlanPackage(
                "plan-context", 3, "Plano limitado", "ENEM", "2032-11-07", sessions));
            Require(applied.Success);
            Require(repository.MarkCompleted("plan-context", "session-000"));
            var before = File.ReadAllBytes(repository.DataPath);

            var context = new StudyPlanningContextProvider(repository, () => Today).Capture();

            Require(context.SnapshotDate == "2031-02-03");
            Require(context.ActivePlanId == "plan-context" && context.ActivePlanRevision == 3);
            Require(context.DailyMinutesLimit == 720 && context.BlockMinutes == 60);
            Require(context.FutureSessions.Count == StudyPlanningContextProvider.MaximumFutureSessions);
            Require(context.HasMoreFutureSessions);
            Require(context.FutureSessions.All(session => session.SessionId != "session-000"));
            Require(context.FutureSessions.All(session => string.CompareOrdinal(session.Date, context.SnapshotDate) >= 0));
            Require(context.FutureSessions[0].SessionId == "session-001");
            var json = JsonSerializer.Serialize(context);
            Require(!json.Contains("private target", StringComparison.Ordinal));
            Require(!json.Contains("CompletedAt", StringComparison.Ordinal));
            Require(File.ReadAllBytes(repository.DataPath).SequenceEqual(before));
        });
    }

    private static void ContextRejectsInvalidSessions()
    {
        var valid = new AiPlanningContext
        {
            SnapshotDate = "2031-02-03",
            ObjectiveName = "ENEM",
            DailyMinutesLimit = 180,
            BlockMinutes = 60,
            FutureSessions = new List<AiPlanningSessionContext>
            {
                new()
                {
                    SessionId = "session-1",
                    PlanId = "plan-1",
                    PlanRevision = 1,
                    Date = "2031-02-04",
                    Subject = "Matemática",
                    Topic = "Funções",
                    Minutes = 60,
                    Kind = "study",
                    Origin = "plan"
                }
            }
        };
        AiContractValidator.ValidatePlanningContext(valid);
        Expect<AiContractValidationException>(() => AiContractValidator.ValidatePlanningContext(valid with
        {
            FutureSessions = new List<AiPlanningSessionContext>
            {
                valid.FutureSessions[0] with { Date = "2031-02-02" }
            }
        }));
        Expect<AiContractValidationException>(() => AiContractValidator.ValidatePlanningContext(valid with
        {
            FutureSessions = new List<AiPlanningSessionContext>
            {
                valid.FutureSessions[0] with { Origin = "runtime", ProtectedFromDirectRemoval = false }
            }
        }));
    }

    private static void ServiceScopesContextToChanges()
    {
        WithRepository(repository =>
        {
            repository.SavePreferences("Objetivo", "2031-12-31", 2.5, 75, true, false, true);
            var applied = repository.ApplyPlan(new PlanPackage(
                "active-plan", 1, "Plano atual", "Objetivo", "2031-12-31",
                new List<SessionItem> { Session(1, new DateOnly(2031, 2, 4), "active-plan", 1) }));
            Require(applied.Success);
            var directory = Path.Combine(Path.GetTempPath(), "RotaPlanningContextStore", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var store = new AiConfigurationStore(Path.Combine(directory, "config.json"));
                var backend = new FakeLocalAiBackend();
                var provider = new StudyPlanningContextProvider(repository, () => Today);
                var service = new AiPlanningService(backend, store, provider);

                var changes = service.CreateGenerationAsync(
                    ValidInput(), AiProposalKind.PlanChanges).GetAwaiter().GetResult();
                Require(backend.LastPlanningContext?.ActivePlanId == "active-plan");
                Require(ReferenceEquals(changes.PlanningContext, backend.LastPlanningContext));
                var newPlan = service.CreateGenerationAsync(
                    ValidInput(), AiProposalKind.StudyPlan).GetAwaiter().GetResult();
                Require(backend.LastPlanningContext is null);
                Require(newPlan.PlanningContext is null);
                Require(backend.LastInput?.AvailableHoursPerDay == 2.5);
                Require(backend.LastInput?.AvailableDays.SequenceEqual(Enum.GetValues<DayOfWeek>()) == true);
                Require(backend.LastInput?.Notes.Contains("75 minutos", StringComparison.Ordinal) == true);

                var explicitAvailability = ValidInput() with
                {
                    AvailableHoursPerDay = 1,
                    AvailableDays = new List<DayOfWeek> { DayOfWeek.Sunday }
                };
                service.CreateGenerationAsync(
                    explicitAvailability, AiProposalKind.StudyPlan).GetAwaiter().GetResult();
                Require(backend.LastInput?.AvailableHoursPerDay == 1);
                Require(backend.LastInput?.AvailableDays.SequenceEqual(new[] { DayOfWeek.Sunday }) == true);
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        });
    }

    private static SessionItem Session(
        int index,
        DateOnly date,
        string planId = "plan-context",
        int planRevision = 3) => new()
        {
            Id = $"session-{index:000}",
            PlanId = planId,
            PlanRevision = planRevision,
            Date = StudyRepository.Iso(date),
            Subject = "Matemática",
            Topic = $"Tópico {index}",
            Minutes = 10,
            Target = "private target",
            Kind = "study",
            Status = "planned",
            Origin = "plan"
        };

    private static AiAssistantInput ValidInput() => new()
    {
        ObjectiveOrExam = "ENEM",
        FreeText = "Redistribua as sessões futuras."
    };

    private static void WithRepository(Action<StudyRepository> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaPlanningContextTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(new StudyRepository(Path.Combine(directory, "state.json"), () => Today));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Planning context assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
