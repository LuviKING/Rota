using System.IO.Compression;
using System.Security.Cryptography;

namespace Rota.Desktop.LocalAI;

internal static class AiInstallationFileSafety
{
    private const int CopyBufferSize = 128 * 1024;
    private const int MaximumRuntimeEntries = 2_048;
    private const long MaximumExtractedRuntimeBytes = 2L * 1024 * 1024 * 1024;

    public static async Task VerifyArtifactAsync(
        AiDownloadArtifact artifact,
        string path,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != artifact.ExpectedSizeBytes)
        {
            throw new AiInstallationException(
                $"O artefato {artifact.Id} possui tamanho diferente do manifesto e foi rejeitado.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var expectedHash = Convert.FromHexString(artifact.Sha256);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new AiInstallationException($"O artefato {artifact.Id} falhou na verificação SHA-256 e foi rejeitado.");
    }

    public static async Task ExtractRuntimeSafelyAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > MaximumRuntimeEntries)
            throw new AiInstallationException("O pacote do runtime possui uma quantidade de arquivos inválida.");

        long totalBytes = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsSymbolicLink(entry))
                throw new AiInstallationException("O pacote do runtime contém um link simbólico não permitido.");

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            if (relativePath.Length == 0) continue;
            AiInstallationManifest.ValidateRelativePath(relativePath, "item do pacote");
            var destinationPath = ResolveContainedPath(destinationDirectory, relativePath);
            if (!destinations.Add(destinationPath))
                throw new AiInstallationException("O pacote do runtime contém destinos duplicados.");

            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumExtractedRuntimeBytes)
                throw new AiInstallationException("O pacote do runtime excede o limite seguro de extração.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[CopyBufferSize];
            long written = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                if (count > entry.Length - written)
                    throw new AiInstallationException("Um arquivo do runtime excede o tamanho anunciado no ZIP.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                written += count;
            }
            if (written != entry.Length)
                throw new AiInstallationException("Um arquivo do runtime está truncado no ZIP.");
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
    }

    public static string ResolveContainedPath(string root, string relativePath)
    {
        AiInstallationManifest.ValidateRelativePath(relativePath, "artefato instalado");
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new AiInstallationException("O pacote tentou gravar fora do diretório local da IA.");
        return resolved;
    }

    internal static void RejectReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new AiInstallationException("O diretório da instalação contém um link ou redirecionamento inseguro.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
}
