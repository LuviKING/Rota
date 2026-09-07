using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop;

public sealed class StudyRepository
{
    private const int CurrentStateVersion = 1;
    private const long MaxStateFileBytes = 8 * 1024 * 1024;
    private const int MaxStoredSessions = 20_000;
    private const int MaxAiApplicationReceipts = 100;
    private readonly object _gate = new();
    private readonly string _dataPath;
    private readonly Func<DateTime> _now;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private AppState _state;

    public string DataPath => _dataPath;
    public string DataDirectory => Path.GetDirectoryName(_dataPath)!;
    public string LastLoadWarning { get; private set; } = "";

    public StudyRepository(string? dataPath = null, Func<DateTime>? nowProvider = null)
    {
        _dataPath = Path.GetFullPath(dataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "desktop-state.json"));
        _now = nowProvider ?? (() => DateTime.Now);
        _state = LoadState();
    }

    public AppSettings Settings
    {
        get
        {
            lock (_gate) return CopySettings(_state.Settings);
        }
    }

    public int CompletedOnboardingStep
    {
        get
        {
            lock (_gate) return _state.CompletedOnboardingStep;
        }
    }

    public bool CompleteOnboardingStep(int step)
    {
        if (step is < OnboardingSteps.Welcome or > OnboardingSteps.Last)
            throw new ArgumentOutOfRangeException(nameof(step), "A etapa da configuração inicial é inválida.");

        lock (_gate)
        {
            if (_state.CompletedOnboardingStep >= step) return false;
            if (step != _state.CompletedOnboardingStep + 1)
                throw new InvalidOperationException("As etapas da configuração inicial precisam ser concluídas em ordem.");

            var next = CloneState(_state);
            next.CompletedOnboardingStep = step;
            Commit(next);
            return true;
        }
    }

    public IReadOnlyList<SessionItem> SessionsForDate(DateOnly date)
    {
        var iso = Iso(date);
        lock (_gate)
        {
            return _state.Sessions
                .Where(s => s.Date == iso)
                .OrderBy(s => s.IsCompleted ? 1 : 0)
                .ThenBy(s => KindOrder(s.Kind))
                .ThenBy(s => s.Subject, StringComparer.CurrentCultureIgnoreCase)
                .Select(s => s.Copy())
                .ToList();
        }
    }

    public IReadOnlyList<SessionItem> UpcomingSessions(DateOnly fromDate, int limit = 6)
    {
        var iso = Iso(fromDate);
        lock (_gate)
        {
            return _state.Sessions
                .Where(s => !s.IsCompleted && string.CompareOrdinal(s.Date, iso) >= 0)
                .OrderBy(s => s.Date, StringComparer.Ordinal)
                .ThenBy(s => KindOrder(s.Kind))
                .ThenBy(s => s.Subject, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Max(1, limit))
                .Select(s => s.Copy())
                .ToList();
        }
    }

    public SessionProgress ProgressForDate(DateOnly date)
    {
        var sessions = SessionsForDate(date);
        return new SessionProgress(sessions.Count(s => s.IsCompleted), sessions.Count, sessions.Sum(s => s.Minutes));
    }

    public IReadOnlyList<WeekDaySummary> WeekSummary(DateOnly anyDate)
    {
        var monday = StartOfWeek(anyDate);
        var result = new List<WeekDaySummary>(7);
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            var progress = ProgressForDate(date);
            result.Add(new WeekDaySummary(date, progress.Completed, progress.Total, progress.Minutes));
        }
        return result;
    }

    public ApplyResult PreviewPlan(PlanPackage plan)
    {
        lock (_gate) return EvaluatePlan(plan).Result;
    }

    public ApplyResult ApplyPlan(PlanPackage plan)
    {
        lock (_gate)
        {
            var evaluation = EvaluatePlan(plan);
            if (!evaluation.Result.Success) return evaluation.Result;

            var next = CloneState(_state);
            var today = Iso(DateOnly.FromDateTime(_now()));
            var removed = next.Sessions.RemoveAll(s =>
                s.Origin == "plan" && !s.IsCompleted && string.CompareOrdinal(s.Date, today) >= 0);

            foreach (var incoming in evaluation.Eligible)
                next.Sessions.Add(incoming.Copy());

            next.Settings.ActivePlanId = plan.PlanId;
            next.Settings.ActivePlanRevision = plan.Revision;
            next.Settings.ActivePlanTitle = plan.Title;
            next.Settings.ObjectiveName = plan.ObjectiveName;
            next.Settings.ObjectiveDate = plan.ObjectiveDate;
            next.Settings.PlanRevisions[plan.PlanId] = plan.Revision;
            Commit(next);

            return new ApplyResult(true, evaluation.Eligible.Count, removed, "Plano aplicado com segurança.");
        }
    }

    public bool MarkCompleted(string planId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(sessionId)) return false;
        lock (_gate)
        {
            var next = CloneState(_state);
            var item = FindIdentity(next, planId, sessionId);
            if (item is null || item.IsCompleted) return false;

            var now = _now();
            item.Status = "completed";
            item.CompletedAtUnixMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

            if (item.Kind == "study")
            {
                var completionDate = DateOnly.FromDateTime(now);
                foreach (var day in EnabledReviewDays())
                {
                    var reviewId = item.Id + StudyPlanImporter.RuntimeReviewIdMarker + day;
                    if (FindIdentity(next, item.PlanId, reviewId) is not null) continue;

                    next.Sessions.Add(new SessionItem
                    {
                        Id = reviewId,
                        PlanId = item.PlanId,
                        PlanRevision = item.PlanRevision,
                        Date = Iso(completionDate.AddDays(day)),
                        Subject = item.Subject,
                        Topic = item.Topic,
                        Minutes = ReviewMinutes(item.Minutes),
                        Target = "Recupere de memória e faça 5 questões.",
                        Kind = "review",
                        ReviewLabel = "D+" + day,
                        Status = "planned",
                        Origin = "runtime"
                    });
                }
            }

            Commit(next);
            return true;
        }
    }

    public void SavePreferences(
        string objectiveName,
        string objectiveDate,
        double dailyHours,
        int blockMinutes,
        bool d1,
        bool d3,
        bool d7,
        IEnumerable<DayOfWeek>? availableStudyDays = null,
        bool reminderEnabled = false,
        string reminderTime = StudyReminderConfiguration.DefaultTime)
    {
        objectiveDate = (objectiveDate ?? "").Trim();
        if (objectiveDate.Length > 0 && !DateOnly.TryParseExact(objectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Use uma data válida no formato AAAA-MM-DD.");
        if (!double.IsFinite(dailyHours) || dailyHours is < 1 or > 12)
            throw new ArgumentException("O limite diário precisa ficar entre 1 e 12 horas.");
        var cleanObjectiveName = CleanText(objectiveName, "Meu objetivo", 120, "Nome do objetivo");
        var normalizedDays = availableStudyDays is null ? null : NormalizeAvailableStudyDays(availableStudyDays);
        var reminder = StudyReminderConfiguration.Parse(reminderEnabled, reminderTime);

        lock (_gate)
        {
            var next = CloneState(_state);
            next.Settings.ObjectiveName = cleanObjectiveName;
            next.Settings.ObjectiveDate = objectiveDate;
            next.Settings.DailyHours = NormalizeDailyHours(dailyHours);
            next.Settings.BlockMinutes = Math.Clamp(blockMinutes, 30, 180);
            next.Settings.ReviewD1 = d1;
            next.Settings.ReviewD3 = d3;
            next.Settings.ReviewD7 = d7;
            next.Settings.ReminderEnabled = reminder.Enabled;
            next.Settings.ReminderTime = reminder.TimeText;
            if (normalizedDays is not null)
                next.Settings.AvailableStudyDays = normalizedDays;
            Commit(next);
        }
    }

    public bool CanUndoSessionMove
    {
        get
        {
            lock (_gate)
            {
                var checkpoint = _state.SessionMoveUndoCheckpoint;
                return checkpoint is not null && checkpoint.ExpectedMutationVersion == _state.MutationVersion;
            }
        }
    }

    public SessionMoveResult MoveSession(
        string planId,
        string sessionId,
        DateOnly targetDate,
        long? expectedMutationVersion = null)
    {
        lock (_gate)
        {
            if (expectedMutationVersion is not null && expectedMutationVersion != _state.MutationVersion)
            {
                return new SessionMoveResult(
                    false,
                    false,
                    "O calendário mudou depois da prévia. Revise o movimento novamente.",
                    MutationVersion: _state.MutationVersion);
            }

            var validation = EvaluateSessionMove(_state, planId, sessionId, targetDate);
            if (!validation.Success || validation.AlreadyHandled) return validation;

            var next = CloneState(_state);
            var item = FindIdentity(next, planId, sessionId);
            if (item is null)
                return new SessionMoveResult(false, false, "A sessão não existe mais no calendário.");
            item.Date = validation.TargetDate;
            next.SessionMoveUndoCheckpoint = new SessionMoveUndoCheckpoint
            {
                PlanId = planId,
                SessionId = sessionId,
                SourceDate = validation.SourceDate,
                TargetDate = validation.TargetDate,
                ExpectedMutationVersion = checked(_state.MutationVersion + 1)
            };
            Commit(next);
            return validation with { Message = "Sessão movida no calendário.", MutationVersion = _state.MutationVersion };
        }
    }

    public SessionMoveResult PreviewSessionMove(string planId, string sessionId, DateOnly targetDate)
    {
        lock (_gate) return EvaluateSessionMove(_state, planId, sessionId, targetDate);
    }

    public SessionMoveResult UndoLastSessionMove()
    {
        lock (_gate)
        {
            var checkpoint = _state.SessionMoveUndoCheckpoint;
            if (checkpoint is null)
                return new SessionMoveResult(false, false, "Não existe um movimento para desfazer.", MutationVersion: _state.MutationVersion);
            if (checkpoint.ExpectedMutationVersion != _state.MutationVersion)
            {
                return new SessionMoveResult(
                    false,
                    false,
                    "O calendário mudou depois do movimento. Nada foi desfeito para preservar as alterações posteriores.",
                    checkpoint.TargetDate,
                    checkpoint.SourceDate,
                    _state.MutationVersion);
            }

            var next = CloneState(_state);
            var item = FindIdentity(next, checkpoint.PlanId, checkpoint.SessionId);
            if (item is null || item.IsCompleted || item.Origin == "runtime" || item.Date != checkpoint.TargetDate)
            {
                return new SessionMoveResult(
                    false,
                    false,
                    "A sessão mudou e não pode mais voltar com segurança.",
                    checkpoint.TargetDate,
                    checkpoint.SourceDate,
                    _state.MutationVersion);
            }

            item.Date = checkpoint.SourceDate;
            next.SessionMoveUndoCheckpoint = null;
            Commit(next);
            return new SessionMoveResult(
                true,
                false,
                "Movimento desfeito. A sessão voltou ao dia anterior.",
                checkpoint.TargetDate,
                checkpoint.SourceDate,
                _state.MutationVersion);
        }
    }

    private SessionMoveResult EvaluateSessionMove(AppState state, string planId, string sessionId, DateOnly targetDate)
    {
        if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(sessionId))
            return new SessionMoveResult(false, false, "A sessão selecionada é inválida.");

        var item = FindIdentity(state, planId, sessionId);
        if (item is null)
            return new SessionMoveResult(false, false, "A sessão não existe mais no calendário.");

        var target = Iso(targetDate);
        if (item.IsCompleted)
            return new SessionMoveResult(false, false, "Uma sessão concluída não pode ser movida.", item.Date, target, state.MutationVersion);
        if (item.Origin == "runtime")
            return new SessionMoveResult(false, false, "Uma revisão automática protegida não pode ser movida.", item.Date, target, state.MutationVersion);
        if (target == item.Date)
            return new SessionMoveResult(true, true, "A sessão já está nesse dia.", item.Date, target, state.MutationVersion);

        var today = DateOnly.FromDateTime(_now());
        if (targetDate < today)
            return new SessionMoveResult(false, false, "Escolha hoje ou uma data futura.", item.Date, target, state.MutationVersion);
        if (!state.Settings.AvailableStudyDays.Contains(targetDate.DayOfWeek))
            return new SessionMoveResult(false, false, "Esse dia da semana não está disponível na sua rotina.", item.Date, target, state.MutationVersion);
        if (state.Settings.ObjectiveDate.Length > 0 && string.CompareOrdinal(target, state.Settings.ObjectiveDate) > 0)
            return new SessionMoveResult(false, false, "A sessão não pode ficar depois do prazo do objetivo.", item.Date, target, state.MutationVersion);

        var targetMinutes = state.Sessions
            .Where(session => session.Date == target && !ReferenceEquals(session, item))
            .Sum(session => (long)session.Minutes);
        var dailyLimit = DailyMinutesLimit(state.Settings);
        if (targetMinutes + item.Minutes > dailyLimit)
        {
            var shown = targetDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
            return new SessionMoveResult(false, false,
                $"Mover para {shown} ultrapassaria seu limite diário de {dailyLimit} minutos.",
                item.Date, target, state.MutationVersion);
        }

        return new SessionMoveResult(true, false, "Movimento validado com segurança.", item.Date, target, state.MutationVersion);
    }

    public bool SaveOnboardingRoutine(
        string objectiveName,
        string objectiveDate,
        double dailyHours,
        IEnumerable<DayOfWeek> availableStudyDays)
    {
        var cleanObjectiveName = CleanRequiredText(objectiveName, 120, "Nome do objetivo");
        objectiveDate = (objectiveDate ?? "").Trim();
        if (!DateOnly.TryParseExact(objectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline))
            throw new ArgumentException("Escolha uma data válida para o objetivo.", nameof(objectiveDate));
        if (deadline <= DateOnly.FromDateTime(_now()))
            throw new ArgumentException("A data do objetivo precisa ser posterior a hoje.", nameof(objectiveDate));
        if (!double.IsFinite(dailyHours) || dailyHours is < 1 or > 12)
            throw new ArgumentException("As horas disponíveis precisam ficar entre 1 e 12 por dia.", nameof(dailyHours));
        var normalizedDays = NormalizeAvailableStudyDays(availableStudyDays);

        lock (_gate)
        {
            if (_state.CompletedOnboardingStep >= OnboardingSteps.Routine) return false;
            if (_state.CompletedOnboardingStep != OnboardingSteps.Welcome)
                throw new InvalidOperationException("Conclua as boas-vindas antes de informar sua rotina.");

            var next = CloneState(_state);
            next.Settings.ObjectiveName = cleanObjectiveName;
            next.Settings.ObjectiveDate = objectiveDate;
            next.Settings.DailyHours = NormalizeDailyHours(dailyHours);
            next.Settings.AvailableStudyDays = normalizedDays;
            next.CompletedOnboardingStep = OnboardingSteps.Routine;
            Commit(next);
            return true;
        }
    }

    public void ExportBackup(string destinationPath)
    {
        lock (_gate)
        {
            WriteStateAtomically(CloneState(_state), destinationPath, keepRecoveryBackup: false);
        }
    }

    public RepositoryApplicationSnapshot CaptureApplicationSnapshot()
    {
        lock (_gate)
        {
            return new RepositoryApplicationSnapshot(
                _state.MutationVersion,
                Iso(DateOnly.FromDateTime(_now())),
                CopySettings(_state.Settings),
                _state.Sessions.Select(session => session.Copy()).ToList());
        }
    }

    public IReadOnlyList<CalendarApplicationReceipt> AiApplicationReceipts()
    {
        lock (_gate) return _state.AiApplications.Select(receipt => receipt.Copy()).ToList();
    }

    public CalendarApplicationState AiApplicationState(Guid proposalId)
    {
        if (proposalId == Guid.Empty)
            return new CalendarApplicationState(false, false, false, false, "A proposta não possui um ID válido.");
        lock (_gate)
        {
            var id = proposalId.ToString("D");
            var receipt = _state.AiApplications.FirstOrDefault(item => item.ProposalId == id);
            if (receipt is null)
                return new CalendarApplicationState(false, false, false, false, "A proposta ainda não foi aplicada ao calendário.");
            var undone = receipt.Status == "undone";
            var canUndo = !undone &&
                _state.AiUndoCheckpoint?.ProposalId == id &&
                _state.AiUndoCheckpoint.ExpectedMutationVersion == _state.MutationVersion;
            var message = undone
                ? "A aplicação desta proposta já foi desfeita."
                : canUndo
                    ? "A última aplicação pode ser desfeita com segurança."
                    : "O calendário mudou depois da aplicação; desfazer foi bloqueado para preservar essas alterações.";
            return new CalendarApplicationState(true, !undone, undone, canUndo, message);
        }
    }

    public CalendarMutationResult CommitAiApplication(
        Guid proposalId,
        string proposalHash,
        long expectedMutationVersion,
        string expectedSnapshotDate,
        AppSettings nextSettings,
        IReadOnlyList<SessionItem> nextSessions)
    {
        if (proposalId == Guid.Empty) throw new ArgumentException("A proposta precisa de um ID válido.", nameof(proposalId));
        ValidateProposalHash(proposalHash);
        ArgumentNullException.ThrowIfNull(nextSettings);
        ArgumentNullException.ThrowIfNull(nextSessions);

        lock (_gate)
        {
            var id = proposalId.ToString("D");
            var existing = _state.AiApplications.FirstOrDefault(receipt => receipt.ProposalId == id);
            if (existing is not null)
            {
                if (!string.Equals(existing.ProposalHash, proposalHash, StringComparison.Ordinal))
                    throw new InvalidDataException("O ID da proposta já foi usado por outro conteúdo.");
                return new CalendarMutationResult(
                    false,
                    true,
                    existing.Status == "undone"
                        ? "Esta proposta já foi aplicada e desfeita; ela não pode ser aplicada novamente."
                        : "Esta proposta já foi aplicada ao calendário.",
                    _state.MutationVersion);
            }

            if (_state.AiApplications.Count >= MaxAiApplicationReceipts)
                return new CalendarMutationResult(false, false, "O limite seguro de propostas aplicadas foi atingido.", _state.MutationVersion);
            if (_state.MutationVersion != expectedMutationVersion ||
                !string.Equals(Iso(DateOnly.FromDateTime(_now())), expectedSnapshotDate, StringComparison.Ordinal))
            {
                return new CalendarMutationResult(
                    false,
                    false,
                    "O calendário mudou depois da confirmação. Revise a prévia atualizada antes de aplicar.",
                    _state.MutationVersion);
            }

            var candidateSettings = CopySettings(nextSettings);
            var candidateSessions = nextSessions.Select(session => session.Copy()).ToList();
            EnsureSafeAiTransition(_state.Settings, _state.Sessions, candidateSettings, candidateSessions, expectedSnapshotDate);

            var appliedVersion = checked(_state.MutationVersion + 1);
            var next = CloneState(_state);
            next.Settings = candidateSettings;
            next.Sessions = candidateSessions;
            next.AiApplications.Add(new CalendarApplicationReceipt
            {
                ProposalId = id,
                ProposalHash = proposalHash,
                Status = "applied",
                AppliedMutationVersion = appliedVersion,
                AppliedAtUnixMs = new DateTimeOffset(_now()).ToUnixTimeMilliseconds()
            });
            next.AiUndoCheckpoint = new CalendarUndoCheckpoint
            {
                ProposalId = id,
                ExpectedMutationVersion = appliedVersion,
                Settings = CopySettings(_state.Settings),
                Sessions = _state.Sessions.Select(session => session.Copy()).ToList()
            };
            Commit(next);
            return new CalendarMutationResult(true, false, "Proposta aplicada ao calendário com segurança.", _state.MutationVersion);
        }
    }

    public CalendarMutationResult ValidateAiApplication(
        long expectedMutationVersion,
        string expectedSnapshotDate,
        AppSettings nextSettings,
        IReadOnlyList<SessionItem> nextSessions)
    {
        ArgumentNullException.ThrowIfNull(nextSettings);
        ArgumentNullException.ThrowIfNull(nextSessions);
        lock (_gate)
        {
            if (_state.MutationVersion != expectedMutationVersion ||
                !string.Equals(Iso(DateOnly.FromDateTime(_now())), expectedSnapshotDate, StringComparison.Ordinal))
            {
                return new CalendarMutationResult(
                    false,
                    false,
                    "O calendário mudou durante a revisão. Abra a proposta novamente para conferir a prévia atual.",
                    _state.MutationVersion);
            }
            try
            {
                EnsureSafeAiTransition(
                    _state.Settings,
                    _state.Sessions,
                    CopySettings(nextSettings),
                    nextSessions.Select(session => session.Copy()).ToList(),
                    expectedSnapshotDate);
                return new CalendarMutationResult(true, false, "A aplicação foi revalidada no calendário atual.", _state.MutationVersion);
            }
            catch (InvalidDataException ex)
            {
                return new CalendarMutationResult(false, false, ex.Message, _state.MutationVersion);
            }
        }
    }

    public CalendarMutationResult UndoAiApplication(Guid proposalId)
    {
        if (proposalId == Guid.Empty) throw new ArgumentException("A proposta precisa de um ID válido.", nameof(proposalId));
        lock (_gate)
        {
            var id = proposalId.ToString("D");
            var index = _state.AiApplications.FindIndex(receipt => receipt.ProposalId == id);
            if (index < 0)
                return new CalendarMutationResult(false, false, "Não existe uma aplicação registrada para desfazer.", _state.MutationVersion);
            var receipt = _state.AiApplications[index];
            if (receipt.Status == "undone")
                return new CalendarMutationResult(false, true, "Esta aplicação já foi desfeita.", _state.MutationVersion);
            var checkpoint = _state.AiUndoCheckpoint;
            if (checkpoint is null || checkpoint.ProposalId != id || checkpoint.ExpectedMutationVersion != _state.MutationVersion)
            {
                return new CalendarMutationResult(
                    false,
                    false,
                    "O calendário mudou depois da aplicação. O Rota não desfez nada para preservar as alterações posteriores.",
                    _state.MutationVersion);
            }

            var restoredSettings = CopySettings(checkpoint.Settings);
            foreach (var revision in _state.Settings.PlanRevisions)
            {
                restoredSettings.PlanRevisions.TryGetValue(revision.Key, out var previous);
                restoredSettings.PlanRevisions[revision.Key] = Math.Max(previous, revision.Value);
            }

            var undoneVersion = checked(_state.MutationVersion + 1);
            var next = CloneState(_state);
            next.Settings = restoredSettings;
            next.Sessions = checkpoint.Sessions.Select(session => session.Copy()).ToList();
            next.AiApplications[index] = receipt.Copy();
            next.AiApplications[index].Status = "undone";
            next.AiApplications[index].UndoneMutationVersion = undoneVersion;
            next.AiApplications[index].UndoneAtUnixMs = new DateTimeOffset(_now()).ToUnixTimeMilliseconds();
            next.AiUndoCheckpoint = null;
            Commit(next);
            return new CalendarMutationResult(true, false, "Aplicação desfeita com segurança.", _state.MutationVersion);
        }
    }

    private (ApplyResult Result, List<SessionItem> Eligible) EvaluatePlan(PlanPackage plan)
    {
        var knownRevision = _state.Settings.PlanRevisions.TryGetValue(plan.PlanId, out var storedRevision) ? storedRevision : 0;
        knownRevision = Math.Max(knownRevision, _state.Sessions.Where(s => s.PlanId == plan.PlanId).Select(s => s.PlanRevision).DefaultIfEmpty(0).Max());
        if (_state.Settings.ActivePlanId == plan.PlanId)
            knownRevision = Math.Max(knownRevision, _state.Settings.ActivePlanRevision);

        if (plan.Revision <= knownRevision)
        {
            return (new ApplyResult(false, 0, 0,
                $"A revisão importada precisa ser maior que a maior revisão já aplicada deste plano ({knownRevision})."), new());
        }

        var today = Iso(DateOnly.FromDateTime(_now()));
        var eligible = new List<SessionItem>();
        foreach (var incoming in plan.Sessions)
        {
            if (string.CompareOrdinal(incoming.Date, today) < 0) continue;
            var existing = FindIdentity(incoming.PlanId, incoming.Id);
            if (existing is not null && (existing.IsCompleted || existing.Origin == "runtime" || string.CompareOrdinal(existing.Date, today) < 0))
                continue;
            eligible.Add(incoming.Copy());
        }

        if (eligible.Count == 0)
            return (new ApplyResult(false, 0, 0, "O plano não contém nenhuma sessão futura que possa ser aplicada com segurança."), eligible);

        var availableDays = _state.Settings.AvailableStudyDays.ToHashSet();
        var outsideAvailability = eligible.FirstOrDefault(session =>
            !availableDays.Contains(DateOnly.ParseExact(session.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek));
        if (outsideAvailability is not null)
        {
            return (new ApplyResult(
                false,
                0,
                0,
                $"O plano contém uma sessão fora dos dias disponíveis ({outsideAvailability.Date})."), eligible);
        }

        var protectedSessions = _state.Sessions.Where(s =>
            string.CompareOrdinal(s.Date, today) >= 0 && (s.IsCompleted || s.Origin == "runtime"));
        var capacity = eligible.Concat(protectedSessions).ToList();
        var dailyLimit = DailyMinutesLimit(_state.Settings);
        var overloadedDate = FirstOverloadedDate(capacity, dailyLimit);
        if (overloadedDate.Length > 0)
        {
            var shown = DateOnly.ParseExact(overloadedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
            return (new ApplyResult(false, 0, 0,
                $"O plano, somado ao histórico concluído e às revisões automáticas preservadas, ultrapassa seu limite diário em {shown}. Ajuste o plano ou o limite diário.", overloadedDate), eligible);
        }

        var removable = _state.Sessions.Count(s => s.Origin == "plan" && !s.IsCompleted && string.CompareOrdinal(s.Date, today) >= 0);
        return (new ApplyResult(true, eligible.Count, removable, $"{eligible.Count} sessões prontas para aplicar. {removable} sessões futuras pendentes serão substituídas."), eligible);
    }

    private SessionItem? FindIdentity(string planId, string id) =>
        FindIdentity(_state, planId, id);

    private static SessionItem? FindIdentity(AppState state, string planId, string id) =>
        state.Sessions.FirstOrDefault(s => s.PlanId == planId && s.Id == id);

    private IEnumerable<int> EnabledReviewDays()
    {
        if (_state.Settings.ReviewD1) yield return 1;
        if (_state.Settings.ReviewD3) yield return 3;
        if (_state.Settings.ReviewD7) yield return 7;
    }

    public static int ReviewMinutes(int originalMinutes) => Math.Max(15, Math.Min(35, originalMinutes / 3));

    public static int DailyMinutesLimit(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(settings.DailyHours))
            throw new InvalidDataException("O limite diário armazenado é inválido.");
        return checked((int)Math.Round(settings.DailyHours * 60, MidpointRounding.AwayFromZero));
    }

    public static string FirstOverloadedDate(IEnumerable<SessionItem> sessions, int maxMinutesPerDay)
    {
        if (maxMinutesPerDay <= 0) return "";
        var totals = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            totals.TryGetValue(session.Date, out var current);
            totals[session.Date] = current + Math.Max(0L, session.Minutes);
        }
        return totals.FirstOrDefault(entry => entry.Value > maxMinutesPerDay).Key ?? "";
    }

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static double NormalizeDailyHours(double hours) =>
        Math.Round(hours * 60, MidpointRounding.AwayFromZero) / 60d;

    private static void ValidateProposalHash(string hash)
    {
        if (!IsValidProposalHash(hash))
            throw new ArgumentException("A assinatura da proposta é inválida.", nameof(hash));
    }

    private static bool IsValidProposalHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit) &&
        string.Equals(hash, hash.ToUpperInvariant(), StringComparison.Ordinal);

    private static void EnsureSafeAiTransition(
        AppSettings currentSettings,
        IReadOnlyList<SessionItem> currentSessions,
        AppSettings nextSettings,
        IReadOnlyList<SessionItem> nextSessions,
        string today)
    {
        ValidateCoreState(nextSettings, nextSessions);

        foreach (var revision in currentSettings.PlanRevisions)
        {
            if (!nextSettings.PlanRevisions.TryGetValue(revision.Key, out var nextRevision) || nextRevision < revision.Value)
                throw new InvalidDataException("Uma aplicação da IA não pode reduzir o histórico de revisões dos planos.");
        }

        var nextByIdentity = nextSessions.ToDictionary(session => (session.PlanId, session.Id));
        foreach (var current in currentSessions)
        {
            var isProtected = current.IsCompleted || current.Origin == "runtime" || string.CompareOrdinal(current.Date, today) < 0;
            if (!isProtected) continue;
            if (!nextByIdentity.TryGetValue((current.PlanId, current.Id), out var preserved) || !SessionEquals(current, preserved))
                throw new InvalidDataException("A aplicação tentou alterar histórico concluído, passado ou uma revisão automática protegida.");
        }

        var currentIdentities = currentSessions.Select(session => (session.PlanId, session.Id)).ToHashSet();
        if (nextSessions.Any(session => session.Origin == "runtime" && !currentIdentities.Contains((session.PlanId, session.Id))))
            throw new InvalidDataException("A aplicação não pode criar revisões automáticas diretamente.");

        var future = nextSessions.Where(session => string.CompareOrdinal(session.Date, today) >= 0).ToList();
        var overloadedDate = FirstOverloadedDate(future, DailyMinutesLimit(nextSettings));
        if (overloadedDate.Length > 0)
            throw new InvalidDataException($"A agenda proposta ultrapassa o limite diário em {overloadedDate}.");

        var availableDays = nextSettings.AvailableStudyDays.ToHashSet();
        var outsideAvailability = future.FirstOrDefault(session =>
            !session.IsCompleted && session.Origin == "plan" &&
            !availableDays.Contains(DateOnly.ParseExact(session.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek));
        if (outsideAvailability is not null)
            throw new InvalidDataException("A agenda proposta mantém uma sessão comum fora dos dias disponíveis.");

        if (nextSettings.ObjectiveDate.Length > 0 && future.Any(session =>
                !session.IsCompleted && session.Origin == "plan" &&
                string.CompareOrdinal(session.Date, nextSettings.ObjectiveDate) > 0))
        {
            throw new InvalidDataException("A agenda proposta ultrapassa a data do objetivo.");
        }
    }

    private static bool SessionEquals(SessionItem left, SessionItem right) =>
        left.Id == right.Id && left.PlanId == right.PlanId && left.PlanRevision == right.PlanRevision &&
        left.Date == right.Date && left.Subject == right.Subject && left.Topic == right.Topic &&
        left.Minutes == right.Minutes && left.Target == right.Target && left.Kind == right.Kind &&
        left.ReviewLabel == right.ReviewLabel && left.Status == right.Status && left.Origin == right.Origin &&
        left.CompletedAtUnixMs == right.CompletedAtUnixMs;

    private static int KindOrder(string kind) => kind switch
    {
        "review" => 0,
        "study" => 1,
        "assessment" => 2,
        _ => 3
    };

    private AppState LoadState()
    {
        if (!File.Exists(_dataPath))
        {
            var fresh = CreateFreshState();
            SaveStateInternal(fresh);
            return fresh;
        }

        try
        {
            return ReadState(_dataPath);
        }
        catch (Exception ex) when (IsInvalidStateError(ex))
        {
            var corruptPath = PreserveInvalidFile(_dataPath, "desktop-state.corrupt");
            var backupPath = _dataPath + ".bak";
            try
            {
                if (File.Exists(backupPath))
                {
                    var recovered = ReadState(backupPath);
                    SaveStateInternal(recovered);
                    LastLoadWarning =
                        $"O estado principal estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. " +
                        "O Rota recuperou automaticamente a última versão íntegra.";
                    return recovered;
                }
            }
            catch (Exception backupError) when (IsInvalidStateError(backupError))
            {
                PreserveInvalidFile(backupPath, "desktop-state.backup-corrupt");
            }

            var fresh = CreateFreshState();
            SaveStateInternal(fresh);
            LastLoadWarning =
                $"O estado local estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. " +
                "Não havia uma cópia íntegra anterior; o Rota iniciou um estado novo.";
            return fresh;
        }
    }

    private AppState ReadState(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxStateFileBytes)
            throw new InvalidDataException("O arquivo de estado excede o limite seguro de 8 MB.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        var loaded = JsonSerializer.Deserialize<AppState>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo de estado está vazio.");
        ValidateState(loaded);
        return CloneState(loaded);
    }

    private void Commit(AppState next)
    {
        next.MutationVersion = checked(_state.MutationVersion + 1);
        ValidateState(next);
        SaveStateInternal(next);
        _state = next;
    }

    private void SaveStateInternal(AppState state) =>
        WriteStateAtomically(state, _dataPath, keepRecoveryBackup: true);

    private void WriteStateAtomically(AppState state, string destinationPath, bool keepRecoveryBackup)
    {
        ValidateState(state);
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Não foi possível determinar a pasta de destino.");
        Directory.CreateDirectory(directory);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
        if (bytes.LongLength > MaxStateFileBytes)
            throw new IOException("O estado do Rota excedeu o limite seguro de 8 MB.");

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(fullPath))
            {
                File.Move(tempPath, fullPath);
            }
            else if (keepRecoveryBackup)
            {
                File.Replace(tempPath, fullPath, fullPath + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullPath, overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private string PreserveInvalidFile(string path, string prefix)
    {
        if (!File.Exists(path)) return "";
        Directory.CreateDirectory(DataDirectory);
        var preserved = Path.Combine(DataDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Move(path, preserved);
        return preserved;
    }

    private static bool IsInvalidStateError(Exception ex) =>
        ex is JsonException or InvalidDataException or DecoderFallbackException or NotSupportedException;

    private static AppState CreateFreshState() => new()
    {
        StateVersion = CurrentStateVersion,
        MutationVersion = 0,
        CompletedOnboardingStep = 0,
        Settings = new AppSettings(),
        Sessions = new List<SessionItem>(),
        AiApplications = new List<CalendarApplicationReceipt>()
    };

    private static void ValidateState(AppState state)
    {
        if (state.StateVersion != CurrentStateVersion)
            throw new InvalidDataException($"Versão de estado não suportada: {state.StateVersion}.");
        if (state.MutationVersion < 0)
            throw new InvalidDataException("A versão de alteração do estado é inválida.");
        if (state.CompletedOnboardingStep is < 0 or > OnboardingSteps.Last)
            throw new InvalidDataException("A etapa da configuração inicial é inválida.");
        if (state.Settings is null || state.Sessions is null || state.AiApplications is null)
            throw new InvalidDataException("O estado local está incompleto.");

        ValidateCoreState(state.Settings, state.Sessions);
        if (state.AiApplications.Count > MaxAiApplicationReceipts)
            throw new InvalidDataException("O estado contém recibos de aplicação demais.");

        var receiptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var receipt in state.AiApplications)
        {
            if (receipt is null || !Guid.TryParseExact(receipt.ProposalId, "D", out var receiptId) ||
                receipt.ProposalId != receiptId.ToString("D") || !receiptIds.Add(receipt.ProposalId))
                throw new InvalidDataException("O estado contém recibos de aplicação inválidos ou duplicados.");
            if (!IsValidProposalHash(receipt.ProposalHash))
                throw new InvalidDataException("Um recibo contém assinatura de proposta inválida.");
            if (receipt.Status is not "applied" and not "undone" ||
                receipt.AppliedMutationVersion < 1 || receipt.AppliedMutationVersion > state.MutationVersion ||
                receipt.AppliedAtUnixMs <= 0)
            {
                throw new InvalidDataException("Um recibo de aplicação está inconsistente.");
            }
            if (receipt.Status == "applied" && (receipt.UndoneMutationVersion != 0 || receipt.UndoneAtUnixMs != 0))
                throw new InvalidDataException("Um recibo aplicado contém dados indevidos de desfazer.");
            if (receipt.Status == "undone" &&
                (receipt.UndoneMutationVersion <= receipt.AppliedMutationVersion ||
                 receipt.UndoneMutationVersion > state.MutationVersion || receipt.UndoneAtUnixMs <= 0))
            {
                throw new InvalidDataException("Um recibo desfeito está inconsistente.");
            }
        }

        if (state.AiUndoCheckpoint is { } checkpoint)
        {
            if (!Guid.TryParseExact(checkpoint.ProposalId, "D", out var checkpointId) ||
                checkpoint.ProposalId != checkpointId.ToString("D") ||
                checkpoint.ExpectedMutationVersion < 1 || checkpoint.ExpectedMutationVersion > state.MutationVersion)
            {
                throw new InvalidDataException("O ponto de desfazer da IA é inválido.");
            }
            var matchingReceipt = state.AiApplications.FirstOrDefault(receipt => receipt.ProposalId == checkpoint.ProposalId);
            if (matchingReceipt is null || matchingReceipt.Status != "applied" ||
                matchingReceipt.AppliedMutationVersion != checkpoint.ExpectedMutationVersion)
            {
                throw new InvalidDataException("O ponto de desfazer não corresponde a uma aplicação registrada.");
            }
            if (checkpoint.Settings is null || checkpoint.Sessions is null)
                throw new InvalidDataException("O ponto de desfazer está incompleto.");
            ValidateCoreState(checkpoint.Settings, checkpoint.Sessions);
        }

        if (state.SessionMoveUndoCheckpoint is { } moveCheckpoint)
        {
            ValidateStoredText(moveCheckpoint.PlanId, "SessionMoveUndo.PlanId", 80, allowEmpty: false);
            ValidateStoredText(moveCheckpoint.SessionId, "SessionMoveUndo.SessionId", 100, allowEmpty: false);
            if (!DateOnly.TryParseExact(moveCheckpoint.SourceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !DateOnly.TryParseExact(moveCheckpoint.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                moveCheckpoint.SourceDate == moveCheckpoint.TargetDate ||
                moveCheckpoint.ExpectedMutationVersion < 1 ||
                moveCheckpoint.ExpectedMutationVersion > state.MutationVersion)
            {
                throw new InvalidDataException("O ponto de desfazer do movimento é inválido.");
            }
        }
    }

    private static void ValidateCoreState(AppSettings settings, IReadOnlyList<SessionItem> sessions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessions);

        ValidateStoredText(settings.ObjectiveName, "ObjectiveName", 120, allowEmpty: false);
        ValidateStoredText(settings.ObjectiveDate, "ObjectiveDate", 10, allowEmpty: true);
        if (settings.ObjectiveDate.Length > 0 && !DateOnly.TryParseExact(settings.ObjectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("ObjectiveDate contém uma data inválida.");
        if (!double.IsFinite(settings.DailyHours) || settings.DailyHours is < 1 or > 12 ||
            Math.Abs(settings.DailyHours * 60 - Math.Round(settings.DailyHours * 60)) > 0.000001 ||
            settings.BlockMinutes is < 30 or > 180)
            throw new InvalidDataException("As preferências de duração estão fora dos limites.");
        if (settings.AvailableStudyDays is null || settings.AvailableStudyDays.Count is < 1 or > 7 ||
            settings.AvailableStudyDays.Distinct().Count() != settings.AvailableStudyDays.Count ||
            settings.AvailableStudyDays.Any(day => !Enum.IsDefined(day)))
        {
            throw new InvalidDataException("Os dias disponíveis armazenados são inválidos.");
        }
        try
        {
            _ = StudyReminderConfiguration.Parse(settings.ReminderEnabled, settings.ReminderTime);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("O horário de lembrete armazenado é inválido.", ex);
        }
        if (settings.SubjectPriorities is null || settings.SubjectPriorities.Count > 500)
            throw new InvalidDataException("As prioridades de matérias armazenadas são inválidas.");
        var prioritySubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var priority in settings.SubjectPriorities)
        {
            ValidateStoredText(priority.Key, "SubjectPriorities.subject", 80, allowEmpty: false);
            if (!prioritySubjects.Add(priority.Key) || priority.Value is < 1 or > 100)
                throw new InvalidDataException("Uma prioridade de matéria armazenada é inválida.");
        }
        ValidateStoredText(settings.ActivePlanId, "ActivePlanId", 80, allowEmpty: true);
        ValidateStoredText(settings.ActivePlanTitle, "ActivePlanTitle", 120, allowEmpty: true);
        if (settings.ActivePlanRevision < 0 || (settings.ActivePlanId.Length > 0 && settings.ActivePlanRevision < 1))
            throw new InvalidDataException("A revisão do plano ativo é inválida.");
        if (settings.PlanRevisions is null || settings.PlanRevisions.Count > 10_000)
            throw new InvalidDataException("O índice de revisões de planos é inválido.");
        foreach (var entry in settings.PlanRevisions)
        {
            ValidateStoredText(entry.Key, "PlanRevisions.id", 80, allowEmpty: false);
            if (entry.Value < 1) throw new InvalidDataException("O índice contém uma revisão inválida.");
        }

        if (sessions.Count > MaxStoredSessions)
            throw new InvalidDataException($"O estado excede o limite de {MaxStoredSessions} sessões armazenadas.");
        var identities = new HashSet<(string PlanId, string Id)>();
        foreach (var session in sessions)
        {
            if (session is null) throw new InvalidDataException("O estado contém uma sessão nula.");
            ValidateStoredText(session.Id, "Session.Id", 100, allowEmpty: false);
            ValidateStoredText(session.PlanId, "Session.PlanId", 80, allowEmpty: false);
            if (!identities.Add((session.PlanId, session.Id)))
                throw new InvalidDataException("O estado contém identidades de sessão duplicadas.");
            if (session.PlanRevision < 1) throw new InvalidDataException("Uma sessão possui revisão inválida.");
            ValidateStoredText(session.Date, "Session.Date", 10, allowEmpty: false);
            if (!DateOnly.TryParseExact(session.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Uma sessão possui data inválida.");
            ValidateStoredText(session.Subject, "Session.Subject", 80, allowEmpty: false);
            ValidateStoredText(session.Topic, "Session.Topic", 160, allowEmpty: false);
            ValidateStoredText(session.Target, "Session.Target", 180, allowEmpty: true);
            ValidateStoredText(session.ReviewLabel, "Session.ReviewLabel", 40, allowEmpty: true);
            if (session.Minutes is < 10 or > 360) throw new InvalidDataException("Uma sessão possui duração inválida.");
            if (session.Kind is not "study" and not "review" and not "assessment") throw new InvalidDataException("Uma sessão possui kind inválido.");
            if (session.Kind != "review" && session.ReviewLabel.Length > 0) throw new InvalidDataException("Uma sessão não-review possui review_label.");
            if (session.Status is not "planned" and not "completed") throw new InvalidDataException("Uma sessão possui status inválido.");
            if (session.Origin is not "plan" and not "runtime") throw new InvalidDataException("Uma sessão possui origem inválida.");
            var usesRuntimeNamespace = session.Id.Contains(StudyPlanImporter.RuntimeReviewIdMarker, StringComparison.Ordinal);
            if (session.Origin == "runtime" && (session.Kind != "review" || !usesRuntimeNamespace))
                throw new InvalidDataException("Uma revisão runtime possui identidade inválida.");
            if (session.Origin == "plan" && usesRuntimeNamespace)
                throw new InvalidDataException("Uma sessão importada usa o namespace runtime.");
            if (session.IsCompleted != (session.CompletedAtUnixMs > 0))
                throw new InvalidDataException("O registro de conclusão de uma sessão é inconsistente.");
        }
    }

    private static void ValidateStoredText(string? value, string field, int max, bool allowEmpty)
    {
        if (value is null || value.Length > max || value.Any(char.IsControl) ||
            (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException($"{field} contém texto inválido.");
    }

    private static string CleanText(string? value, string fallback, int max, string field)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (clean.Length > max) throw new ArgumentException($"{field} deve ter no máximo {max} caracteres.");
        if (clean.Any(char.IsControl)) throw new ArgumentException($"{field} contém caractere de controle não permitido.");
        return clean;
    }

    private static string CleanRequiredText(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} é obrigatório.", field);
        return CleanText(value, "", max, field);
    }

    private static List<DayOfWeek> NormalizeAvailableStudyDays(IEnumerable<DayOfWeek>? days)
    {
        if (days is null)
            throw new ArgumentException("Escolha pelo menos um dia de estudo.", nameof(days));
        var normalized = days.Distinct().ToList();
        if (normalized.Count is < 1 or > 7 || normalized.Any(day => !Enum.IsDefined(day)))
            throw new ArgumentException("Escolha entre um e sete dias de estudo válidos.", nameof(days));
        return normalized.OrderBy(day => ((int)day + 6) % 7).ToList();
    }

    private static AppState CloneState(AppState source) => new()
    {
        StateVersion = source.StateVersion,
        MutationVersion = source.MutationVersion,
        CompletedOnboardingStep = source.CompletedOnboardingStep,
        Settings = CopySettings(source.Settings),
        Sessions = source.Sessions.Select(session => session.Copy()).ToList(),
        AiApplications = source.AiApplications.Select(receipt => receipt.Copy()).ToList(),
        AiUndoCheckpoint = source.AiUndoCheckpoint?.Copy(),
        SessionMoveUndoCheckpoint = source.SessionMoveUndoCheckpoint?.Copy()
    };

    public static AppSettings CopySettings(AppSettings source) => new()
    {
        ObjectiveName = source.ObjectiveName,
        ObjectiveDate = source.ObjectiveDate,
        DailyHours = source.DailyHours,
        BlockMinutes = source.BlockMinutes,
        ReviewD1 = source.ReviewD1,
        ReviewD3 = source.ReviewD3,
        ReviewD7 = source.ReviewD7,
        ReminderEnabled = source.ReminderEnabled,
        ReminderTime = source.ReminderTime,
        AvailableStudyDays = source.AvailableStudyDays.ToList(),
        SubjectPriorities = new Dictionary<string, int>(source.SubjectPriorities, StringComparer.OrdinalIgnoreCase),
        ActivePlanId = source.ActivePlanId,
        ActivePlanRevision = source.ActivePlanRevision,
        ActivePlanTitle = source.ActivePlanTitle,
        PlanRevisions = new Dictionary<string, int>(source.PlanRevisions, StringComparer.Ordinal)
    };
}

