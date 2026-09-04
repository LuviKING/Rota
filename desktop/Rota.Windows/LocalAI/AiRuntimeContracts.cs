namespace Rota.Desktop.LocalAI;

public enum AiRuntimeState
{
    Stopped,
    Starting,
    Ready,
    Stopping,
    Faulted
}

public sealed record AiRuntimeStatus(
    AiRuntimeState State,
    Uri? Endpoint,
    int? ProcessId,
    AiProfile? EffectiveProfile,
    AiComputePreference? ComputePreference,
    string Message);

public sealed record AiRuntimeLaunchCommand(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

public sealed record AiInstallationIntegrityResult(
    string InstallationDirectory,
    AiComputePreference RuntimePreference);

public sealed class AiRuntimeConnection
{
    public Uri Endpoint { get; }
    public string ApiKey { get; }

    public AiRuntimeConnection(Uri endpoint, string apiKey)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        ApiKey = !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey
            : throw new ArgumentException("A conexão local exige uma chave efêmera.", nameof(apiKey));
    }

    public override string ToString() => Endpoint.ToString();
}

public interface IAiInstallationIntegrityVerifier
{
    Task<AiInstallationIntegrityResult> VerifyAsync(
        AiConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public interface IAiRuntimeProcess : IAsyncDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    string RecentOutput { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
    void Kill(bool entireProcessTree);
}

public interface IAiRuntimeProcessFactory
{
    IAiRuntimeProcess Start(AiRuntimeLaunchCommand command);
}

public interface IAiRuntimeHealthClient
{
    Task<bool> IsHealthyAsync(Uri endpoint, CancellationToken cancellationToken = default);
}

public interface IAiLoopbackPortAllocator
{
    int GetAvailablePort();
}

public interface ILocalAiRuntimeHost : IAsyncDisposable
{
    AiRuntimeStatus Status { get; }
    AiRuntimeConnection? Connection { get; }

    Task<AiRuntimeStatus> StartAsync(
        AiConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class AiRuntimeException : Exception
{
    public AiRuntimeException(string message) : base(message)
    {
    }

    public AiRuntimeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
