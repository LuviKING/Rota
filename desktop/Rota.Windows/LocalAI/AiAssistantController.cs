namespace Rota.Desktop.LocalAI;

public sealed class AiAssistantController : IAiAssistantController, IAsyncDisposable
{
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiModelManager _modelManager;
    private readonly IAiProposalStore _proposalStore;
    private readonly IAiProposalWorkflowService _workflow;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateSync = new();
    private CancellationTokenSource? _activeOperation;
    private AiAssistantState _state = new();
    private bool _disposed;

    public AiAssistantController(
        IAiConfigurationStore configurationStore,
        IAiModelManager modelManager,
        IAiProposalStore proposalStore,
        IAiProposalWorkflowService workflow)
    {
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    public AiAssistantState State
    {
        get { lock (_stateSync) return _state; }
    }

    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var operation = await BeginOperationAsync(AiAssistantActivity.Loading, cancellationToken).ConfigureAwait(false);
        try
        {
            var configuration = await _configurationStore.LoadAsync(operation.Token).ConfigureAwait(false);
            var installation = await _modelManager
                .GetInstallationInfoAsync(configuration, operation.Token)
                .ConfigureAwait(false);
            var history = await _proposalStore.LoadAsync(operation.Token).ConfigureAwait(false);
            var warnings = installation.Warnings.ToList();
            AddWarning(warnings, _configurationStore.LastLoadWarning);
            AddWarning(warnings, _proposalStore.LastLoadWarning);
            UpdateState(new AiAssistantState
            {
                IsInitialized = true,
                Activity = AiAssistantActivity.Idle,
                EffectiveProfile = installation.EffectiveProfile,
                InstallationState = installation.State,
                StatusMessage = InstallationMessage(installation.State),
                Warnings = warnings.TakeLast(50).ToList(),
                History = history.ToList()
            });
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            UpdateState(State with
            {
                Activity = AiAssistantActivity.Idle,
                StatusMessage = "Carregamento do assistente cancelado."
            });
        }
        catch (Exception ex)
        {
            SetError("O estado da IA local não pôde ser carregado.", ex);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public async Task<AiStoredProposal?> SendAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = State;
        if (!current.IsInitialized)
        {
            SetError("Abra o assistente e aguarde o carregamento antes de enviar.");
            return null;
        }
        if (!current.IsOfflineReady)
        {
            SetError("Instale o runtime e o modelo local antes de gerar uma proposta.");
            return null;
        }

        var operation = await BeginOperationAsync(AiAssistantActivity.Generating, cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await _workflow.PrepareAsync(input, kind, operation.Token).ConfigureAwait(false);
            var history = State.History
                .Where(item => item.Proposal.Id != stored.Proposal.Id)
                .Prepend(stored)
                .Take(100)
                .ToList();
            UpdateState(State with
            {
                Activity = AiAssistantActivity.Idle,
                StatusMessage = stored.Preview.CanProceed
                    ? "Proposta pronta para revisão. Nada foi aplicado."
                    : "A proposta foi bloqueada pela validação. Nada foi aplicado.",
                History = history
            });
            return stored;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            UpdateState(State with
            {
                Activity = AiAssistantActivity.Idle,
                StatusMessage = "Geração cancelada. Nenhuma proposta parcial foi salva."
            });
            return null;
        }
        catch (Exception ex)
        {
            SetError("A proposta local não pôde ser preparada.", ex);
            return null;
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public void CancelCurrentOperation()
    {
        lock (_stateSync)
        {
            try { _activeOperation?.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public AiAssistantInput ApplyQuickAction(AiAssistantInput input, AiAssistantQuickAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        var instruction = action switch
        {
            AiAssistantQuickAction.BuildPlan => "Monte um plano de estudos completo com base nestas informações.",
            AiAssistantQuickAction.ReorganizeWeek => "Reorganize somente as sessões futuras desta semana.",
            AiAssistantQuickAction.AdjustLoad => "Ajuste a carga futura sem ultrapassar minha disponibilidade diária.",
            AiAssistantQuickAction.PrepareForEnem => "Prepare um plano equilibrado para o ENEM, priorizando minhas dificuldades.",
            AiAssistantQuickAction.ReviewDelays => "Revise as sessões futuras e proponha como recuperar os atrasos com segurança.",
            _ => throw new AiContractValidationException("A ação rápida do assistente é inválida.")
        };
        var freeText = string.IsNullOrWhiteSpace(input.FreeText)
            ? instruction
            : input.FreeText.TrimEnd() + Environment.NewLine + instruction;
        var result = input with { FreeText = freeText };
        AiContractValidator.ValidateInput(result);
        return result;
    }

    public AiProposalKind SuggestedKind(AiAssistantQuickAction action) => action switch
    {
        AiAssistantQuickAction.BuildPlan or AiAssistantQuickAction.PrepareForEnem => AiProposalKind.StudyPlan,
        AiAssistantQuickAction.ReorganizeWeek or AiAssistantQuickAction.AdjustLoad or
            AiAssistantQuickAction.ReviewDelays => AiProposalKind.PlanChanges,
        _ => throw new AiContractValidationException("A ação rápida do assistente é inválida.")
    };

    private async Task<CancellationTokenSource> BeginOperationAsync(
        AiAssistantActivity activity,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_stateSync) _activeOperation = operation;
            UpdateState(State with
            {
                Activity = activity,
                StatusMessage = activity == AiAssistantActivity.Loading
                    ? "Carregando IA local…"
                    : "Gerando proposta local…"
            });
            return operation;
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        lock (_stateSync)
        {
            if (ReferenceEquals(_activeOperation, operation)) _activeOperation = null;
        }
        operation.Dispose();
        _operationGate.Release();
    }

    private void SetError(string message, Exception? exception = null)
    {
        var warnings = State.Warnings.ToList();
        if (exception is not null)
            warnings.Add(exception is AiProposalWorkflowException or AiPlanningException
                ? exception.Message
                : "O Rota conteve uma falha interna da IA local.");
        UpdateState(State with
        {
            Activity = AiAssistantActivity.Error,
            StatusMessage = message,
            Warnings = warnings.TakeLast(50).ToList()
        });
    }

    private void UpdateState(AiAssistantState state)
    {
        lock (_stateSync) _state = state;
        var handlers = StateChanged;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); } catch { }
        }
    }

    private static string InstallationMessage(AiInstallationState state) => state switch
    {
        AiInstallationState.Ready => "IA local pronta e offline.",
        AiInstallationState.RuntimeInstalled => "Runtime instalado; o modelo local ainda está ausente.",
        _ => "IA local ainda não instalada."
    };

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning)) warnings.Add(warning);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCurrentOperation();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateSync)
            {
                _activeOperation?.Dispose();
                _activeOperation = null;
            }
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }
}
