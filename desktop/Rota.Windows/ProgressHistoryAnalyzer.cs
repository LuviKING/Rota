using System.Globalization;

namespace Rota.Desktop;

public sealed record MonthlyProgressItem(
    DateOnly Month,
    int PlannedSessions,
    int CompletedSessions,
    int PlannedMinutes,
    int CompletedMinutes)
{
    public int CompletionPercent => PlannedSessions == 0
        ? 0
        : (int)Math.Round(CompletedSessions * 100d / PlannedSessions, MidpointRounding.AwayFromZero);
}

public sealed record WeeklyComparisonItem(
    DateOnly WeekStart,
    int PlannedSessions,
    int CompletedSessions,
    int PlannedMinutes,
    int CompletedMinutes,
    int CompletedMinutesChange)
{
    public DateOnly WeekEnd => WeekStart.AddDays(6);
    public int CompletionPercent => PlannedSessions == 0
        ? 0
        : (int)Math.Round(CompletedSessions * 100d / PlannedSessions, MidpointRounding.AwayFromZero);
}

public sealed record ProgressHistorySnapshot(
    int TotalCompletedSessions,
    int TotalCompletedMinutes,
    int ActiveMonths,
    IReadOnlyList<MonthlyProgressItem> Months,
    IReadOnlyList<WeeklyComparisonItem> Weeks);

public static class ProgressHistoryAnalyzer
{
    public const int MaximumMonthCount = 24;
    public const int MaximumWeekCount = 26;

    public static ProgressHistorySnapshot Analyze(
        RepositoryApplicationSnapshot snapshot,
        int monthCount = 12,
        int weekCount = 8)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (monthCount is < 1 or > MaximumMonthCount)
            throw new ArgumentOutOfRangeException(nameof(monthCount));
        if (weekCount is < 2 or > MaximumWeekCount)
            throw new ArgumentOutOfRangeException(nameof(weekCount));
        if (!DateOnly.TryParseExact(snapshot.SnapshotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var referenceDate))
            throw new InvalidDataException("A data do histórico de progresso é inválida.");

        var parsedSessions = snapshot.Sessions.Select(session =>
        {
            if (!DateOnly.TryParseExact(session.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                throw new InvalidDataException("Uma sessão do histórico possui data inválida.");
            return (Session: session, Date: date);
        }).ToList();

        var currentMonth = new DateOnly(referenceDate.Year, referenceDate.Month, 1);
        var months = Enumerable.Range(0, monthCount)
            .Select(offset => currentMonth.AddMonths(-offset))
            .Select(month =>
            {
                var sessions = parsedSessions.Where(item => item.Date.Year == month.Year && item.Date.Month == month.Month)
                    .Select(item => item.Session).ToList();
                var completed = sessions.Where(session => session.IsCompleted).ToList();
                return new MonthlyProgressItem(
                    month,
                    sessions.Count,
                    completed.Count,
                    sessions.Sum(session => session.Minutes),
                    completed.Sum(session => session.Minutes));
            }).ToList();

        var daysSinceMonday = ((int)referenceDate.DayOfWeek + 6) % 7;
        var currentWeekStart = referenceDate.AddDays(-daysSinceMonday);
        var rawWeeks = Enumerable.Range(0, weekCount + 1)
            .Select(offset => currentWeekStart.AddDays(-7 * offset))
            .Reverse()
            .Select(weekStart =>
            {
                var weekEnd = weekStart.AddDays(6);
                var sessions = parsedSessions.Where(item => item.Date >= weekStart && item.Date <= weekEnd)
                    .Select(item => item.Session).ToList();
                var completed = sessions.Where(session => session.IsCompleted).ToList();
                return new
                {
                    WeekStart = weekStart,
                    PlannedSessions = sessions.Count,
                    CompletedSessions = completed.Count,
                    PlannedMinutes = sessions.Sum(session => session.Minutes),
                    CompletedMinutes = completed.Sum(session => session.Minutes)
                };
            }).ToList();

        var weeks = rawWeeks.Skip(1).Select((week, index) => new WeeklyComparisonItem(
            week.WeekStart,
            week.PlannedSessions,
            week.CompletedSessions,
            week.PlannedMinutes,
            week.CompletedMinutes,
            week.CompletedMinutes - rawWeeks[index].CompletedMinutes)).Reverse().ToList();

        return new ProgressHistorySnapshot(
            parsedSessions.Count(item => item.Session.IsCompleted),
            parsedSessions.Where(item => item.Session.IsCompleted).Sum(item => item.Session.Minutes),
            months.Count(month => month.PlannedSessions > 0),
            months,
            weeks);
    }
}
