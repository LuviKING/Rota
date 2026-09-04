using System.Globalization;
using System.Security.Cryptography;

namespace Rota.Desktop.LocalAI;

public sealed class LocalAiRuntimeHost : ILocalAiRuntimeHost
{
    private static readonly AiRuntimeStatus StoppedStatus = new(
        AiRuntimeState.Stopped, null, null, null, null, "Runtime local parado.");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusSync = new();
    private readonly IAiModelManager _modelManager;
    private readonly IAiInstallationIntegrityVerifier _integrityVerifier;
    private readonly IAiRuntimeProcessFactory _processFactory;
    private readonly IAiRuntimeHealthClient _healthClient;
    private readonly IAiLoopbackPortAllocator _portAllocator;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _stopTimeout;
    private readonly bool _ownsHealthClient;
    private IAiRuntimeProcess? _process;
    private AiRuntimeStatus _status = StoppedStatus;
    private AiRuntimeConnection? _connection;
    private bool _disposed;

    public LocalAiRuntimeHost(
        IAiModelManager modelManager,
        IAiInstallationIntegrityVerifier? integrityVerifier = null,
        IAiRuntimeProcessFactory? processFactory = null,
        IAiRuntimeHealthClient? healthClient = null,
        IAiLoopbackPortAllocator? portAllocator = null,
        TimeSpan? startupTimeout = null,
        TimeSpan? pollInterval = null,
        TimeSpan? stopTimeout = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _integrityVerifier = integrityVerifier ?? new AiInstallationIntegrityVerifier();
        _processFactory = processFactory ?? new SystemAiRuntimeProcessFactory();
        _healthClient = healthClient ?? new LlamaServerHealthClient();
        _ownsHealthClient = healthClient is null;
        _portAllocator = portAllocator ?? new AiLoopbackPortAllocator();
        _startupTimeout = startupTimeout ?? TimeSpan.FromMinutes(3);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(10);
        ValidateTimeout(_startupTimeout, nameof(startupTimeout));
        ValidateTimeout(_pollInterval, nameof(pollInterval));
        ValidateTimeout(_stopTimeout, nameof(stopTimeout));
    }

    public AiRuntimeStatus Status
    {
        get
        {
            lock (_statusSync)
            {
                if (_process is not null &&
                    (_status.State is AiRuntimeState.Starting or AiRuntimeState.Ready) &&
                    ProcessHasExited(_process))
                {
                    _status = _status with
                    {
                        State = AiRuntimeState.Faulted,
                        Message = BuildUnexpectedExitMessage(_process)
                    };
                    _connection = null;
                }
                return _status;
            }
        }
    }

    public AiRuntimeConnection? Connection
    {
        get
        {
            lock (_statusSync)
                return Status.State == AiRuntimeState.Ready ? _connection : null;
        }
    }

