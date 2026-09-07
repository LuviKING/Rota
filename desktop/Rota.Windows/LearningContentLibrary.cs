namespace Rota.Desktop;

/// <summary>Leitura segura da biblioteca de pacotes instalados pelo Rota.</summary>
public sealed class LearningContentLibrary
{
    private readonly string _rootDirectory;
    private readonly Version _applicationVersion;

    public LearningContentLibrary(string rootDirectory, Version applicationVersion)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("A pasta da biblioteca é obrigatória.", nameof(rootDirectory));
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _applicationVersion = applicationVersion ?? throw new ArgumentNullException(nameof(applicationVersion));
    }

    public LearningContentLibrarySnapshot Load()
    {
        var packages = new List<LearningLibraryPackage>();
        var issues = new List<LearningLibraryIssue>();
        var packagesRoot = Path.Combine(_rootDirectory, "packages");
        if (!Directory.Exists(packagesRoot)) return new LearningContentLibrarySnapshot(packages, issues);

        foreach (var directory in Directory.EnumerateDirectories(packagesRoot, "*", SearchOption.AllDirectories))
        {
            var packagePath = Path.Combine(directory, LearningPackageInstaller.PackageFileName);
            var manifestPath = Path.Combine(directory, LearningPackageInstaller.ManifestFileName);
            if (!File.Exists(packagePath) && !File.Exists(manifestPath)) continue;
            if (!File.Exists(packagePath) || !File.Exists(manifestPath))
            {
                issues.Add(new LearningLibraryIssue(directory, "O pacote instalado está incompleto."));
                continue;
            }

            try
            {
                var packageBytes = ReadBounded(packagePath, LearningContentPackageFormat.MaximumPackageBytes);
                var manifestBytes = ReadBounded(manifestPath, LearningPackageManifestFormat.MaximumManifestBytes);
                var manifest = LearningPackageManifestFormat.Parse(manifestBytes);
                var package = LearningPackageManifestFormat.VerifyPackage(manifest, packageBytes, _applicationVersion);
                packages.Add(new LearningLibraryPackage(package.Copy(), manifest.Copy(), directory));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                issues.Add(new LearningLibraryIssue(directory, "O pacote instalado foi ignorado: " + exception.Message));
            }
        }

        return new LearningContentLibrarySnapshot(
            packages.OrderBy(item => item.Package.Package.Title, StringComparer.CurrentCultureIgnoreCase).ToList(), issues);
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length is < 1 or > int.MaxValue || info.Length > maximumBytes)
            throw new InvalidDataException("O arquivo do pacote excede o limite seguro.");
        return File.ReadAllBytes(path);
    }
}

public sealed record LearningLibraryPackage(LearningContentPackage Package, LearningPackageManifest Manifest, string DirectoryPath);
public sealed record LearningLibraryIssue(string DirectoryPath, string Message);
public sealed record LearningContentLibrarySnapshot(IReadOnlyList<LearningLibraryPackage> Packages, IReadOnlyList<LearningLibraryIssue> Issues);
