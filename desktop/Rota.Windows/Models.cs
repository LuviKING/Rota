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
    public int DailyHours { get; set; } = 5;
    public int BlockMinutes { get; set; } = 60;
    public bool ReviewD1 { get; set; } = true;
    public bool ReviewD3 { get; set; } = true;
    public bool ReviewD7 { get; set; } = true;
    public string ActivePlanId { get; set; } = "";
    public int ActivePlanRevision { get; set; }
    public string ActivePlanTitle { get; set; } = "";
    public Dictionary<string, int> PlanRevisions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AppState
{
    public int StateVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<SessionItem> Sessions { get; set; } = new();
}

public sealed record SessionProgress(int Completed, int Total, int Minutes)
{
    public double Fraction => Total == 0 ? 0 : (double)Completed / Total;
}

public sealed record WeekDaySummary(DateOnly Date, int Completed, int Total, int Minutes);

