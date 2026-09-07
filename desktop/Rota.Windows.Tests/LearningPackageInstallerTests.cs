using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningPackageInstallerTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package installer verifies and atomically installs a package", InstallsVerifiedPackage),
        ("Learning package installer rejects altered bytes without partial files", RejectsAlteredBytes),
        ("Learning package installer rejects duplicate package versions", RejectsDuplicateVersion)
    };

    private static void InstallsVerifiedPackage()
    {
        WithInstaller((installer, packageBytes, manifestBytes, root) =>
        {
            var installed = installer.Install(packageBytes, manifestBytes);
            Require(File.Exists(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.PackageFileName)), "package file missing");
            Require(File.Exists(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.ManifestFileName)), "manifest file missing");
            Require(installed.AuthorName == "Equipe Rota", "author identity missing");
            Require(!Directory.EnumerateDirectories(root, ".install-*", SearchOption.TopDirectoryOnly).Any(), "staging directory leaked");
        });
    }

    private static void RejectsAlteredBytes()
    {
        WithInstaller((installer, packageBytes, manifestBytes, root) =>
        {
            packageBytes[^1] ^= 1;
            Throws(() => installer.Install(packageBytes, manifestBytes), "SHA-256");
            Require(!Directory.Exists(Path.Combine(root, "packages")), "invalid package was installed");
        });
    }

    private static void RejectsDuplicateVersion()
    {
        WithInstaller((installer, packageBytes, manifestBytes, root) =>
        {
            _ = installer.Install(packageBytes, manifestBytes);
            Throws(() => installer.Install(packageBytes, manifestBytes), "já está instalada");
        });
    }

    private static void WithInstaller(Action<LearningPackageInstaller, byte[], byte[], string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "rota-package-installer-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var package = Package();
            var packageBytes = LearningContentPackageFormat.Serialize(package);
            var manifest = LearningPackageManifestFormat.Create(package,
                new LearningPackageAuthor { Id = "rota-team", Name = "Equipe Rota" },
                LearningEducationLevels.Enem, new Version(0, 5, 0));
            action(new LearningPackageInstaller(root, new Version(0, 5, 0)), packageBytes,
                LearningPackageManifestFormat.Serialize(manifest), root);
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { } }
    }

    private static LearningContentPackage Package() => new()
    {
        Package = new LearningPackageIdentity { Id = "enem-matematica", Version = "1.0.0", Title = "Matemática", Locale = "pt-BR" },
        Catalog = new LearningCatalog
        {
            Subjects = new() { new() { Id = "matematica", Name = "Matemática" } },
            Courses = new() { new() { Id = "mat-basico", SubjectId = "matematica", Name = "Básico", Position = 1 } },
            Modules = new() { new() { Id = "mat-equacoes", CourseId = "mat-basico", Name = "Equações", Position = 1 } },
            Skills = new() { new() { Id = "mat-skill", SubjectId = "matematica", Name = "Equações simples" } },
            Lessons = new() { new() { Id = "mat-aula", ModuleId = "mat-equacoes", Name = "Aula", Position = 1, SkillIds = new() { "mat-skill" } } },
            Contents = new() { new() { Id = "mat-conteudo", LessonId = "mat-aula", Title = "Conteúdo", Position = 1 } }
        }
    };

    private static void Throws(Action action, string fragment)
    {
        try { action(); }
        catch (InvalidDataException exception) when (exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected InvalidDataException containing '{fragment}'.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
