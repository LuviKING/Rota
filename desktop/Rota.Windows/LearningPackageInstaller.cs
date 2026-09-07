namespace Rota.Desktop;

/// <summary>
/// Instala um pacote pedagógico somente depois de conferir seu manifesto. A
/// instalação é feita em uma pasta temporária e só aparece na biblioteca após
/// todos os bytes terem sido validados e gravados.
/// </summary>
public sealed class LearningPackageInstaller
{
    public const string PackageFileName = "package.json";
    public const string ManifestFileName = "manifest.json";

    private readonly string _rootDirectory;
    private readonly Version _applicationVersion;

    public LearningPackageInstaller(string rootDirectory, Version applicationVersion)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("A pasta da biblioteca é obrigatória.", nameof(rootDirectory));
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _applicationVersion = applicationVersion ?? throw new ArgumentNullException(nameof(applicationVersion));
    }

    public LearningInstalledPackage Install(ReadOnlySpan<byte> packageBytes, ReadOnlySpan<byte> manifestBytes)
    {
        var manifest = LearningPackageManifestFormat.Parse(manifestBytes);
        var package = LearningPackageManifestFormat.VerifyPackage(manifest, packageBytes, _applicationVersion);
        var packageDirectory = Path.Combine(_rootDirectory, "packages", package.Package.Id, package.Package.Version);
        if (Directory.Exists(packageDirectory))
            throw new InvalidDataException("Esta versão do pacote pedagógico já está instalada.");

        var stagingDirectory = Path.Combine(_rootDirectory, $".install-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            WriteFile(Path.Combine(stagingDirectory, PackageFileName), packageBytes);
            WriteFile(Path.Combine(stagingDirectory, ManifestFileName), manifestBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(packageDirectory)!);
            Directory.Move(stagingDirectory, packageDirectory);
            return new LearningInstalledPackage(package.Package.Id, package.Package.Version, package.Package.Title,
                package.Package.Locale, manifest.Author.Name, manifest.EducationLevel, packageDirectory);
        }
        finally
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); } catch { }
        }
    }

    private static void WriteFile(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}

public sealed record LearningInstalledPackage(
    string Id,
    string Version,
    string Title,
    string Locale,
    string AuthorName,
    string EducationLevel,
    string DirectoryPath);
