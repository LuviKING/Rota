using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningContentLibraryTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning library exposes only verified installed packages", ExposesVerifiedPackages),
        ("Learning library isolates a damaged installed package", IsolatesDamagedPackage)
    };

    private static void ExposesVerifiedPackages()
    {
        WithLibrary((root, installer, packageBytes, manifestBytes) =>
        {
            _ = installer.Install(packageBytes, manifestBytes);
            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Single().Package.Package.Id == "matematica-basica", "installed package was not found");
            Require(snapshot.Issues.Count == 0, "valid package reported an issue");
        });
    }

    private static void IsolatesDamagedPackage()
    {
        WithLibrary((root, installer, packageBytes, manifestBytes) =>
        {
            var installed = installer.Install(packageBytes, manifestBytes);
            File.WriteAllText(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.PackageFileName), "{}" );
            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Count == 0 && snapshot.Issues.Count == 1, "damaged package was accepted");
        });
    }

    private static void WithLibrary(Action<string, LearningPackageInstaller, byte[], byte[]> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "rota-library-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var package = Package();
            var packageBytes = LearningContentPackageFormat.Serialize(package);
            var manifest = LearningPackageManifestFormat.Create(package,
                new LearningPackageAuthor { Id = "rota-team", Name = "Equipe Rota" }, LearningEducationLevels.General, new Version(0, 5, 0));
            action(root, new LearningPackageInstaller(root, new Version(0, 5, 0)), packageBytes, LearningPackageManifestFormat.Serialize(manifest));
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { } }
    }

    private static LearningContentPackage Package() => new()
    {
        Package = new LearningPackageIdentity { Id = "matematica-basica", Version = "1.0.0", Title = "Matemática Básica", Locale = "pt-BR" },
        Catalog = new LearningCatalog
        {
            Subjects = new() { new() { Id = "matematica", Name = "Matemática" } }, Courses = new() { new() { Id = "curso", SubjectId = "matematica", Name = "Curso", Position = 1 } },
            Modules = new() { new() { Id = "modulo", CourseId = "curso", Name = "Módulo", Position = 1 } }, Skills = new() { new() { Id = "skill", SubjectId = "matematica", Name = "Habilidade" } },
            Lessons = new() { new() { Id = "aula", ModuleId = "modulo", Name = "Aula", Position = 1, SkillIds = new() { "skill" } } }, Contents = new() { new() { Id = "conteudo", LessonId = "aula", Title = "Conteúdo", Position = 1 } }
        }
    };

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
