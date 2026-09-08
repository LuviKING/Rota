using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningContentLibraryTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning library exposes only verified installed packages", ExposesVerifiedPackages),
        ("Learning library isolates a damaged installed package", IsolatesDamagedPackage),
        ("Learning library activates only the newest verified package version", UsesNewestVerifiedVersion),
        ("Learning library falls back to the previous verified version when an update is damaged", FallsBackWhenNewestDamaged)
    };

    private static void ExposesVerifiedPackages()
    {
        WithLibrary((root, installer) =>
        {
            var package = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(package), ManifestBytes(package));
            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Single().Package.Package.Id == "matematica-basica", "installed package was not found");
            Require(snapshot.Issues.Count == 0, "valid package reported an issue");
        });
    }

    private static void IsolatesDamagedPackage()
    {
        WithLibrary((root, installer) =>
        {
            var package = Package("1.0.0");
            var installed = installer.Install(LearningContentPackageFormat.Serialize(package), ManifestBytes(package));
            File.WriteAllText(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.PackageFileName), "{}");
            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Count == 0 && snapshot.Issues.Count == 1, "damaged package was accepted");
        });
    }

    private static void UsesNewestVerifiedVersion()
    {
        WithLibrary((root, installer) =>
        {
            var original = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(original), ManifestBytes(original));
            var update = Package("1.2.0");
            _ = installer.InstallOrUpdate(LearningContentPackageFormat.Serialize(update), ManifestBytes(update));

            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Count == 1, "multiple versions of the same package were exposed simultaneously");
            Require(snapshot.Packages[0].Package.Package.Version == "1.2.0", "newest verified version was not selected");
            Require(snapshot.Issues.Count == 0, "valid side-by-side versions reported an issue");
        });
    }

    private static void FallsBackWhenNewestDamaged()
    {
        WithLibrary((root, installer) =>
        {
            var original = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(original), ManifestBytes(original));
            var update = Package("1.1.0");
            var updated = installer.InstallOrUpdate(LearningContentPackageFormat.Serialize(update), ManifestBytes(update));
            File.WriteAllText(Path.Combine(updated.Installed.DirectoryPath, LearningPackageInstaller.PackageFileName), "{}");

            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Count == 1, "valid previous version was not recovered");
            Require(snapshot.Packages[0].Package.Package.Version == "1.0.0", "library did not fall back to the previous verified version");
            Require(snapshot.Issues.Count == 1, "damaged newer version was not reported");
        });
    }

    private static byte[] ManifestBytes(LearningContentPackage package)
    {
        var manifest = LearningPackageManifestFormat.Create(
            package,
            new LearningPackageAuthor { Id = "rota-team", Name = "Equipe Rota" },
            LearningEducationLevels.General,
            new Version(0, 5, 0));
        return LearningPackageManifestFormat.Serialize(manifest);
    }

    private static void WithLibrary(Action<string, LearningPackageInstaller> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "rota-library-tests", Guid.NewGuid().ToString("N"));
        try { action(root, new LearningPackageInstaller(root, new Version(0, 5, 0))); }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { } }
    }

    private static LearningContentPackage Package(string version) => new()
    {
        Package = new LearningPackageIdentity { Id = "matematica-basica", Version = version, Title = "Matemática Básica", Locale = "pt-BR" },
        Catalog = new LearningCatalog
        {
            Subjects = new() { new() { Id = "matematica", Name = "Matemática" } },
            Courses = new() { new() { Id = "curso", SubjectId = "matematica", Name = "Curso", Position = 1 } },
            Modules = new() { new() { Id = "modulo", CourseId = "curso", Name = "Módulo", Position = 1 } },
            Skills = new() { new() { Id = "skill", SubjectId = "matematica", Name = "Habilidade" } },
            Lessons = new() { new() { Id = "aula", ModuleId = "modulo", Name = "Aula", Position = 1, SkillIds = new() { "skill" } } },
            Contents = new() { new() { Id = "conteudo", LessonId = "aula", Title = "Conteúdo", Position = 1 } }
        }
    };

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
