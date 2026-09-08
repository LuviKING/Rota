namespace Rota.Desktop;

/// <summary>
/// Instala e atualiza pacotes pedagógicos somente depois de conferir seu manifesto.
/// Cada versão é ativada por um rename de diretório, sem sobrescrever a versão anterior,
/// para que uma falha de escrita nunca destrua o material que já estava funcionando.
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

    /// <summary>Valida o arquivo e informa se ele será uma instalação nova ou uma atualização, sem gravar nada.</summary>
    public LearningPackageChange Inspect(ReadOnlySpan<byte> packageBytes, ReadOnlySpan<byte> manifestBytes) =>
        Evaluate(packageBytes, manifestBytes).Change;

    /// <summary>
    /// Instala somente um pacote ainda inexistente. Atualizações devem passar por
    /// <see cref="InstallOrUpdate"/> para que as regras de continuidade sejam verificadas.
    /// </summary>
    public LearningInstalledPackage Install(ReadOnlySpan<byte> packageBytes, ReadOnlySpan<byte> manifestBytes)
    {
        var candidate = Evaluate(packageBytes, manifestBytes);
        if (candidate.Change.Kind == LearningPackageChangeKind.Update)
            throw new InvalidDataException("Este material já está instalado. Use a atualização de pacote para trocar de versão com segurança.");
        return Persist(candidate);
    }

    /// <summary>
    /// Instala um pacote novo ou ativa uma versão superior do mesmo pacote. A versão
    /// anterior permanece intacta no disco e a biblioteca passa a expor somente a
    /// versão válida mais recente.
    /// </summary>
    public LearningPackageChangeResult InstallOrUpdate(ReadOnlySpan<byte> packageBytes, ReadOnlySpan<byte> manifestBytes)
    {
        var candidate = Evaluate(packageBytes, manifestBytes);
        var installed = Persist(candidate);
        return new LearningPackageChangeResult(candidate.Change, installed);
    }

    private Candidate Evaluate(ReadOnlySpan<byte> packageBytes, ReadOnlySpan<byte> manifestBytes)
    {
        var packageRaw = packageBytes.ToArray();
        var manifestRaw = manifestBytes.ToArray();
        var manifest = LearningPackageManifestFormat.Parse(manifestRaw);
        var package = LearningPackageManifestFormat.VerifyPackage(manifest, packageRaw, _applicationVersion);
        var current = new LearningContentLibrary(_rootDirectory, _applicationVersion).Load().Packages
            .SingleOrDefault(item => string.Equals(item.Package.Package.Id, package.Package.Id, StringComparison.Ordinal));

        if (current is null)
        {
            return new Candidate(package, manifest, packageRaw, manifestRaw, new LearningPackageChange(
                LearningPackageChangeKind.Install,
                package.Package.Id,
                package.Package.Title,
                null,
                package.Package.Version,
                manifest.Author.Name,
                manifest.EducationLevel,
                package.Catalog.Contents.Count));
        }

        var currentVersion = Version.Parse(current.Package.Package.Version);
        var candidateVersion = Version.Parse(package.Package.Version);
        if (candidateVersion == currentVersion)
            throw new InvalidDataException("Esta versão do pacote pedagógico já está instalada.");
        if (candidateVersion < currentVersion)
            throw new InvalidDataException($"O Rota bloqueou o downgrade do pacote pedagógico: a versão instalada é {current.Package.Package.Version} e o arquivo selecionado é {package.Package.Version}.");

        ValidateUpdateContinuity(current, package, manifest);
        return new Candidate(package, manifest, packageRaw, manifestRaw, new LearningPackageChange(
            LearningPackageChangeKind.Update,
            package.Package.Id,
            package.Package.Title,
            current.Package.Package.Version,
            package.Package.Version,
            manifest.Author.Name,
            manifest.EducationLevel,
            package.Catalog.Contents.Count));
    }

    private LearningInstalledPackage Persist(Candidate candidate)
    {
        var packageDirectory = Path.Combine(_rootDirectory, "packages", candidate.Package.Package.Id, candidate.Package.Package.Version);
        if (Directory.Exists(packageDirectory))
            throw new InvalidDataException("Esta versão do pacote pedagógico já existe na biblioteca local.");

        var stagingDirectory = Path.Combine(_rootDirectory, $".install-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            WriteFile(Path.Combine(stagingDirectory, PackageFileName), candidate.PackageBytes);
            WriteFile(Path.Combine(stagingDirectory, ManifestFileName), candidate.ManifestBytes);
            VerifyStagedPackage(stagingDirectory, candidate.Package.Package.Id, candidate.Package.Package.Version);

            Directory.CreateDirectory(Path.GetDirectoryName(packageDirectory)!);
            Directory.Move(stagingDirectory, packageDirectory);
            return new LearningInstalledPackage(
                candidate.Package.Package.Id,
                candidate.Package.Package.Version,
                candidate.Package.Package.Title,
                candidate.Package.Package.Locale,
                candidate.Manifest.Author.Name,
                candidate.Manifest.EducationLevel,
                packageDirectory);
        }
        finally
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); } catch { }
        }
    }

    private void VerifyStagedPackage(string stagingDirectory, string expectedId, string expectedVersion)
    {
        var packageBytes = ReadBounded(Path.Combine(stagingDirectory, PackageFileName), LearningContentPackageFormat.MaximumPackageBytes);
        var manifestBytes = ReadBounded(Path.Combine(stagingDirectory, ManifestFileName), LearningPackageManifestFormat.MaximumManifestBytes);
        var manifest = LearningPackageManifestFormat.Parse(manifestBytes);
        var package = LearningPackageManifestFormat.VerifyPackage(manifest, packageBytes, _applicationVersion);
        if (!string.Equals(package.Package.Id, expectedId, StringComparison.Ordinal) ||
            !string.Equals(package.Package.Version, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A verificação final do pacote pedagógico não corresponde ao arquivo preparado.");
        }
    }

    private static void ValidateUpdateContinuity(
        LearningLibraryPackage current,
        LearningContentPackage candidate,
        LearningPackageManifest candidateManifest)
    {
        if (!string.Equals(current.Manifest.Author.Id, candidateManifest.Author.Id, StringComparison.Ordinal))
            throw new InvalidDataException("A atualização foi bloqueada porque o autor do pacote não corresponde ao material já instalado.");
        if (!string.Equals(current.Package.Package.Locale, candidate.Package.Locale, StringComparison.Ordinal))
            throw new InvalidDataException("A atualização foi bloqueada porque o idioma do pacote mudou.");
        if (!string.Equals(current.Manifest.EducationLevel, candidateManifest.EducationLevel, StringComparison.Ordinal))
            throw new InvalidDataException("A atualização foi bloqueada porque o nível escolar do pacote mudou.");

        RequirePreservedIds(
            current.Package.Catalog.Contents.Select(item => item.Id),
            candidate.Catalog.Contents.Select(item => item.Id),
            "conteúdo");
        RequirePreservedIds(
            current.Package.PracticeQuestions.Select(item => item.Id),
            candidate.PracticeQuestions.Select(item => item.Id),
            "questão");
        RequirePreservedIds(
            current.Package.Assessments.Select(item => item.Id),
            candidate.Assessments.Select(item => item.Id),
            "simulado");
    }

    private static void RequirePreservedIds(IEnumerable<string> currentIds, IEnumerable<string> candidateIds, string itemName)
    {
        var candidateSet = candidateIds.ToHashSet(StringComparer.Ordinal);
        var missing = currentIds.FirstOrDefault(id => !candidateSet.Contains(id));
        if (missing is not null)
            throw new InvalidDataException($"A atualização removeria um {itemName} já identificado pelo Rota ('{missing}'). Os identificadores pedagógicos existentes precisam ser preservados.");
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length is < 1 or > int.MaxValue || info.Length > maximumBytes)
            throw new InvalidDataException("O arquivo do pacote excede o limite seguro.");
        return File.ReadAllBytes(path);
    }

    private static void WriteFile(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private sealed record Candidate(
        LearningContentPackage Package,
        LearningPackageManifest Manifest,
        byte[] PackageBytes,
        byte[] ManifestBytes,
        LearningPackageChange Change);
}

public enum LearningPackageChangeKind
{
    Install,
    Update
}

public sealed record LearningPackageChange(
    LearningPackageChangeKind Kind,
    string PackageId,
    string Title,
    string? PreviousVersion,
    string NewVersion,
    string AuthorName,
    string EducationLevel,
    int ContentCount);

public sealed record LearningPackageChangeResult(LearningPackageChange Change, LearningInstalledPackage Installed);

public sealed record LearningInstalledPackage(
    string Id,
    string Version,
    string Title,
    string Locale,
    string AuthorName,
    string EducationLevel,
    string DirectoryPath);
