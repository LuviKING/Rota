namespace Rota.Desktop.LocalAI;

public sealed record AiStorageSnapshot(
    string RootPath,
    long? TotalBytes,
    long? AvailableBytes,
    string Warning = "");

public sealed record AiHardwareDiagnosticReport(
    AiHardwareProfile Hardware,
    AiStorageSnapshot Storage,
    DateTimeOffset CapturedAtUtc)
{
    public IReadOnlyList<string> Warnings
    {
        get
        {
            var warnings = Hardware.Warnings.ToList();
            if (!string.IsNullOrWhiteSpace(Storage.Warning)) warnings.Add(Storage.Warning);
            return warnings;
        }
    }
}

public interface IAiStorageProbe
{
    Task<AiStorageSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IAiHardwareDiagnosticsService
{
    Task<AiHardwareDiagnosticReport> AnalyzeAsync(CancellationToken cancellationToken = default);
}

public sealed class AiHardwareDiagnosticsService : IAiHardwareDiagnosticsService
{
    private readonly IAiHardwareProfileDetector _hardwareDetector;
    private readonly IAiStorageProbe _storageProbe;
    private readonly Func<DateTimeOffset> _utcNow;

    public AiHardwareDiagnosticsService(
        IAiHardwareProfileDetector? hardwareDetector = null,
        IAiStorageProbe? storageProbe = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _hardwareDetector = hardwareDetector ?? new WindowsAiHardwareProfileDetector();
        _storageProbe = storageProbe ?? new WindowsAiStorageProbe(DefaultAiRootDirectory());
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AiHardwareDiagnosticReport> AnalyzeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hardwareTask = _hardwareDetector.DetectAsync(cancellationToken);
        var storageTask = _storageProbe.CaptureAsync(cancellationToken);
        await Task.WhenAll(hardwareTask, storageTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var hardware = await hardwareTask.ConfigureAwait(false);
        var storage = await storageTask.ConfigureAwait(false);
        Validate(hardware, storage);
        return new AiHardwareDiagnosticReport(hardware, storage, _utcNow());
    }

    private static void Validate(AiHardwareProfile hardware, AiStorageSnapshot storage)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(storage);
        if (hardware.LogicalProcessorCount <= 0 || hardware.SystemMemoryBytes <= 0 ||
            hardware.DedicatedGpuMemoryBytes is < 0)
            throw new AiContractValidationException("A leitura de hardware retornou valores inválidos.");
        if (!Enum.IsDefined(hardware.RecommendedProfile) || hardware.RecommendedProfile == AiProfile.Automatic)
            throw new AiContractValidationException("A leitura de hardware não retornou um perfil recomendado válido.");
        if (hardware.Warnings is null)
            throw new AiContractValidationException("A leitura de hardware retornou uma lista de avisos inválida.");
        if (string.IsNullOrWhiteSpace(storage.RootPath) || !Path.IsPathFullyQualified(storage.RootPath))
            throw new AiContractValidationException("O local analisado para a IA precisa ser um caminho absoluto.");
        if (storage.TotalBytes is < 0 || storage.AvailableBytes is < 0)
            throw new AiContractValidationException("O espaço de armazenamento detectado não pode ser negativo.");
        if (storage.TotalBytes.HasValue && storage.AvailableBytes > storage.TotalBytes)
            throw new AiContractValidationException("O espaço livre detectado não pode ultrapassar o total da unidade.");
    }

    private static string DefaultAiRootDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rota",
        "AI");
}

public sealed class WindowsAiStorageProbe : IAiStorageProbe
{
    public string RootDirectory { get; }

    public WindowsAiStorageProbe(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
            throw new AiContractValidationException("O diretório analisado para a IA precisa ser absoluto.");
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public Task<AiStorageSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private AiStorageSnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = Path.GetPathRoot(RootDirectory);
            if (string.IsNullOrWhiteSpace(root))
                throw new IOException("A unidade de armazenamento não pôde ser identificada.");

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return new AiStorageSnapshot(
                    RootDirectory,
                    null,
                    null,
                    "A unidade onde a IA será instalada não está pronta para consulta.");

            cancellationToken.ThrowIfCancellationRequested();
            return new AiStorageSnapshot(RootDirectory, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException)
        {
            return new AiStorageSnapshot(
                RootDirectory,
                null,
                null,
                "O espaço livre para a IA local não pôde ser consultado.");
        }
    }
}
