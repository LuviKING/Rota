namespace Rota.Desktop.LocalAI;

public enum AiInstallationStage
{
    Preparing,
    DownloadingRuntime,
    VerifyingRuntime,
    ExtractingRuntime,
    DownloadingModel,
    VerifyingModel,
    Activating,
    Completed
}

public sealed record AiDownloadArtifact(
    string Id,
    Uri Source,
    string FileName,
    long ExpectedSizeBytes,
    string Sha256);

public sealed record AiRuntimePackage(
    AiComputePreference ComputePreference,
    AiDownloadArtifact Archive,
    string ServerRelativePath);

public sealed record AiModelPackage(
    string ModelId,
    AiDownloadArtifact Artifact);

public sealed record AiDownloadProgress(
    long BytesReceived,
    long? TotalBytes);

public sealed record AiInstallationProgress(
    AiInstallationStage Stage,
    string ArtifactId,
    long BytesReceived,
    long? TotalBytes);

public sealed record AiInstallationResult(
    AiConfiguration Configuration,
    AiModelInstallationInfo InstallationInfo,
    string InstallationDirectory);

public sealed record AiInstallationReceipt
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int ManifestVersion { get; init; }
    public DateTimeOffset InstalledAtUtc { get; init; }
    public string RuntimeArtifactId { get; init; } = "";
    public string RuntimeSha256 { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string ModelArtifactId { get; init; } = "";
    public string ModelSha256 { get; init; } = "";
}

public interface IAiArtifactDownloader
{
    Task DownloadAsync(
        Uri source,
        string destinationPath,
        long expectedSizeBytes,
        IProgress<AiDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IAiInstaller
{
    Task<AiInstallationResult> InstallAsync(
        AiConfiguration configuration,
        IProgress<AiInstallationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class AiInstallationException : Exception
{
    public AiInstallationException(string message) : base(message)
    {
    }

    public AiInstallationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
