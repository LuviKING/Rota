using System.Globalization;

namespace Rota.Desktop.LocalAI;

public sealed class AiProposalPreviewService : IAiProposalPreviewService
{
    private readonly StudyRepository _repository;
    private readonly IAiPlanningContextProvider _contextProvider;

    public AiProposalPreviewService(
        StudyRepository repository,
        IAiPlanningContextProvider? contextProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _contextProvider = contextProvider ?? new StudyPlanningContextProvider(repository);
    }

    public AiProposalPreview Preview(AiProposal proposal, AiPlanningContext? planningContext = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        try
        {
            AiContractValidator.ValidateProposal(proposal);
            if (proposal.Status != AiProposalStatus.Pending)
                throw new PreviewBlockedException("Somente uma proposta pendente pode gerar prévia.");
            var preview = proposal.Kind switch
            {
                AiProposalKind.StudyPlan => PreviewStudyPlan(proposal),
                AiProposalKind.PlanChanges => PreviewChanges(proposal, planningContext ?? _contextProvider.Capture()),
                _ => throw new PreviewBlockedException("O tipo de proposta não pode ser visualizado.")
            };
            AiContractValidator.ValidatePreview(preview);
            return preview;
        }
        catch (Exception ex) when (ex is AiContractValidationException or ArgumentException or InvalidDataException or PreviewBlockedException)
        {
            return Blocked(proposal, ex.Message, planningContext);
        }
    }

    private AiProposalPreview PreviewStudyPlan(AiProposal proposal)
    {
        var package = StudyPlanImporter.Parse(proposal.StudyPlan!.StudyPlanJson);
        var evaluation = _repository.PreviewPlan(package);
        var context = _contextProvider.Capture();
        var currentSessions = _repository.UpcomingSessions(ParseIso(context.SnapshotDate), int.MaxValue);
        var beforeMinutes = currentSessions.Sum(session => session.Minutes);
        var futurePlanMinutes = package.Sessions
            .Where(session => string.CompareOrdinal(session.Date, context.SnapshotDate) >= 0)
            .Sum(session => session.Minutes);
        var protectedMinutes = currentSessions
            .Where(session => session.Origin == "runtime")
            .Sum(session => session.Minutes);
        return new AiProposalPreview
        {
            ProposalId = proposal.Id,
            Kind = proposal.Kind,
            State = evaluation.Success ? AiProposalPreviewState.Ready : AiProposalPreviewState.Blocked,
            Message = evaluation.Message,
            BeforeSessionCount = currentSessions.Count,
            AfterSessionCount = evaluation.Success
                ? currentSessions.Count - evaluation.Removed + evaluation.Applied
                : currentSessions.Count,
            BeforeMinutes = beforeMinutes,
            AfterMinutes = evaluation.Success ? protectedMinutes + futurePlanMinutes : beforeMinutes,
            AddedSessionCount = evaluation.Success ? evaluation.Applied : 0,
            RemovedSessionCount = evaluation.Success ? evaluation.Removed : 0,
            Warnings = CopyWarnings(proposal.Warnings, context.HasMoreFutureSessions)
        };
    }

