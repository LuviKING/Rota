using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningPackageManifestTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package manifest verifies an exact pedagogical package", VerifiesExactPackage),
        ("Learning package manifest rejects altered package bytes", RejectsAlteredPackage),
        ("Learning package manifest applies application version bounds", AppliesApplicationVersionBounds),
        ("Learning package manifest rejects unsafe JSON and invalid hashes", RejectsUnsafeManifest)
    };

    private static void VerifiesExactPackage()
    {
        var package = Package();
        var manifest = LearningPackageManifestFormat.Create(
            package,
            new LearningPackageAuthor { Id = "rota-content-team", Name = "Equipe de Conteúdo Rota" },
            LearningEducationLevels.Enem,
            new Version(0, 5, 0),
            new Version(0, 5, 99),
            new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero));

        var parsedManifest = LearningPackageManifestFormat.Parse(LearningPackageManifestFormat.Serialize(manifest));
        var verified = LearningPackageManifestFormat.VerifyPackage(
            parsedManifest,
            LearningContentPackageFormat.Serialize(package),
            new Version(0, 5, 0));

        Require(verified.Package.Id == package.Package.Id, "verified package identity changed");
        Require(parsedManifest.Author.Name == "Equipe de Conteúdo Rota", "author was not preserved");
        Require(parsedManifest.EducationLevel == LearningEducationLevels.Enem, "education level was not preserved");
        Require(parsedManifest.Package.Sha256.Length == 64, "package integrity hash was not produced");
    }

    private static void RejectsAlteredPackage()
    {
        var packageBytes = LearningContentPackageFormat.Serialize(Package());
        var manifest = LearningPackageManifestFormat.Create(
            Package(),
            new LearningPackageAuthor { Id = "rota-content-team", Name = "Equipe Rota" },
            LearningEducationLevels.HighSchool,
            new Version(0, 5, 0));
        packageBytes[^1] ^= 0x01;

        Throws(() => LearningPackageManifestFormat.VerifyPackage(manifest, packageBytes, new Version(0, 5, 0)), "SHA-256");
    }

    private static void AppliesApplicationVersionBounds()
    {
        var manifest = LearningPackageManifestFormat.Create(
            Package(),
            new LearningPackageAuthor { Id = "rota-content-team", Name = "Equipe Rota" },
            LearningEducationLevels.General,
            new Version(0, 5, 0),
            new Version(0, 5, 2));

        Require(!LearningPackageManifestFormat.IsCompatibleWith(manifest, new Version(0, 4, 9)), "older application was accepted");
        Require(LearningPackageManifestFormat.IsCompatibleWith(manifest, new Version(0, 5, 1)), "supported application was rejected");
        Require(!LearningPackageManifestFormat.IsCompatibleWith(manifest, new Version(0, 5, 3)), "newer unsupported application was accepted");
    }

    private static void RejectsUnsafeManifest()
    {
        var manifest = LearningPackageManifestFormat.Create(
            Package(),
            new LearningPackageAuthor { Id = "rota-content-team", Name = "Equipe Rota" },
            LearningEducationLevels.Fundamental,
            new Version(0, 5, 0));
        manifest.Package.Sha256 = manifest.Package.Sha256.ToUpperInvariant();
        Throws(() => LearningPackageManifestFormat.Serialize(manifest), "SHA-256");

        var validJson = Encoding.UTF8.GetString(LearningPackageManifestFormat.Serialize(LearningPackageManifestFormat.Create(
            Package(),
            new LearningPackageAuthor { Id = "rota-content-team", Name = "Equipe Rota" },
            LearningEducationLevels.Fundamental,
            new Version(0, 5, 0))));
        var root = JsonNode.Parse(validJson)!.AsObject();
        root["unexpected"] = true;
        Throws(() => LearningPackageManifestFormat.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())), "JSON inválido");

        var duplicated = validJson.Replace("\"format\": \"rota-learning-package-manifest\"", "\"format\": \"rota-learning-package-manifest\", \"format\": \"rota-learning-package-manifest\"", StringComparison.Ordinal);
        Throws(() => LearningPackageManifestFormat.Parse(Encoding.UTF8.GetBytes(duplicated)), "duplicada");
    }

    private static LearningContentPackage Package() => new()
    {
        Package = new LearningPackageIdentity
        {
            Id = "enem-matematica-fundamentos",
            Version = "1.0.0",
            Title = "Matemática — Fundamentos",
            Locale = "pt-BR"
        },
        Catalog = new LearningCatalog
        {
            Subjects = new List<LearningSubject> { new() { Id = "matematica", Name = "Matemática" } },
            Courses = new List<LearningCourse> { new() { Id = "mat-fundamentos", SubjectId = "matematica", Name = "Fundamentos", Position = 1 } },
            Modules = new List<LearningModule> { new() { Id = "mat-equacoes", CourseId = "mat-fundamentos", Name = "Equações", Position = 1 } },
            Skills = new List<LearningSkill> { new() { Id = "mat-equacoes-simples", SubjectId = "matematica", Name = "Resolver equações" } },
            Lessons = new List<LearningLesson>
            {
                new() { Id = "mat-equacoes-aula", ModuleId = "mat-equacoes", Name = "Equações de primeiro grau", Position = 1, SkillIds = new() { "mat-equacoes-simples" } }
            },
            Contents = new List<LearningContent>
            {
                new() { Id = "mat-equacoes-isolar", LessonId = "mat-equacoes-aula", Title = "Isolando a incógnita", Position = 1 }
            }
        }
    };

    private static void Throws(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException exception) when (exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected InvalidDataException containing '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
