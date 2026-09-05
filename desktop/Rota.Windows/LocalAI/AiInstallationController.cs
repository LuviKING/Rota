namespace Rota.Desktop.LocalAI;

public enum AiInstallationActivity
{
    Idle,
    Analyzing,
    Installing,
    Completed,
    Error,
    Cancelled
}

public sealed record AiPreparedInstallation
{
    public Guid ConfirmationId { get; init; }
    public AiProfile RequestedProfile { get; init; }
    public AiProfile EffectiveProfile { get; init; }
    public AiComputePreference ComputePreference { get; init; }
    public string ModelName { get; init; } = "";
    public long DownloadBytes { get; init; }
    public long RecommendedFreeBytes { get; init; }
    public string InstallationDirectory { get; init; } = "";
}

public sealed record AiInstallationUiState
{
    public AiInstallationActivity Activity { get; init; }
    public string StatusMessage { get; init; } = "Escolha um perfil para analisar a instalação.";
    public AiPreparedInstallation? Plan { get; init; }
    public AiInstallationStage? Stage { get; init; }
    public long BytesReceived { get; init; }
    public long TotalDownloadBytes { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsBusy => Activity is AiInstallationActivity.Analyzing or AiInstallationActivity.Installing;
    public bool CanInstall => Activity == AiInstallationActivity.Idle && Plan is not null;
    public double ProgressFraction => TotalDownloadBytes <= 0
        ? 0
        : Math.Clamp((double)BytesReceived / TotalDownloadBytes, 0, 1);
}

public interface IAiInstallationController
{
    AiInstallationUiState State { get; }
    event EventHandler? StateChanged;

    Task<AiPreparedInstallation?> PrepareAsync(
        AiProfile profile,
        CancellationToken cancellationToken = default);
    Task<AiInstallationResult?> InstallAsync(
        Guid confirmationId,
        CancellationToken cancellationToken = default);
    void CancelCurrentOperation();
}

public sealed class AiInstallationController : IAiInstallationController, IAsyncDisposable
{
    private const long ExtractionAndReserveBytes = (2L * 1024 + 128) * 1024 * 1024;
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiModelManager _modelManager;
    private readonly IAiInstaller _installer;
    private readonly AiInstallationManifest _manifest;
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateSync = new();
    private CancellationTokenSource? _activeOperation;
    private PreparedOperation? _prepared;
    private AiInstallationUiState _state = new();
    private bool _disposed;

    public AiInstallationController(
        string rootDirectory,
        IAiConfigurationStore configurationStore,
        IAiModelManager modelManager,
        IAiInstaller installer,
        AiInstallationManifest? manifest = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
            throw new AiContractValidationException("O diretório da instalação guiada precisa ser absoluto.");
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _manifest = manifest ?? AiInstallationManifest.Default;
    }

    public AiInstallationUiState State
    {
        get { lock (_stateSync) return _state; }
    }

    public event EventHandler? StateChanged;

    public async Task<AiPreparedInstallation?> PrepareAsync(
        AiProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(profile))
        {
            ClearPrepared();
            SetError("O perfil selecionado não é válido.");
            return null;
        }