    private static AiProposalPreview PreviewChanges(AiProposal proposal, AiPlanningContext context)
    {
        AiContractValidator.ValidatePlanningContext(context);
        var beforeCount = context.FutureSessions.Count;
        var beforeMinutes = context.FutureSessions.Sum(session => session.Minutes);
        if (context.HasMoreFutureSessions)
            throw new PreviewBlockedException("A agenda possui mais de 200 sessões futuras; a prévia foi bloqueada para não ignorar conflitos ocultos.");

        var sessions = context.FutureSessions.Select(MutableSession.From).ToList();
        var previewOperations = new List<AiPreviewOperation>();
        var warnings = CopyWarnings(proposal.Warnings, hasMore: false);
        var moved = new HashSet<(string PlanId, string SessionId)>();
        var removed = new HashSet<(string PlanId, string SessionId)>();
        var added = 0;
        var dailyLimit = context.DailyMinutesLimit;
        HashSet<DayOfWeek>? availableDays = null;

        foreach (var operation in proposal.Changes!.Operations)
        {
            switch (operation.Type)
            {
                case AiPlanOperationType.MoveSession:
                    Move(operation, sessions, context, previewOperations, moved);
                    break;
                case AiPlanOperationType.AddSession:
                    Add(operation, sessions, context, previewOperations);
                    added++;
                    break;
                case AiPlanOperationType.RemoveFutureSession:
                    Remove(operation, sessions, previewOperations, removed);
                    break;
                case AiPlanOperationType.ChangeSubjectPriority:
                    PreviewPriority(operation, sessions, previewOperations, warnings);
                    break;
                case AiPlanOperationType.SetAvailability:
                    (dailyLimit, availableDays) = Availability(operation, previewOperations);
                    break;
                case AiPlanOperationType.RedistributeLoad:
                    (dailyLimit, availableDays) = Availability(operation, previewOperations);
                    Redistribute(operation, sessions, context, dailyLimit, availableDays, previewOperations, moved);
                    break;
                case AiPlanOperationType.RebuildFuturePlan:
                    throw new PreviewBlockedException(
                        "Reconstruir o plano exige uma proposta StudyPlan detalhada; a operação genérica não pode ser aplicada com segurança.");
                default:
                    throw new PreviewBlockedException("A proposta contém uma operação não suportada.");
            }
        }

        ValidateFinalSchedule(sessions, context, dailyLimit, availableDays);
        return new AiProposalPreview
        {
            ProposalId = proposal.Id,
            Kind = proposal.Kind,
            State = AiProposalPreviewState.Ready,
            Message = $"Prévia pronta: {added} adições, {removed.Count} remoções e {moved.Count} mudanças de data.",
            BeforeSessionCount = beforeCount,
            AfterSessionCount = sessions.Count,
            BeforeMinutes = beforeMinutes,
            AfterMinutes = sessions.Sum(session => session.Minutes),
            AddedSessionCount = added,
            RemovedSessionCount = removed.Count,
            MovedSessionCount = moved.Count,
            Operations = previewOperations,
            Warnings = warnings
        };
    }

    private static void Move(
        AiPlanOperation operation,
        List<MutableSession> sessions,
        AiPlanningContext context,
        List<AiPreviewOperation> previews,
        HashSet<(string PlanId, string SessionId)> moved)
    {
        var session = ResolveSession(operation, sessions);
        EnsureEditable(session);
        var destination = ParseFutureDate(operation.DestinationDate, context);
        var original = session.Date;
        session.Date = destination;
        if (destination != original) moved.Add(session.Identity);
        previews.Add(Item(operation, session.SessionId, original, destination));
    }

    private static void Add(
        AiPlanOperation operation,
        List<MutableSession> sessions,
        AiPlanningContext context,
        List<AiPreviewOperation> previews)
    {
        if (context.ActivePlanId.Length == 0)
            throw new PreviewBlockedException("Não há plano ativo para receber uma nova sessão.");
        if (string.IsNullOrWhiteSpace(operation.Subject) || operation.Subject.Length > 80 ||
            operation.Minutes is not int minutes || minutes is < 10 or > 360)
            throw new PreviewBlockedException("Adicionar sessão exige matéria, data e duração.");
        var destination = ParseFutureDate(operation.DestinationDate, context);
        var sessionId = $"preview-{operation.Id:N}";
        sessions.Add(new MutableSession(
            context.ActivePlanId,
            sessionId,
            destination,
            operation.Subject,
            minutes,
            false));
        previews.Add(Item(operation, sessionId, "", destination));
    }

    private static void Remove(
        AiPlanOperation operation,
        List<MutableSession> sessions,
        List<AiPreviewOperation> previews,
        HashSet<(string PlanId, string SessionId)> removed)
    {
        var session = ResolveSession(operation, sessions);
        EnsureEditable(session);
        sessions.Remove(session);
        removed.Add(session.Identity);
        previews.Add(Item(operation, session.SessionId, session.Date, ""));
    }

