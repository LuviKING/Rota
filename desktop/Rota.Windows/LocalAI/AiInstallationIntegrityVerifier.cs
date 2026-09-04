using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public sealed class AiInstallationIntegrityVerifier : IAiInstallationIntegrityVerifier
{
    private const long MaximumReceiptBytes = 64 * 1024;
    private const int CopyBufferSize = 128 * 1024;
    private readonly AiInstallationManifest _manifest;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public AiInstallationIntegrityVerifier(AiInstallationManifest? manifest = null)
    {
        _manifest = manifest ?? AiInstallationManifest.Default;
    }

    public async Task<AiInstallationIntegrityResult> VerifyAsync(
        AiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        AiContractValidator.ValidateConfiguration(configuration);
        if (configuration.InstallationState != AiInstallationState.Ready)
            throw new AiRuntimeException("A configuração da IA não representa uma instalação pronta.");

        cancellationToken.ThrowIfCancellationRequested();
        var runtimePath = Path.GetFullPath(configuration.RuntimePath);
        var modelPath = Path.GetFullPath(configuration.ModelPath);
        var runtimeDirectory = Path.GetDirectoryName(runtimePath)
            ?? throw new AiRuntimeException("O caminho do runtime instalado é inválido.");
        var modelDirectory = Path.GetDirectoryName(modelPath)
            ?? throw new AiRuntimeException("O caminho do modelo instalado é inválido.");
        var installationDirectory = Path.GetDirectoryName(runtimeDirectory)
            ?? throw new AiRuntimeException("O diretório da instalação local é inválido.");
        if (!string.Equals(Path.GetDirectoryName(modelDirectory), installationDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(runtimeDirectory, Path.Combine(installationDirectory, "runtime"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(modelDirectory, Path.Combine(installationDirectory, "models"), StringComparison.OrdinalIgnoreCase))
        {
            throw new AiRuntimeException("Os artefatos da IA não pertencem à mesma instalação isolada.");
        }

        AiInstallationFileSafety.RejectReparsePoints(runtimePath);
        AiInstallationFileSafety.RejectReparsePoints(modelPath);
        var receiptPath = Path.Combine(installationDirectory, "installation.json");
        var archivePath = Path.Combine(installationDirectory, ".integrity", "runtime-package.zip");
        AiInstallationFileSafety.RejectReparsePoints(receiptPath);
        AiInstallationFileSafety.RejectReparsePoints(archivePath);

        var receipt = await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false);
        ValidateReceipt(receipt, configuration);
        var runtimePackage = _manifest.RuntimePackages.SingleOrDefault(package =>
            string.Equals(package.Archive.Id, receipt.RuntimeArtifactId, StringComparison.OrdinalIgnoreCase));
        var modelPackage = _manifest.GetModelPackage(configuration.ModelId);
        if (runtimePackage is null ||
            !string.Equals(receipt.RuntimeSha256, runtimePackage.Archive.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.ModelArtifactId, modelPackage.Artifact.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.ModelSha256, modelPackage.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new AiRuntimeException("O recibo da instalação não corresponde ao manifesto fixado do Rota.");
        }

        var expectedRuntimePath = Path.Combine(runtimeDirectory, runtimePackage.ServerRelativePath);
        var expectedModelPath = Path.Combine(modelDirectory, modelPackage.Artifact.FileName);
        if (!string.Equals(runtimePath, expectedRuntimePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(modelPath, expectedModelPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new AiRuntimeException("A configuração aponta para artefatos diferentes dos instalados pelo Rota.");
        }

        try
        {
            await AiInstallationFileSafety.VerifyArtifactAsync(
                runtimePackage.Archive, archivePath, cancellationToken).ConfigureAwait(false);
            await AiInstallationFileSafety.VerifyArtifactAsync(
                modelPackage.Artifact, modelPath, cancellationToken).ConfigureAwait(false);
            await VerifyRuntimeFilesAsync(archivePath, runtimeDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AiInstallationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new AiRuntimeException("A instalação local foi alterada ou corrompida e não será executada.", ex);
        }

        return new AiInstallationIntegrityResult(installationDirectory, runtimePackage.ComputePreference);
    }

    private async Task<AiInstallationReceipt> ReadReceiptAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumReceiptBytes)
                throw new AiRuntimeException("O recibo da instalação local está ausente ou inválido.");
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<AiInstallationReceipt>(stream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new AiRuntimeException("O recibo da instalação local está vazio.");
        }
        catch (AiRuntimeException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new AiRuntimeException("O recibo da instalação local não pôde ser validado.", ex);
        }
    }

    private void ValidateReceipt(AiInstallationReceipt receipt, AiConfiguration configuration)
    {
        if (receipt.SchemaVersion != AiInstallationReceipt.CurrentSchemaVersion ||
            receipt.ManifestVersion != _manifest.Version ||
            receipt.InstalledAtUtc == default || receipt.InstalledAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(receipt.RuntimeArtifactId) ||
            string.IsNullOrWhiteSpace(receipt.RuntimeSha256) ||
            !string.Equals(receipt.ModelId, configuration.ModelId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(receipt.ModelArtifactId) ||
            string.IsNullOrWhiteSpace(receipt.ModelSha256))
        {
            throw new AiRuntimeException("O recibo da instalação local é incompatível ou inválido.");
        }
    }

    private static async Task VerifyRuntimeFilesAsync(
        string archivePath,
        string runtimeDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var installedPath = AiInstallationFileSafety.ResolveContainedPath(runtimeDirectory, relativePath);
            AiInstallationFileSafety.RejectReparsePoints(installedPath);
            expectedFiles.Add(Path.GetFullPath(installedPath));
            var installed = new FileInfo(installedPath);
            if (!installed.Exists || installed.Length != entry.Length ||
                (installed.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new AiInstallationException("Um arquivo extraído do runtime foi alterado ou removido.");
            }

            await using var expected = entry.Open();
            await using var actual = new FileStream(
                installedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var expectedHash = await SHA256.HashDataAsync(expected, cancellationToken).ConfigureAwait(false);
            var actualHash = await SHA256.HashDataAsync(actual, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                throw new AiInstallationException("Um arquivo extraído do runtime falhou na verificação de integridade.");
        }

        var actualFiles = EnumerateRuntimeFilesWithoutFollowingLinks(runtimeDirectory).ToArray();
        if (actualFiles.Length != expectedFiles.Count || actualFiles.Any(path => !expectedFiles.Contains(path)))
            throw new AiInstallationException("O diretório do runtime contém arquivos não verificados.");
    }

    private static IEnumerable<string> EnumerateRuntimeFilesWithoutFollowingLinks(string runtimeDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(runtimeDirectory));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new AiInstallationException("O diretório do runtime contém um link não verificado.");
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new AiInstallationException("O runtime contém um arquivo redirecionado não verificado.");
                yield return Path.GetFullPath(file);
            }
            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new AiInstallationException("O runtime contém um diretório redirecionado não verificado.");
                pending.Push(child);
            }
        }
    }
}
