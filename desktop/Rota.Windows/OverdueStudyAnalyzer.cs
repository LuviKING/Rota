using System.Globalization;

namespace Rota.Desktop;

public sealed record OverdueStudyItem(
    string PlanId,
    string SessionId,
    DateOnly PlannedDate,
    string Subject,
    string Topic,
    int Minutes,
    string Kind,
    bool IsRuntimeProtected);

public sealed record OverdueStudySnapshot(
    DateOnly SnapshotDate,
    int TotalCount,
    int TotalMinutes,
    DateOnly? OldestDate,
    int MostDelayedDays,
    int RuntimeProtectedCount,
    IReadOnlyList<OverdueStudyItem> Items)
{
    public bool HasOverdue => TotalCount > 0;
}

public static class OverdueStudyAnalyzer
{
    public const int DefaultItemLimit = 200;
    public const int MaximumItemLimit = 500;

    public static OverdueStudySnapshot Analyze(
        RepositoryApplicationSnapshot snapshot,
        int itemLimit = DefaultItemLimit)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (itemLimit is < 1 or > MaximumItemLimit)
            throw new ArgumentOutOfRangeException(nameof(itemLimit));
        if (!DateOnly.TryParseExact(
                snapshot.SnapshotDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var today))
            throw new InvalidDataException("A data da análise de atrasos é inválida.");

        var overdue = snapshot.Sessions
            .Where(session => !session.IsCompleted &&
                              string.CompareOrdinal(session.Date, snapshot.SnapshotDate) < 0)
            .Select(session => ToItem(session))
            .OrderBy(item => item.PlannedDate)
            .ThenBy(item => KindOrder(item.Kind))
            .ThenBy(item => item.Subject, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.PlanId, StringComparer.Ordinal)
            .ThenBy(item => item.SessionId, StringComparer.Ordinal)
            .ToList();

        var oldest = overdue.Count == 0 ? (DateOnly?)null : overdue[0].PlannedDate;
        return new OverdueStudySnapshot(
            today,
            overdue.Count,
            overdue.Sum(item => item.Minutes),
            oldest,
            oldest is null ? 0 : today.DayNumber - oldest.Value.DayNumber,
            overdue.Count(item => item.IsRuntimeProtected),
            overdue.Take(itemLimit).ToList());
    }

    private static OverdueStudyItem ToItem(SessionItem session)
    {
        if (!DateOnly.TryParseExact(
                session.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            throw new InvalidDataException("Uma sessão possui data inválida na análise de atrasos.");

        return new OverdueStudyItem(
            session.PlanId,
            session.Id,
            date,
            session.Subject,
            session.Topic,
            session.Minutes,
            session.Kind,
            string.Equals(session.Origin, "runtime", StringComparison.Ordinal));
    }

    private static int KindOrder(string kind) => kind switch
    {
        "assessment" => 0,
        "review" => 1,
        _ => 2
    };
}
