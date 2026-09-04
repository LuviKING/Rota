namespace Rota.Desktop.LocalAI;

public sealed class LocalAiInstaller : IAiInstaller, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiModelManager _modelManager;
    private readonly IAiArtifactDownloader _downloader;
    private readonly AiInstallationManifest _manifest;

    public string RootDirectory { get; }
    public string StagingDirectory => Path.Combine(RootDirectory, ".staging");
    public string InstallationsDirectory => Path.Combine(RootDirectory, "installations");

    public LocalAiInstaller(
        string rootDirectory,
        IAiConfigurationStore configurationStore,
        IAiModelManager modelManager,
        IAiArtifactDownloader? downloader = null,
        AiInstallationManifest? manifest = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
            throw new AiContractValidationException("O diretório de instalação da IA precisa ser um caminho absoluto.");

        RootDirectory = Path.GetFullPath(rootDirectory);
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _downloader = downloader ?? new HttpAiArtifactDownloader();
        _manifest = manifest ?? AiInstallationManifest.Default;
    }

    public async Task<AiInstallationResult> InstallAsync(
        AiConfiguration configuration,
        IProgress<AiInstallationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AiContractValidator.ValidateConfiguration(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        string? stagingRoot = null;
        string? finalRoot = null;
        FileStream? installationLock = null;
        var movedPayload = false;
        var activated = false;
        try
        {
            Report(progress, AiInstallationStage.Preparing, "", 0, null);
            var selection = await _modelManager.GetInstallationInfoAsync(configuration, cancellationToken).ConfigureAwait(false);
            var runtimePreference = ResolveRuntimePreference(configuration.ComputePreference, selection.EffectiveProfile);
            var runtimePackage = _manifest.GetRuntimePackage(runtimePreference);
            var modelPackage = _manifest.GetModelPackage(selection.Model.Id);

            cancellationToken.ThrowIfCancellationRequested();
            AiInstallationFileSafety.RejectReparsePoints(RootDirectory);
            Directory.CreateDirectory(RootDirectory);
            var lockPath = Path.Combine(RootDirectory, ".install.lock");
            AiInstallationFileSafety.RejectReparsePoints(lockPath);
            // Exclusive handle also protects separate installer instances and processes.
            installationLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            CheckAvailableSpace(runtimePackage.Archive.ExpectedSizeBytes + modelPackage.Artifact.ExpectedSizeBytes);
            stagingRoot = CreateStagingDirectory();
            var downloadsDirectory = Path.Combine(stagingRoot, "downloads");
            var payloadDirectory = Path.Combine(stagingRoot, "payload");
            var runtimeDirectory = Path.Combine(payloadDirectory, "runtime");
            var modelsDirectory = Path.Combine(payloadDirectory, "models");
            Directory.CreateDirectory(downloadsDirectory);
            Directory.CreateDirectory(runtimeDirectory);
            Directory.CreateDirectory(modelsDirectory);

            var runtimeArchivePath = Path.Combine(downloadsDirectory, runtimePackage.Archive.FileName);
            await DownloadAsync(
                runtimePackage.Archive,
                runtimeArchivePath,
                AiInstallationStage.DownloadingRuntime,
                progress,
                cancellationToken).ConfigureAwait(false);
            Report(progress, AiInstallationStage.VerifyingRuntime, runtimePackage.Archive.Id, 0, runtimePackage.Archive.ExpectedSizeBytes);
            await AiInstallationFileSafety.VerifyArtifactAsync(
                runtimePackage.Archive,
                runtimeArchivePath,
                cancellationToken).ConfigureAwait(false);

            Report(progress, AiInstallationStage.ExtractingRuntime, runtimePackage.Archive.Id, 0, null);
            await AiInstallationFileSafety.ExtractRuntimeSafelyAsync(
                runtimeArchivePath,
                runtimeDirectory,
                cancellationToken).ConfigureAwait(false);
            var stagedRuntimePath = AiInstallationFileSafety.ResolveContainedPath(
                runtimeDirectory,
                runtimePackage.ServerRelativePath);
            EnsureNonEmptyFile(stagedRuntimePath, "O pacote do runtime não contém um llama-server.exe válido.");

            var stagedModelPath = Path.Combine(modelsDirectory, selection.Model.FileName);
            await DownloadAsync(
                modelPackage.Artifact,
                stagedModelPath,
                AiInstallationStage.DownloadingModel,
                progress,
                cancellationToken).ConfigureAwait(false);
            Report(progress, AiInstallationStage.VerifyingModel, modelPackage.Artifact.Id, 0, modelPackage.Artifact.ExpectedSizeBytes);
            await AiInstallationFileSafety.VerifyArtifactAsync(
                modelPackage.Artifact,
                stagedModelPath,
                cancellationToken).ConfigureAwait(false);

            await WriteReceiptAsync(
                Path.Combine(payloadDirectory, "installation.json"),
                new AiInstallationReceipt
                {
                    ManifestVersion = _manifest.Version,
                    InstalledAtUtc = DateTimeOffset.UtcNow,
                    RuntimeArtifactId = runtimePackage.Archive.Id,
                    RuntimeSha256 = runtimePackage.Archive.Sha256,
                    ModelId = selection.Model.Id,
                    ModelArtifactId = modelPackage.Artifact.Id,
                    ModelSha256 = modelPackage.Artifact.Sha256
                },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, AiInstallationStage.Activating, selection.Model.Id, 0, null);
            cancellationToken.ThrowIfCancellationRequested();
            AiInstallationFileSafety.RejectReparsePoints(InstallationsDirectory);
            Directory.CreateDirectory(InstallationsDirectory);
            finalRoot = Path.Combine(
                InstallationsDirectory,
                $"manifest-{_manifest.Version}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
            Directory.Move(payloadDirectory, finalRoot);
            movedPayload = true;

            var installedConfiguration = configuration with
            {
                ModelId = selection.Model.Id,
                RuntimePath = AiInstallationFileSafety.ResolveContainedPath(
                    finalRoot,
                    Path.Combine("runtime", runtimePackage.ServerRelativePath)),
                ModelPath = AiInstallationFileSafety.ResolveContainedPath(
                    finalRoot,
                    Path.Combine("models", selection.Model.FileName)),
                InstallationState = AiInstallationState.Ready
            };
            // Reuse the selected profile so a second hardware probe cannot change the transaction.
            var installedInfo = await _modelManager.GetInstallationInfoAsync(
                installedConfiguration with { Profile = selection.EffectiveProfile }, cancellationToken).ConfigureAwait(false);
            if (installedInfo.State != AiInstallationState.Ready)
                throw new AiInstallationException("Os artefatos foram preparados, mas a instalação não ficou pronta para ativação.");

            await _configurationStore.SaveAsync(installedConfiguration, cancellationToken).ConfigureAwait(false);
            activated = true;
            try
            {
                Report(progress, AiInstallationStage.Completed, selection.Model.Id,
                    modelPackage.Artifact.ExpectedSizeBytes, modelPackage.Artifact.ExpectedSizeBytes);
            }
            catch
            {
                // The atomic configuration write is the commit point; notification cannot undo it.
            }
            return new AiInstallationResult(installedConfiguration, installedInfo, finalRoot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not AiContractValidationException and not AiInstallationException)
        {
            throw new AiInstallationException(
                "A instalação local da IA falhou sem alterar a configuração ativa.",
                ex);
        }
        finally
        {
            if (!activated && movedPayload && finalRoot is not null)
                TryDeleteOwnedDirectory(InstallationsDirectory, finalRoot);
            if (stagingRoot is not null)
                TryDeleteOwnedDirectory(StagingDirectory, stagingRoot);
            installationLock?.Dispose();
            _gate.Release();
        }
    }

    private async Task DownloadAsync(
        AiDownloadArtifact artifact,
        string destinationPath,
        AiInstallationStage stage,
        IProgress<AiInstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, stage, artifact.Id, 0, artifact.ExpectedSizeBytes);
        var adapter = progress is null
            ? null
            : new DownloadProgressAdapter(download =>
                Report(progress, stage, artifact.Id, download.BytesReceived, download.TotalBytes ?? artifact.ExpectedSizeBytes));
        await _downloader.DownloadAsync(
            artifact.Source, destinationPath, artifact.ExpectedSizeBytes, adapter, cancellationToken).ConfigureAwait(false);
    }

    private string CreateStagingDirectory()
    {
        AiInstallationFileSafety.RejectReparsePoints(StagingDirectory);
        Directory.CreateDirectory(StagingDirectory);
        var path = Path.Combine(StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void CheckAvailableSpace(long downloadBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(RootDirectory)!);
        // Room for both downloads, bounded runtime extraction and configuration writes.
        const long extractionAndReserve = (2L * 1024 + 128) * 1024 * 1024;
        if (drive.IsReady && drive.AvailableFreeSpace < downloadBytes + extractionAndReserve)
            throw new AiInstallationException("Não há espaço livre suficiente para preparar a instalação da IA.");
    }

    private static async Task WriteReceiptAsync(
        string path,
        AiInstallationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(receipt, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static AiComputePreference ResolveRuntimePreference(
        AiComputePreference configuredPreference,
        AiProfile effectiveProfile) => configuredPreference switch
        {
            AiComputePreference.Cpu => AiComputePreference.Cpu,
            AiComputePreference.Gpu => AiComputePreference.Gpu,
            AiComputePreference.Automatic when effectiveProfile == AiProfile.Performance => AiComputePreference.Gpu,
            AiComputePreference.Automatic => AiComputePreference.Cpu,
            _ => throw new AiContractValidationException("A preferência de processamento da IA é inválida.")
        };

    private static void EnsureNonEmptyFile(string path, string message)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0)
            throw new AiInstallationException(message);
    }

    private static void Report(
        IProgress<AiInstallationProgress>? progress,
        AiInstallationStage stage,
        string artifactId,
        long bytesReceived,
        long? totalBytes) =>
        progress?.Report(new AiInstallationProgress(stage, artifactId, bytesReceived, totalBytes));

    private static void TryDeleteOwnedDirectory(string parent, string path)
    {
        try
        {
            var canonicalPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(canonicalPath), Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase))
                return;
            AiInstallationFileSafety.RejectReparsePoints(canonicalPath);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A limpeza é de melhor esforço; nunca mascara o resultado da instalação.
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed class DownloadProgressAdapter : IProgress<AiDownloadProgress>
    {
        private readonly Action<AiDownloadProgress> _report;

        public DownloadProgressAdapter(Action<AiDownloadProgress> report)
        {
            _report = report;
        }

        public void Report(AiDownloadProgress value) => _report(value);
    }
}
