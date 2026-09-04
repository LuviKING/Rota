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
        var snapshotDate = DateOnly.FromDateTime(_now());
        var settings = _repository.Settings;
        var candidates = _repository.UpcomingSessions(snapshotDate, MaximumFutureSessions + 1);
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
            SnapshotDate = StudyRepository.Iso(snapshotDate),
            ObjectiveName = settings.ObjectiveName,
            ObjectiveDate = settings.ObjectiveDate,
            ActivePlanId = settings.ActivePlanId,
            ActivePlanRevision = settings.ActivePlanRevision,
            ActivePlanTitle = settings.ActivePlanTitle,
            DailyMinutesLimit = checked(settings.DailyHours * 60),
            BlockMinutes = settings.BlockMinutes,
            FutureSessions = sessions,
            HasMoreFutureSessions = candidates.Count > MaximumFutureSessions
        };
        AiContractValidator.ValidatePlanningContext(context);
        return context;
    }
}