        ClearPrepared();
        var operation = await BeginOperationAsync(AiInstallationActivity.Analyzing, cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _configurationStore.LoadAsync(operation.Token).ConfigureAwait(false);
            var requested = existing with
            {
                Profile = profile,
                ModelId = "",
                ModelPath = "",
                RuntimePath = "",
                ComputePreference = AiComputePreference.Automatic,
                InstallationState = AiInstallationState.NotInstalled
            };
            var selection = await _modelManager.GetInstallationInfoAsync(requested, operation.Token).ConfigureAwait(false);
            var compute = selection.EffectiveProfile == AiProfile.Performance
                ? AiComputePreference.Gpu
                : AiComputePreference.Cpu;
            var runtime = _manifest.GetRuntimePackage(compute);
            var model = _manifest.GetModelPackage(selection.Model.Id);
            var downloadBytes = checked(runtime.Archive.ExpectedSizeBytes + model.Artifact.ExpectedSizeBytes);
            var configuration = requested with { ContextSize = selection.Model.DefaultContextSize };
            var plan = new AiPreparedInstallation
            {
                ConfirmationId = Guid.NewGuid(),
                RequestedProfile = profile,
                EffectiveProfile = selection.EffectiveProfile,
                ComputePreference = compute,
                ModelName = selection.Model.DisplayName,
                DownloadBytes = downloadBytes,
                RecommendedFreeBytes = checked(downloadBytes + ExtractionAndReserveBytes),
                InstallationDirectory = _rootDirectory
            };

            lock (_stateSync)
            {
                _prepared = new PreparedOperation(
                    plan,
                    configuration,
                    runtime.Archive.ExpectedSizeBytes,
                    model.Artifact.ExpectedSizeBytes);
            }
            UpdateState(new AiInstallationUiState
            {
                Activity = AiInstallationActivity.Idle,
                StatusMessage = "Instalação analisada. Confira os detalhes antes de continuar.",
                Plan = plan,
                TotalDownloadBytes = downloadBytes,
                Warnings = selection.Warnings.TakeLast(20).ToList()
            });
            return plan;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            ClearPrepared();
            UpdateState(State with
            {
                Activity = AiInstallationActivity.Cancelled,
                StatusMessage = "Análise cancelada. Nenhum download foi iniciado.",
                Plan = null
            });
            return null;
        }
        catch (Exception ex)
        {
            ClearPrepared();
            SetError("Não foi possível analisar a instalação local.", ex);
            return null;
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public async Task<AiInstallationResult?> InstallAsync(
        Guid confirmationId,
        CancellationToken cancellationToken = default)
    {
        PreparedOperation? prepared;
        lock (_stateSync)
        {
            prepared = _prepared?.Plan.ConfirmationId == confirmationId ? _prepared : null;
            if (prepared is not null) _prepared = null;
        }
        if (prepared is null)
        {
            ClearPrepared();
            SetError("A confirmação da instalação expirou. Analise o perfil novamente.");
            return null;
        }

        var operation = await BeginOperationAsync(AiInstallationActivity.Installing, cancellationToken).ConfigureAwait(false);
        try
        {
            var progress = new InlineProgress(value => ReportProgress(prepared, value));
            var result = await _installer
                .InstallAsync(prepared.Configuration, progress, operation.Token)
                .ConfigureAwait(false);
            if (result.InstallationInfo.State != AiInstallationState.Ready)
                throw new AiInstallationException("A instalação terminou sem deixar a IA local pronta.");

            UpdateState(State with
            {
                Activity = AiInstallationActivity.Completed,
                StatusMessage = "IA local instalada e verificada. Você já pode gerar prévias offline.",
                Plan = prepared.Plan,
                Stage = AiInstallationStage.Completed,
                BytesReceived = prepared.Plan.DownloadBytes,
                TotalDownloadBytes = prepared.Plan.DownloadBytes
            });
            return result;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            UpdateState(State with
            {
                Activity = AiInstallationActivity.Cancelled,
                StatusMessage = "Instalação cancelada. Nenhuma instalação incompleta foi ativada.",
                Plan = null,
                Stage = null
            });
            return null;
        }
        catch (Exception ex)
        {
            SetError("A instalação local não pôde ser concluída.", ex);
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

    private async Task<CancellationTokenSource> BeginOperationAsync(
        AiInstallationActivity activity,
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
                StatusMessage = activity == AiInstallationActivity.Analyzing
                    ? "Analisando perfil e tamanho da instalação…"
                    : "Preparando a instalação local…",
                Stage = activity == AiInstallationActivity.Installing ? AiInstallationStage.Preparing : null,
                BytesReceived = 0,
                Plan = activity == AiInstallationActivity.Analyzing ? null : State.Plan
            });
            return operation;
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
    }

    private void ReportProgress(PreparedOperation prepared, AiInstallationProgress progress)
    {
        var received = progress.Stage switch
        {
            AiInstallationStage.DownloadingRuntime => progress.BytesReceived,
            AiInstallationStage.VerifyingRuntime or AiInstallationStage.ExtractingRuntime => prepared.RuntimeBytes,
            AiInstallationStage.DownloadingModel => prepared.RuntimeBytes + progress.BytesReceived,
            AiInstallationStage.VerifyingModel or AiInstallationStage.Activating or AiInstallationStage.Completed =>
                prepared.RuntimeBytes + prepared.ModelBytes,
            _ => 0
        };
        UpdateState(State with
        {
            Activity = progress.Stage == AiInstallationStage.Completed
                ? AiInstallationActivity.Completed
                : AiInstallationActivity.Installing,
            StatusMessage = StageMessage(progress.Stage),
            Stage = progress.Stage,
            BytesReceived = Math.Clamp(received, 0, prepared.Plan.DownloadBytes),
            TotalDownloadBytes = prepared.Plan.DownloadBytes
        });
    }

    private void SetError(string message, Exception? exception = null)
    {
        var warnings = State.Warnings.ToList();
        if (exception is AiInstallationException or AiContractValidationException)
            warnings.Add(exception.Message);
        else if (exception is not null)
            warnings.Add("O Rota conteve uma falha interna durante a instalação.");
        UpdateState(State with
        {
            Activity = AiInstallationActivity.Error,
            StatusMessage = message,
            Plan = null,
            Stage = null,
            Warnings = warnings.TakeLast(20).ToList()
        });
    }

    private void ClearPrepared()
    {
        lock (_stateSync) _prepared = null;
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

    private void UpdateState(AiInstallationUiState state)
    {
        lock (_stateSync) _state = state;
        var handlers = StateChanged;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); } catch { }
        }
    }

    private static string StageMessage(AiInstallationStage stage) => stage switch
    {
        AiInstallationStage.DownloadingRuntime => "Baixando o motor local…",
        AiInstallationStage.VerifyingRuntime => "Verificando o motor local…",
        AiInstallationStage.ExtractingRuntime => "Preparando o motor local…",
        AiInstallationStage.DownloadingModel => "Baixando o modelo de IA…",
        AiInstallationStage.VerifyingModel => "Verificando o modelo de IA…",
        AiInstallationStage.Activating => "Ativando a instalação verificada…",
        AiInstallationStage.Completed => "Instalação concluída e verificada.",
        _ => "Preparando a instalação local…"
    };

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
                _prepared = null;
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

    private sealed record PreparedOperation(
        AiPreparedInstallation Plan,
        AiConfiguration Configuration,
        long RuntimeBytes,
        long ModelBytes);

    private sealed class InlineProgress : IProgress<AiInstallationProgress>
    {
        private readonly Action<AiInstallationProgress> _report;
        public InlineProgress(Action<AiInstallationProgress> report) => _report = report;
        public void Report(AiInstallationProgress value) => _report(value);
    }
}
