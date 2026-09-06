namespace Rota.Desktop;

public sealed record SubjectProgressItem(
    string Subject,
    int PlannedSessions,
    int CompletedSessions,
    int PlannedMinutes,
    int CompletedMinutes,
    int CompletedStudyDays)
{
    public int RemainingSessions => PlannedSessions - CompletedSessions;
    public int RemainingMinutes => PlannedMinutes - CompletedMinutes;
    public int CompletionPercent => PlannedSessions == 0
        ? 0
        : (int)Math.Round(CompletedSessions * 100d / PlannedSessions, MidpointRounding.AwayFromZero);
}

public sealed record SubjectProgressSnapshot(
    int TotalSubjects,
    int PlannedSessions,
    int CompletedSessions,
    int PlannedMinutes,
    int CompletedMinutes,
    IReadOnlyList<SubjectProgressItem> Subjects)
{
    public int HiddenSubjectCount => TotalSubjects - Subjects.Count;
    public int CompletionPercent => PlannedSessions == 0
        ? 0
        : (int)Math.Round(CompletedSessions * 100d / PlannedSessions, MidpointRounding.AwayFromZero);
}

public static class SubjectProgressAnalyzer
{
    public const int DefaultItemLimit = 100;
    public const int MaximumItemLimit = 500;

    public static SubjectProgressSnapshot Analyze(
        RepositoryApplicationSnapshot snapshot,
        int itemLimit = DefaultItemLimit)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (itemLimit is < 1 or > MaximumItemLimit)
            throw new ArgumentOutOfRangeException(nameof(itemLimit));

        var subjects = snapshot.Sessions
            .GroupBy(session => session.Subject, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var sessions = group.ToList();
                var completed = sessions.Where(session => session.IsCompleted).ToList();
                return new SubjectProgressItem(
                    sessions[0].Subject,
                    sessions.Count,
                    completed.Count,
                    sessions.Sum(session => session.Minutes),
                    completed.Sum(session => session.Minutes),
                    completed.Select(session => session.Date).Distinct(StringComparer.Ordinal).Count());
            })
            .OrderByDescending(subject => subject.PlannedMinutes)
            .ThenBy(subject => subject.Subject, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new SubjectProgressSnapshot(
            subjects.Count,
            subjects.Sum(subject => subject.PlannedSessions),
            subjects.Sum(subject => subject.CompletedSessions),
            subjects.Sum(subject => subject.PlannedMinutes),
            subjects.Sum(subject => subject.CompletedMinutes),
            subjects.Take(itemLimit).ToList());
    }
}