    public async Task<AiRuntimeStatus> StartAsync(
        AiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AiContractValidator.ValidateConfiguration(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is not null && !ProcessHasExited(_process))
                return Status;

            if (_process is not null)
            {
                await _process.DisposeAsync().ConfigureAwait(false);
                _process = null;
                ClearConnection();
            }

            var installation = await _modelManager.GetInstallationInfoAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            if (installation.State != AiInstallationState.Ready ||
                !installation.RuntimeAvailable || !installation.ModelAvailable)
            {
                throw new AiRuntimeException("A IA local precisa estar completamente instalada antes de iniciar.");
            }

            var integrity = await _integrityVerifier.VerifyAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            int port;
            try
            {
                port = _portAllocator.GetAvailablePort();
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
            {
                throw new AiRuntimeException("Não foi possível encontrar uma porta local para a IA.", ex);
            }
            if (port is < 1024 or > 65535)
                throw new AiRuntimeException("Não foi possível reservar uma porta local segura para a IA.");

            var endpoint = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
            var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var command = BuildCommand(configuration, installation, integrity.RuntimePreference, port, apiKey);
            IAiRuntimeProcess process;
            try
            {
                process = _processFactory.Start(command);
            }
            catch (AiRuntimeException ex)
            {
                SetFaulted(ex.Message);
                throw;
            }
            _process = process;
            ClearConnection();
            SetStatus(new AiRuntimeStatus(
                AiRuntimeState.Starting,
                endpoint,
                process.Id,
                installation.EffectiveProfile,
                integrity.RuntimePreference,
                "Carregando o modelo local."));

            using var timeout = new CancellationTokenSource(_startupTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                while (true)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (ProcessHasExited(process))
                        throw new AiRuntimeException(BuildUnexpectedExitMessage(process));
                    if (await _healthClient.IsHealthyAsync(endpoint, linked.Token).ConfigureAwait(false))
                    {
                        var ready = new AiRuntimeStatus(
                            AiRuntimeState.Ready,
                            endpoint,
                            process.Id,
                            installation.EffectiveProfile,
                            integrity.RuntimePreference,
                            "IA local pronta.");
                        lock (_statusSync)
                        {
                            _connection = new AiRuntimeConnection(endpoint, apiKey);
                            _status = ready;
                        }
                        return ready;
                    }
                    await Task.Delay(_pollInterval, linked.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupFailedStartAsync(process).ConfigureAwait(false);
                SetStatus(StoppedStatus with { Message = "Inicialização da IA local cancelada." });
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                await CleanupFailedStartAsync(process).ConfigureAwait(false);
                var failure = "A IA local não ficou pronta dentro do tempo limite.";
                SetFaulted(failure);
                throw new AiRuntimeException(failure);
            }
            catch (Exception ex)
            {
                await CleanupFailedStartAsync(process).ConfigureAwait(false);
                var failure = ex is AiRuntimeException
                    ? ex.Message
                    : "O runtime local falhou durante a inicialização.";
                SetFaulted(failure);
                if (ex is AiRuntimeException) throw;
                throw new AiRuntimeException(failure, ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = _process;
            if (process is null)
            {
                ClearConnection();
                SetStatus(StoppedStatus);
                return;
            }

            SetStatus(Status with { State = AiRuntimeState.Stopping, Message = "Encerrando a IA local." });
            try
            {
                await TerminateProcessAsync(process).ConfigureAwait(false);
                _process = null;
                ClearConnection();
                SetStatus(StoppedStatus);
            }
            catch (Exception ex)
            {
                ClearConnection();
                const string failure = "O runtime local não pôde ser encerrado com segurança.";
                SetFaulted(failure);
                throw new AiRuntimeException(failure, ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AiRuntimeLaunchCommand BuildCommand(
        AiConfiguration configuration,
        AiModelInstallationInfo installation,
        AiComputePreference runtimePreference,
        int port,
        string apiKey)
    {
        if (runtimePreference is not (AiComputePreference.Cpu or AiComputePreference.Gpu))
            throw new AiRuntimeException("A instalação não identifica um runtime CPU ou GPU válido.");

        var arguments = new[]
        {
            "--model", installation.ModelPath,
            "--ctx-size", configuration.ContextSize.ToString(CultureInfo.InvariantCulture),
            "--host", "127.0.0.1",
            "--port", port.ToString(CultureInfo.InvariantCulture),
            "--parallel", "1",
            "--n-gpu-layers", runtimePreference == AiComputePreference.Gpu ? "all" : "0",
            "--cors-origins", "localhost",
            "--api-key", apiKey
        };
        return new AiRuntimeLaunchCommand(
            installation.RuntimePath,
            Path.GetDirectoryName(installation.RuntimePath)!,
            Array.AsReadOnly(arguments));
    }

    private async Task TerminateProcessAsync(IAiRuntimeProcess process)
    {
        if (!ProcessHasExited(process))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (ProcessHasExited(process))
            {
                // The process exited between the state check and Kill.
            }
        }
        using var timeout = new CancellationTokenSource(_stopTimeout);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        await process.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CleanupFailedStartAsync(IAiRuntimeProcess process)
    {
        try
        {
            await TerminateProcessAsync(process).ConfigureAwait(false);
            _process = null;
            ClearConnection();
        }
        catch (Exception ex)
        {
            ClearConnection();
            const string failure = "O runtime local falhou e não pôde ser encerrado com segurança.";
            SetFaulted(failure);
            throw new AiRuntimeException(failure, ex);
        }
    }

    private static bool ProcessHasExited(IAiRuntimeProcess process)
    {
        try { return process.HasExited; }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { return true; }
    }

    private static string BuildUnexpectedExitMessage(IAiRuntimeProcess process)
    {
        var exit = process.ExitCode is { } exitCode ? $" (código {exitCode})" : "";
        var output = new string(process.RecentOutput
            .Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t')
            .ToArray()).Trim();
        if (output.Length > 500) output = output[^500..];
        return output.Length == 0
            ? $"O runtime local encerrou inesperadamente{exit}."
            : $"O runtime local encerrou inesperadamente{exit}: {output}";
    }

    private void SetFaulted(string message) => SetStatus(new AiRuntimeStatus(
        AiRuntimeState.Faulted, null, null, null, null, message));

    private void SetStatus(AiRuntimeStatus status)
    {
        lock (_statusSync) _status = status;
    }

    private void ClearConnection()
    {
        lock (_statusSync) _connection = null;
    }

    private static void ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var process = _process;
            if (process is not null)
            {
                await TerminateProcessAsync(process).ConfigureAwait(false);
                _process = null;
            }
            ClearConnection();
            SetStatus(StoppedStatus);
            _disposed = true;
            if (_ownsHealthClient && _healthClient is IDisposable disposable) disposable.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }
}