    private static void PreviewPriority(
        AiPlanOperation operation,
        List<MutableSession> sessions,
        List<AiPreviewOperation> previews,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(operation.Subject) || operation.Priority is null)
            throw new PreviewBlockedException("Alterar prioridade exige matéria e prioridade.");
        if (!sessions.Any(session => string.Equals(session.Subject, operation.Subject, StringComparison.OrdinalIgnoreCase)))
            throw new PreviewBlockedException("A matéria indicada para prioridade não existe na agenda futura.");
        previews.Add(Item(operation));
        warnings.Add("A prioridade será usada somente numa futura redistribuição; esta prévia não muda datas por conta própria.");
    }

    private static (int DailyLimit, HashSet<DayOfWeek> AvailableDays) Availability(
        AiPlanOperation operation,
        List<AiPreviewOperation> previews)
    {
        if (operation.MaxHoursPerDay is null || operation.AvailableDays.Count == 0)
            throw new PreviewBlockedException("Alterar disponibilidade exige limite diário e ao menos um dia disponível.");
        var dailyLimit = checked((int)Math.Round(
            operation.MaxHoursPerDay.Value * 60,
            MidpointRounding.AwayFromZero));
        if (dailyLimit is < 60 or > 720)
            throw new PreviewBlockedException("O limite diário proposto é inválido.");
        previews.Add(Item(operation));
        return (dailyLimit, operation.AvailableDays.ToHashSet());
    }

    private static void Redistribute(
        AiPlanOperation operation,
        List<MutableSession> sessions,
        AiPlanningContext context,
        int dailyLimit,
        HashSet<DayOfWeek> availableDays,
        List<AiPreviewOperation> previews,
        HashSet<(string PlanId, string SessionId)> moved)
    {
        var protectedLoad = sessions
            .Where(session => session.IsProtected)
            .GroupBy(session => session.Date)
            .ToDictionary(group => group.Key, group => group.Sum(session => session.Minutes), StringComparer.Ordinal);
        var movable = sessions
            .Where(session => !session.IsProtected)
            .OrderBy(session => session.Date, StringComparer.Ordinal)
            .ThenBy(session => session.PlanId, StringComparer.Ordinal)
            .ThenBy(session => session.SessionId, StringComparer.Ordinal)
            .ToList();
        var assignedLoad = new Dictionary<string, int>(protectedLoad, StringComparer.Ordinal);
        var cursor = ParseIso(context.SnapshotDate);
        var latest = sessions.Select(session => ParseIso(session.Date)).DefaultIfEmpty(cursor).Max().AddDays(366);
        if (context.ObjectiveDate.Length > 0)
            latest = ParseIso(context.ObjectiveDate);

        foreach (var session in movable)
        {
            if (session.Minutes > dailyLimit)
                throw new PreviewBlockedException("Uma sessão é maior que o novo limite diário e não pode ser redistribuída.");
            var original = session.Date;
            var date = cursor;
            while (date <= latest)
            {
                var iso = StudyRepository.Iso(date);
                assignedLoad.TryGetValue(iso, out var used);
                if (availableDays.Contains(date.DayOfWeek) && used + session.Minutes <= dailyLimit)
                {
                    session.Date = iso;
                    assignedLoad[iso] = used + session.Minutes;
                    if (original != iso)
                    {
                        moved.Add(session.Identity);
                        previews.Add(new AiPreviewOperation
                        {
                            OperationId = operation.Id,
                            Type = AiPlanOperationType.MoveSession,
                            Summary = "Movimento calculado pela redistribuição.",
                            SessionId = session.SessionId,
                            OriginalDate = original,
                            ProposedDate = iso
                        });
                    }
                    cursor = date;
                    break;
                }
                date = date.AddDays(1);
            }
            if (date > latest)
                throw new PreviewBlockedException("Não há espaço suficiente antes da data do objetivo para redistribuir todas as sessões.");
        }
    }

    private static void ValidateFinalSchedule(
        List<MutableSession> sessions,
        AiPlanningContext context,
        int dailyLimit,
        HashSet<DayOfWeek>? availableDays)
    {
        foreach (var group in sessions.GroupBy(session => session.Date))
        {
            if (group.Sum(session => session.Minutes) > dailyLimit)
                throw new PreviewBlockedException($"A carga diária excede o limite em {group.Key}.");
        }
        if (availableDays is not null)
        {
            var conflict = sessions.FirstOrDefault(session =>
                !session.IsProtected && !availableDays.Contains(ParseIso(session.Date).DayOfWeek));
            if (conflict is not null)
                throw new PreviewBlockedException("A agenda ainda contém sessões comuns fora dos dias disponíveis.");
        }
        if (context.ObjectiveDate.Length > 0 &&
            sessions.Any(session => string.CompareOrdinal(session.Date, context.ObjectiveDate) > 0))
        {
            throw new PreviewBlockedException("A agenda proposta ultrapassa a data do objetivo.");
        }
    }

    private static MutableSession ResolveSession(AiPlanOperation operation, List<MutableSession> sessions)
    {
        if (string.IsNullOrWhiteSpace(operation.SessionId))
            throw new PreviewBlockedException("A operação exige o ID da sessão.");
        var matches = sessions.Where(session => session.SessionId == operation.SessionId).Take(2).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new PreviewBlockedException("A sessão indicada não existe na agenda futura."),
            _ => throw new PreviewBlockedException("O ID indicado é ambíguo entre planos e não pode ser alterado.")
        };
    }

    private static void EnsureEditable(MutableSession session)
    {
        if (session.IsProtected)
            throw new PreviewBlockedException("Uma revisão automática protegida não pode ser movida ou removida diretamente.");
    }

    private static string ParseFutureDate(string value, AiPlanningContext context)
    {
        var date = ParseIso(value);
        if (string.CompareOrdinal(value, context.SnapshotDate) < 0)
            throw new PreviewBlockedException("A proposta tenta mover uma sessão para o passado.");
        if (context.ObjectiveDate.Length > 0 && string.CompareOrdinal(value, context.ObjectiveDate) > 0)
            throw new PreviewBlockedException("A proposta tenta agendar depois da data do objetivo.");
        return StudyRepository.Iso(date);
    }

    private static DateOnly ParseIso(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new PreviewBlockedException("A proposta contém uma data inválida.");
        return date;
    }

    private static AiPreviewOperation Item(
        AiPlanOperation operation,
        string sessionId = "",
        string originalDate = "",
        string proposedDate = "") => new()
        {
            OperationId = operation.Id,
            Type = operation.Type,
            Summary = operation.Summary,
            SessionId = sessionId,
            OriginalDate = originalDate,
            ProposedDate = proposedDate
        };

    private static List<string> CopyWarnings(IEnumerable<string> warnings, bool hasMore)
    {
        var result = warnings.ToList();
        if (hasMore) result.Add("A fotografia do plano foi limitada às primeiras 200 sessões futuras.");
        return result;
    }

    private static AiProposalPreview Blocked(
        AiProposal proposal,
        string message,
        AiPlanningContext? context)
    {
        var sessions = context?.FutureSessions ?? new List<AiPlanningSessionContext>();
        return new AiProposalPreview
        {
            ProposalId = proposal.Id,
            Kind = proposal.Kind,
            State = AiProposalPreviewState.Blocked,
            Message = message,
            BeforeSessionCount = sessions.Count,
            AfterSessionCount = sessions.Count,
            BeforeMinutes = sessions.Sum(session => session.Minutes),
            AfterMinutes = sessions.Sum(session => session.Minutes),
            Warnings = proposal.Warnings?.ToList() ?? new List<string>()
        };
    }

    private sealed class PreviewBlockedException : Exception
    {
        public PreviewBlockedException(string message) : base(message)
        {
        }
    }

    private sealed class MutableSession
    {
        public MutableSession(
            string planId,
            string sessionId,
            string date,
            string subject,
            int minutes,
            bool isProtected)
        {
            PlanId = planId;
            SessionId = sessionId;
            Date = date;
            Subject = subject;
            Minutes = minutes;
            IsProtected = isProtected;
        }

        public string PlanId { get; }
        public string SessionId { get; }
        public string Date { get; set; }
        public string Subject { get; }
        public int Minutes { get; }
        public bool IsProtected { get; }
        public (string PlanId, string SessionId) Identity => (PlanId, SessionId);

        public static MutableSession From(AiPlanningSessionContext session) => new(
            session.PlanId,
            session.SessionId,
            session.Date,
            session.Subject,
            session.Minutes,
            session.ProtectedFromDirectRemoval);
    }
}
