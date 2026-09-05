namespace Rota.Desktop.LocalAI;

public sealed class StudyPlanningContextProvider : IAiPlanningContextProvider
{
    public const int MaximumFutureSessions = 200;

    private readonly StudyRepository _repository;
    private readonly Func<DateTime> _now;

    public StudyPlanningContextProvider(StudyRepository repository, Func<DateTime>? now = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _now = now ?? (() => DateTime.Now);
    }

    public AiPlanningContext Capture()
    {
        var requestedDate = StudyRepository.Iso(DateOnly.FromDateTime(_now()));
        var snapshot = _repository.CaptureApplicationSnapshot();
        if (!string.Equals(snapshot.SnapshotDate, requestedDate, StringComparison.Ordinal))
            snapshot = snapshot with { SnapshotDate = requestedDate };
        return FromSnapshot(snapshot);
    }

    public static AiPlanningContext FromSnapshot(RepositoryApplicationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = snapshot.Settings;
        var candidates = snapshot.Sessions
            .Where(session => !session.IsCompleted && string.CompareOrdinal(session.Date, snapshot.SnapshotDate) >= 0)
            .OrderBy(session => session.Date, StringComparer.Ordinal)
            .ThenBy(session => session.Kind == "review" ? 0 : session.Kind == "study" ? 1 : 2)
            .ThenBy(session => session.Subject, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumFutureSessions + 1)
            .ToList();
        var sessions = candidates
            .Take(MaximumFutureSessions)
            .Select(session => new AiPlanningSessionContext
            {
                SessionId = session.Id,
                PlanId = session.PlanId,
                PlanRevision = session.PlanRevision,
                Date = session.Date,
                Subject = session.Subject,
                Topic = session.Topic,
                Minutes = session.Minutes,
                Kind = session.Kind,
                Origin = session.Origin,
                ProtectedFromDirectRemoval = string.Equals(session.Origin, "runtime", StringComparison.Ordinal)
            })
            .ToList();

        var context = new AiPlanningContext
        {
            SnapshotDate = snapshot.SnapshotDate,
            ObjectiveName = settings.ObjectiveName,
            ObjectiveDate = settings.ObjectiveDate,
            ActivePlanId = settings.ActivePlanId,
            ActivePlanRevision = settings.ActivePlanRevision,
            ActivePlanTitle = settings.ActivePlanTitle,
            DailyMinutesLimit = StudyRepository.DailyMinutesLimit(settings),
            BlockMinutes = settings.BlockMinutes,
            AvailableDays = settings.AvailableStudyDays.ToList(),
            SubjectPriorities = new Dictionary<string, int>(settings.SubjectPriorities, StringComparer.OrdinalIgnoreCase),
            FutureSessions = sessions,
            HasMoreFutureSessions = candidates.Count > MaximumFutureSessions
        };
        AiContractValidator.ValidatePlanningContext(context);
        return context;
    }
}
