using System.Text.Json.Serialization;

namespace Rota.Desktop;

public sealed class SessionItem
{
    public string Id { get; set; } = "";
    public string PlanId { get; set; } = "";
    public int PlanRevision { get; set; }
    public string Date { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public int Minutes { get; set; }
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "study";
    public string ReviewLabel { get; set; } = "";
    public string Status { get; set; } = "planned";
    public string Origin { get; set; } = "plan";
    public long CompletedAtUnixMs { get; set; }

    [JsonIgnore]
    public bool IsCompleted => string.Equals(Status, "completed", StringComparison.Ordinal);

    public SessionItem Copy() => new()
    {
        Id = Id,
        PlanId = PlanId,
        PlanRevision = PlanRevision,
        Date = Date,
        Subject = Subject,
        Topic = Topic,
        Minutes = Minutes,
        Target = Target,
        Kind = Kind,
        ReviewLabel = ReviewLabel,
        Status = Status,
        Origin = Origin,
        CompletedAtUnixMs = CompletedAtUnixMs
    };
}

public sealed record PlanPackage(
    string PlanId,
    int Revision,
    string Title,
    string ObjectiveName,
    string ObjectiveDate,
    IReadOnlyList<SessionItem> Sessions);

public sealed record ApplyResult(
    bool Success,
    int Applied,
    int Removed,
    string Message,
    string OverloadedDate = "");

public sealed class AppSettings
{
    public string ObjectiveName { get; set; } = "Meu objetivo";
    public string ObjectiveDate { get; set; } = "";
    public double DailyHours { get; set; } = 5;
    public int BlockMinutes { get; set; } = 60;
    public bool ReviewD1 { get; set; } = true;
    public bool ReviewD3 { get; set; } = true;
    public bool ReviewD7 { get; set; } = true;
    public bool ReminderEnabled { get; set; }
    public string ReminderTime { get; set; } = StudyReminderConfiguration.DefaultTime;
    public List<DayOfWeek> AvailableStudyDays { get; set; } = Enum.GetValues<DayOfWeek>().ToList();
    public Dictionary<string, int> SubjectPriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActivePlanId { get; set; } = "";
    public int ActivePlanRevision { get; set; }
    public string ActivePlanTitle { get; set; } = "";
    public Dictionary<string, int> PlanRevisions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AppState
{
    public int StateVersion { get; set; } = 1;
    public long MutationVersion { get; set; }
    public int CompletedOnboardingStep { get; set; }
    public AppSettings Settings { get; set; } = new();
    public List<SessionItem> Sessions { get; set; } = new();
    public List<CalendarApplicationReceipt> AiApplications { get; set; } = new();
    public CalendarUndoCheckpoint? AiUndoCheckpoint { get; set; }
    public SessionMoveUndoCheckpoint? SessionMoveUndoCheckpoint { get; set; }
}

public static class OnboardingSteps
{
    public const int Welcome = 1;
    public const int Routine = 2;
    public const int FirstPlan = 3;
    public const int Last = FirstPlan;
}

public sealed class CalendarApplicationReceipt
{
    public string ProposalId { get; set; } = "";
    public string ProposalHash { get; set; } = "";
    public string Status { get; set; } = "applied";
    public long AppliedMutationVersion { get; set; }
    public long AppliedAtUnixMs { get; set; }
    public long UndoneMutationVersion { get; set; }
    public long UndoneAtUnixMs { get; set; }

    public CalendarApplicationReceipt Copy() => new()
    {
        ProposalId = ProposalId,
        ProposalHash = ProposalHash,
        Status = Status,
        AppliedMutationVersion = AppliedMutationVersion,
        AppliedAtUnixMs = AppliedAtUnixMs,
        UndoneMutationVersion = UndoneMutationVersion,
        UndoneAtUnixMs = UndoneAtUnixMs
    };
}

public sealed class CalendarUndoCheckpoint
{
    public string ProposalId { get; set; } = "";
    public long ExpectedMutationVersion { get; set; }
    public AppSettings Settings { get; set; } = new();
    public List<SessionItem> Sessions { get; set; } = new();

    public CalendarUndoCheckpoint Copy() => new()
    {
        ProposalId = ProposalId,
        ExpectedMutationVersion = ExpectedMutationVersion,
        Settings = StudyRepository.CopySettings(Settings),
        Sessions = Sessions.Select(session => session.Copy()).ToList()
    };
}

public sealed record RepositoryApplicationSnapshot(
    long MutationVersion,
    string SnapshotDate,
    AppSettings Settings,
    IReadOnlyList<SessionItem> Sessions);

public sealed record CalendarApplicationState(
    bool Exists,
    bool IsApplied,
    bool IsUndone,
    bool CanUndo,
    string Message);

public sealed record CalendarMutationResult(
    bool Success,
    bool AlreadyHandled,
    string Message,
    long MutationVersion = 0);

public sealed record SessionMoveResult(
    bool Success,
    bool AlreadyHandled,
    string Message,
    string SourceDate = "",
    string TargetDate = "",
    long MutationVersion = 0);

public sealed class SessionMoveUndoCheckpoint
{
    public string PlanId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceDate { get; set; } = "";
    public string TargetDate { get; set; } = "";
    public long ExpectedMutationVersion { get; set; }

    public SessionMoveUndoCheckpoint Copy() => new()
    {
        PlanId = PlanId,
        SessionId = SessionId,
        SourceDate = SourceDate,
        TargetDate = TargetDate,
        ExpectedMutationVersion = ExpectedMutationVersion
    };
}

public sealed record SessionProgress(int Completed, int Total, int Minutes)
{
    public double Fraction => Total == 0 ? 0 : (double)Completed / Total;
}

public sealed record WeekDaySummary(DateOnly Date, int Completed, int Total, int Minutes);

