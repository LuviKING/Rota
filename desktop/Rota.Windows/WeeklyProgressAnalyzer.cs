using System.Globalization;

namespace Rota.Desktop;

public sealed record WeeklyProgressDay(
    DateOnly Date,
    int PlannedSessions,
    int CompletedSessions,
    int PlannedMinutes,
    int CompletedMinutes);

public sealed record WeeklyProgressSnapshot(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int PlannedSessions,
    int CompletedSessions,
    int PlannedMinutes,
    int CompletedMinutes,
    IReadOnlyList<WeeklyProgressDay> Days)
{
    public int RemainingSessions => PlannedSessions - CompletedSessions;
    public int RemainingMinutes => PlannedMinutes - CompletedMinutes;
    public int CompletionPercent => PlannedSessions == 0
        ? 0
        : (int)Math.Round(CompletedSessions * 100d / PlannedSessions, MidpointRounding.AwayFromZero);
}

public static class WeeklyProgressAnalyzer
{
    public static WeeklyProgressSnapshot Analyze(
        RepositoryApplicationSnapshot snapshot,
        DateOnly? containingDate = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!DateOnly.TryParseExact(
                snapshot.SnapshotDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var snapshotDate))
        {
            throw new InvalidDataException("A data do resumo semanal é inválida.");
        }

        var referenceDate = containingDate ?? snapshotDate;
        var daysSinceMonday = ((int)referenceDate.DayOfWeek + 6) % 7;
        var weekStart = referenceDate.AddDays(-daysSinceMonday);
        var weekEnd = weekStart.AddDays(6);
        var sessionsByDate = snapshot.Sessions
            .Where(session => string.CompareOrdinal(session.Date, weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0 &&
                              string.CompareOrdinal(session.Date, weekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) <= 0)
            .GroupBy(session => session.Date)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = weekStart.AddDays(offset);
            var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sessions = sessionsByDate.GetValueOrDefault(key) ?? new List<SessionItem>();
            var completed = sessions.Where(session => session.IsCompleted).ToList();
            return new WeeklyProgressDay(
                date,
                sessions.Count,
                completed.Count,
                sessions.Sum(session => session.Minutes),
                completed.Sum(session => session.Minutes));
        }).ToList();

        return new WeeklyProgressSnapshot(
            weekStart,
            weekEnd,
            days.Sum(day => day.PlannedSessions),
            days.Sum(day => day.CompletedSessions),
            days.Sum(day => day.PlannedMinutes),
            days.Sum(day => day.CompletedMinutes),
            days);
    }
}
