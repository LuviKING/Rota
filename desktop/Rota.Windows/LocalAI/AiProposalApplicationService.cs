using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public sealed class AiProposalApplicationService : IAiProposalApplicationService, IDisposable
{
    private readonly StudyRepository _repository;
    private readonly IAiProposalPreviewService _previewService;
    private readonly IAiProposalStore _proposalStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _canonicalJson = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private PreparedWork? _prepared;
    private bool _disposed;

    public AiProposalApplicationService(
        StudyRepository repository,
        IAiProposalPreviewService previewService,
        IAiProposalStore proposalStore)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconcileCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiPreparedApplication> PrepareAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (proposalId == Guid.Empty)
            throw new AiProposalApplicationException("A proposta não possui um ID válido.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _prepared = null;
            await ReconcileCoreAsync(cancellationToken).ConfigureAwait(false);
            var history = await _proposalStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var stored = history.FirstOrDefault(item => item.Proposal.Id == proposalId)
                ?? throw new AiProposalApplicationException("A proposta não existe no histórico local.");
            var calendarState = _repository.AiApplicationState(proposalId);
            if (calendarState.Exists)
                throw new AiProposalApplicationException(calendarState.Message);
            if (stored.Proposal.Status is not AiProposalStatus.Validated and not AiProposalStatus.Accepted)
                throw new AiProposalApplicationException("Somente uma proposta validada pode ser revisada para aplicação.");

            var snapshot = _repository.CaptureApplicationSnapshot();
            var pending = stored.Proposal with { Status = AiProposalStatus.Pending };
            var context = pending.Kind == AiProposalKind.PlanChanges
                ? StudyPlanningContextProvider.FromSnapshot(snapshot)
                : null;
            var preview = _previewService.Preview(pending, context);
            AppSettings? nextSettings = null;
            IReadOnlyList<SessionItem>? nextSessions = null;

            if (preview.CanProceed)
            {
                try
                {
                    (nextSettings, nextSessions) = pending.Kind switch
                    {
                        AiProposalKind.StudyPlan => BuildStudyPlan(snapshot, pending),
                        AiProposalKind.PlanChanges => BuildPlanChanges(snapshot, pending, preview),
                        _ => throw new InvalidDataException("O tipo de proposta não pode ser aplicado.")
                    };
                    var validation = _repository.ValidateAiApplication(
                        snapshot.MutationVersion,
                        snapshot.SnapshotDate,
                        nextSettings,
                        nextSessions);
                    if (!validation.Success)
                    {
                        preview = Blocked(pending, validation.Message, snapshot);
                        nextSettings = null;
                        nextSessions = null;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or OverflowException)
                {
                    preview = Blocked(pending, ex.Message, snapshot);
                    nextSettings = null;
                    nextSessions = null;
                }
            }

            AiContractValidator.ValidatePreview(preview);
            var changed = !PreviewEquals(stored.Preview, preview);
            var confirmationId = preview.CanProceed ? Guid.NewGuid() : Guid.Empty;
            if (preview.CanProceed)
            {
                _prepared = new PreparedWork(
                    confirmationId,
                    proposalId,
                    ProposalHash(pending),
                    snapshot.MutationVersion,
                    snapshot.SnapshotDate,
                    StudyRepository.CopySettings(nextSettings!),
                    nextSessions!.Select(session => session.Copy()).ToList());
            }

            return new AiPreparedApplication
            {
                ConfirmationId = confirmationId,
                ProposalId = proposalId,
                Summary = stored.Proposal.Summary,
                Kind = stored.Proposal.Kind,
                Preview = preview,
                ChangedSinceSavedPreview = changed,
                Message = preview.CanProceed
                    ? changed
                        ? "A prévia foi recalculada com o calendário atual. Confira os novos números antes de confirmar."
                        : "A prévia continua válida no calendário atual."
                    : "A proposta foi bloqueada na nova verificação e não pode ser aplicada."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiProposalApplicationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiProposalApplicationException("O Rota não conseguiu preparar a confirmação da proposta.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiProposalApplicationResult> ApplyAsync(
        Guid confirmationId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var prepared = _prepared;
            _prepared = null;
            if (prepared is null || confirmationId == Guid.Empty || prepared.ConfirmationId != confirmationId)
            {
                return new AiProposalApplicationResult
                {
                    Message = "A confirmação expirou. Revise a proposta novamente antes de aplicar."
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            CalendarMutationResult committed;
            try
            {
                committed = _repository.CommitAiApplication(
                    prepared.ProposalId,
                    prepared.ProposalHash,
                    prepared.ExpectedMutationVersion,
                    prepared.ExpectedSnapshotDate,
                    prepared.NextSettings,
                    prepared.NextSessions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new AiProposalApplicationResult
                {
                    Message = "O calendário não foi alterado porque a gravação segura falhou. " + ex.Message
                };
            }

            if (!committed.Success)
            {
                return new AiProposalApplicationResult
                {
                    AlreadyHandled = committed.AlreadyHandled,
                    Message = committed.Message
                };
            }

            var synchronized = await TryMarkAppliedAsync(prepared.ProposalId).ConfigureAwait(false);
            return new AiProposalApplicationResult
            {
                Success = true,
                HistorySynchronized = synchronized,
                Message = synchronized
                    ? committed.Message
                    : committed.Message + " O histórico será sincronizado automaticamente na próxima abertura."
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiProposalApplicationResult> UndoAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _prepared = null;
            cancellationToken.ThrowIfCancellationRequested();
            CalendarMutationResult undone;
            try
            {
                undone = _repository.UndoAiApplication(proposalId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new AiProposalApplicationResult
                {
                    Message = "Nada foi desfeito porque a gravação segura falhou. " + ex.Message
                };
            }

            if (!undone.Success && !undone.AlreadyHandled)
                return new AiProposalApplicationResult { Message = undone.Message };

            var synchronized = await TryMarkUndoneAsync(proposalId).ConfigureAwait(false);
            return new AiProposalApplicationResult
            {
                Success = undone.Success,
                AlreadyHandled = undone.AlreadyHandled,
                HistorySynchronized = synchronized,
                Message = synchronized
                    ? undone.Message
                    : undone.Message + " O histórico será sincronizado automaticamente na próxima abertura."
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiStoredProposal> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_prepared?.ProposalId == proposalId) _prepared = null;
            return await _proposalStore.RejectAsync(proposalId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public CalendarApplicationState GetCalendarState(Guid proposalId) =>
        _repository.AiApplicationState(proposalId);

    private async Task ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var history = await _proposalStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var receipts = _repository.AiApplicationReceipts()
            .ToDictionary(receipt => Guid.ParseExact(receipt.ProposalId, "D"));
        foreach (var stored in history)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (receipts.TryGetValue(stored.Proposal.Id, out var receipt))
            {
                var hash = ProposalHash(stored.Proposal with { Status = AiProposalStatus.Pending });
                if (!string.Equals(hash, receipt.ProposalHash, StringComparison.Ordinal))
                    throw new AiProposalApplicationException("Um recibo de aplicação não corresponde à proposta armazenada.");

                if (receipt.Status == "applied" &&
                    stored.Proposal.Status is AiProposalStatus.Validated or AiProposalStatus.Accepted or AiProposalStatus.Undone)
                    await _proposalStore.MarkAppliedAsync(stored.Proposal.Id, cancellationToken).ConfigureAwait(false);
                else if (receipt.Status == "undone" && stored.Proposal.Status != AiProposalStatus.Undone)
                {
                    if (stored.Proposal.Status is AiProposalStatus.Validated or AiProposalStatus.Accepted)
                        await _proposalStore.MarkAppliedAsync(stored.Proposal.Id, cancellationToken).ConfigureAwait(false);
                    await _proposalStore.MarkUndoneAsync(stored.Proposal.Id, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (stored.Proposal.Status == AiProposalStatus.Applied &&
                     !string.IsNullOrWhiteSpace(_repository.LastLoadWarning))
            {
                await _proposalStore.MarkUndoneAsync(stored.Proposal.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryMarkAppliedAsync(Guid proposalId)
    {
        try
        {
            await _proposalStore.MarkAppliedAsync(proposalId, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryMarkUndoneAsync(Guid proposalId)
    {
        try
        {
            await _proposalStore.MarkUndoneAsync(proposalId, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (AppSettings Settings, IReadOnlyList<SessionItem> Sessions) BuildStudyPlan(
        RepositoryApplicationSnapshot snapshot,
        AiProposal proposal)
    {
        var package = StudyPlanImporter.Parse(proposal.StudyPlan!.StudyPlanJson);
        var knownRevision = snapshot.Settings.PlanRevisions.TryGetValue(package.PlanId, out var storedRevision)
            ? storedRevision
            : 0;
        knownRevision = Math.Max(knownRevision, snapshot.Sessions
            .Where(session => session.PlanId == package.PlanId)
            .Select(session => session.PlanRevision)
            .DefaultIfEmpty(0)
            .Max());
        if (snapshot.Settings.ActivePlanId == package.PlanId)
            knownRevision = Math.Max(knownRevision, snapshot.Settings.ActivePlanRevision);
        if (package.Revision <= knownRevision)
            throw new InvalidDataException($"A revisão proposta precisa ser maior que a revisão {knownRevision} já aplicada.");

        var eligible = new List<SessionItem>();
        foreach (var incoming in package.Sessions)
        {
            if (string.CompareOrdinal(incoming.Date, snapshot.SnapshotDate) < 0) continue;
            var existing = snapshot.Sessions.FirstOrDefault(session =>
                session.PlanId == incoming.PlanId && session.Id == incoming.Id);
            if (existing is not null && (existing.IsCompleted || existing.Origin == "runtime" ||
                                         string.CompareOrdinal(existing.Date, snapshot.SnapshotDate) < 0))
            {
                continue;
            }
            eligible.Add(incoming.Copy());
        }
        if (eligible.Count == 0)
            throw new InvalidDataException("O plano não contém nenhuma sessão futura que possa ser aplicada com segurança.");

        var sessions = snapshot.Sessions.Select(session => session.Copy()).ToList();
        sessions.RemoveAll(session => session.Origin == "plan" && !session.IsCompleted &&
                                      string.CompareOrdinal(session.Date, snapshot.SnapshotDate) >= 0);
        sessions.AddRange(eligible.Select(session => session.Copy()));
        var settings = StudyRepository.CopySettings(snapshot.Settings);
        settings.ActivePlanId = package.PlanId;
        settings.ActivePlanRevision = package.Revision;
        settings.ActivePlanTitle = package.Title;
        settings.ObjectiveName = package.ObjectiveName;
        settings.ObjectiveDate = package.ObjectiveDate;
        settings.PlanRevisions[package.PlanId] = package.Revision;
        return (settings, sessions);
    }

    private static (AppSettings Settings, IReadOnlyList<SessionItem> Sessions) BuildPlanChanges(
        RepositoryApplicationSnapshot snapshot,
        AiProposal proposal,
        AiProposalPreview preview)
    {
        var settings = StudyRepository.CopySettings(snapshot.Settings);
        var sessions = snapshot.Sessions.Select(session => session.Copy()).ToList();
        var previewToActualIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var operation in proposal.Changes!.Operations)
        {
            switch (operation.Type)
            {
                case AiPlanOperationType.MoveSession:
                {
                    var session = ResolveEditableSession(sessions, operation.SessionId, snapshot.SnapshotDate);
                    session.Date = operation.DestinationDate;
                    break;
                }
                case AiPlanOperationType.AddSession:
                {
                    if (settings.ActivePlanId.Length == 0 || settings.ActivePlanRevision < 1 ||
                        string.IsNullOrWhiteSpace(operation.Subject) || operation.Subject.Length > 80 ||
                        operation.Minutes is not int minutes || minutes is < 10 or > 360)
                    {
                        throw new InvalidDataException("A nova sessão não possui dados seguros para aplicação.");
                    }
                    var actualId = $"ai-{proposal.Id:N}-{operation.Id:N}";
                    var previewId = $"preview-{operation.Id:N}";
                    if (sessions.Any(session => session.PlanId == settings.ActivePlanId && session.Id == actualId))
                        throw new InvalidDataException("A nova sessão já existe no calendário.");
                    sessions.Add(new SessionItem
                    {
                        Id = actualId,
                        PlanId = settings.ActivePlanId,
                        PlanRevision = settings.ActivePlanRevision,
                        Date = operation.DestinationDate,
                        Subject = operation.Subject.Trim(),
                        Topic = "Sessão adicionada pelo Assistente IA",
                        Minutes = minutes,
                        Target = SafeTarget(operation.Summary),
                        Kind = "study",
                        Status = "planned",
                        Origin = "plan"
                    });
                    previewToActualIds[previewId] = actualId;
                    break;
                }
                case AiPlanOperationType.RemoveFutureSession:
                {
                    var session = ResolveEditableSession(sessions, operation.SessionId, snapshot.SnapshotDate);
                    sessions.Remove(session);
                    break;
                }
                case AiPlanOperationType.ChangeSubjectPriority:
                {
                    if (operation.Priority is not int priority)
                        throw new InvalidDataException("A prioridade proposta é inválida.");
                    var canonicalSubject = sessions
                        .Where(session => !session.IsCompleted && string.CompareOrdinal(session.Date, snapshot.SnapshotDate) >= 0)
                        .Select(session => session.Subject)
                        .FirstOrDefault(subject => string.Equals(subject, operation.Subject, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("A matéria priorizada não existe na agenda futura.");
                    var previousKey = settings.SubjectPriorities.Keys.FirstOrDefault(key =>
                        string.Equals(key, canonicalSubject, StringComparison.OrdinalIgnoreCase));
                    if (previousKey is not null) settings.SubjectPriorities.Remove(previousKey);
                    settings.SubjectPriorities[canonicalSubject] = priority;
                    break;
                }
                case AiPlanOperationType.SetAvailability:
                    ApplyAvailability(settings, operation);
                    break;
                case AiPlanOperationType.RedistributeLoad:
                {
                    ApplyAvailability(settings, operation);
                    foreach (var movement in preview.Operations.Where(item =>
                                 item.OperationId == operation.Id && item.Type == AiPlanOperationType.MoveSession))
                    {
                        var sessionId = previewToActualIds.TryGetValue(movement.SessionId, out var mapped)
                            ? mapped
                            : movement.SessionId;
                        var session = ResolveEditableSession(sessions, sessionId, snapshot.SnapshotDate);
                        session.Date = movement.ProposedDate;
                    }
                    break;
                }
                case AiPlanOperationType.RebuildFuturePlan:
                    throw new InvalidDataException("Reconstruir exige uma proposta StudyPlan detalhada.");
                default:
                    throw new InvalidDataException("A proposta contém uma operação não suportada.");
            }
        }
        return (settings, sessions);
    }

    private static SessionItem ResolveEditableSession(List<SessionItem> sessions, string sessionId, string today)
    {
        var matches = sessions.Where(session =>
                session.Id == sessionId && !session.IsCompleted && string.CompareOrdinal(session.Date, today) >= 0)
            .Take(2)
            .ToList();
        var result = matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException("A sessão indicada não existe mais na agenda futura."),
            _ => throw new InvalidDataException("O ID da sessão é ambíguo entre planos.")
        };
        if (result.Origin == "runtime")
            throw new InvalidDataException("Uma revisão automática protegida não pode ser alterada diretamente.");
        return result;
    }

    private static void ApplyAvailability(AppSettings settings, AiPlanOperation operation)
    {
        if (operation.MaxHoursPerDay is not double hours || !double.IsFinite(hours) || hours is < 1 or > 12 ||
            operation.AvailableDays.Count == 0)
        {
            throw new InvalidDataException("A disponibilidade proposta é inválida.");
        }
        settings.DailyHours = Math.Round(hours * 60, MidpointRounding.AwayFromZero) / 60d;
        settings.AvailableStudyDays = operation.AvailableDays.Distinct().OrderBy(day => day).ToList();
    }

    private static string SafeTarget(string value)
    {
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return "Concluir o bloco proposto pelo Assistente IA.";
        return normalized.Length <= 180 ? normalized : normalized[..180].TrimEnd();
    }

    private static AiProposalPreview Blocked(
        AiProposal proposal,
        string message,
        RepositoryApplicationSnapshot snapshot)
    {
        var future = snapshot.Sessions
            .Where(session => !session.IsCompleted && string.CompareOrdinal(session.Date, snapshot.SnapshotDate) >= 0)
            .ToList();
        return new AiProposalPreview
        {
            ProposalId = proposal.Id,
            Kind = proposal.Kind,
            State = AiProposalPreviewState.Blocked,
            Message = string.IsNullOrWhiteSpace(message) ? "A proposta não passou na verificação final." : message,
            BeforeSessionCount = future.Count,
            AfterSessionCount = future.Count,
            BeforeMinutes = future.Sum(session => session.Minutes),
            AfterMinutes = future.Sum(session => session.Minutes),
            Warnings = proposal.Warnings.ToList()
        };
    }

    private bool PreviewEquals(AiProposalPreview left, AiProposalPreview right) =>
        JsonSerializer.Serialize(left, _canonicalJson) == JsonSerializer.Serialize(right, _canonicalJson);

    private string ProposalHash(AiProposal proposal) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(proposal, _canonicalJson)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _prepared = null;
        _gate.Dispose();
    }

    private sealed record PreparedWork(
        Guid ConfirmationId,
        Guid ProposalId,
        string ProposalHash,
        long ExpectedMutationVersion,
        string ExpectedSnapshotDate,
        AppSettings NextSettings,
        IReadOnlyList<SessionItem> NextSessions);
}
