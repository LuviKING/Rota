namespace Rota.Desktop;

/// <summary>
/// Leitura segura da biblioteca de pacotes instalados pelo Rota. Quando existem
/// várias versões válidas do mesmo pacote, somente a mais recente fica ativa;
/// versões anteriores permanecem disponíveis no disco como fallback local.
/// </summary>
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
        var verifiedPackages = new List<LearningLibraryPackage>();
        var issues = new List<LearningLibraryIssue>();
        var packagesRoot = Path.Combine(_rootDirectory, "packages");
        if (!Directory.Exists(packagesRoot))
            return new LearningContentLibrarySnapshot(Array.Empty<LearningLibraryPackage>(), issues);

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
                ValidateInstallationPath(packagesRoot, directory, package);
                verifiedPackages.Add(new LearningLibraryPackage(package.Copy(), manifest.Copy(), directory));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                issues.Add(new LearningLibraryIssue(directory, "O pacote instalado foi ignorado: " + exception.Message));
            }
        }

        var activePackages = verifiedPackages
            .GroupBy(item => item.Package.Package.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => Version.Parse(item.Package.Package.Version))
                .ThenByDescending(item => item.Manifest.CreatedAtUtc)
                .First())
            .OrderBy(item => item.Package.Package.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Package.Package.Id, StringComparer.Ordinal)
            .ToList();

        return new LearningContentLibrarySnapshot(activePackages, issues);
    }

    private static void ValidateInstallationPath(string packagesRoot, string directory, LearningContentPackage package)
    {
        var versionDirectory = new DirectoryInfo(directory);
        var packageDirectory = versionDirectory.Parent;
        if (packageDirectory?.Parent is null ||
            !PathsEqual(packageDirectory.Parent.FullName, packagesRoot) ||
            !string.Equals(packageDirectory.Name, package.Package.Id, StringComparison.Ordinal) ||
            !string.Equals(versionDirectory.Name, package.Package.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A pasta do pacote não corresponde à identidade declarada no material.");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
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
