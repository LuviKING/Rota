using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningContentPackageTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package serializes a portable strict format", SerializesPortableFormat),
        ("Learning package round-trips its pedagogical hierarchy", RoundTripsHierarchy),
        ("Learning package rejects unknown JSON properties", RejectsUnknownProperties),
        ("Learning package rejects duplicate JSON properties", RejectsDuplicateProperties),
        ("Learning package rejects incomplete course material", RejectsIncompleteMaterial),
        ("Learning package copy does not share mutable data", CopyDoesNotShareMutableData)
    };

    private static void SerializesPortableFormat()
    {
        var json = Encoding.UTF8.GetString(LearningContentPackageFormat.Serialize(Package()));
        Require(json.Contains("\"format\": \"rota-learning-package\"", StringComparison.Ordinal), "format was not serialized");
        Require(json.Contains("\"schema_version\": 1", StringComparison.Ordinal), "schema version was not serialized");
        Require(json.Contains("\"subject_id\": \"matematica\"", StringComparison.Ordinal), "nested fields must use the portable naming convention");
    }

    private static void RoundTripsHierarchy()
    {
        var parsed = LearningContentPackageFormat.Parse(LearningContentPackageFormat.Serialize(Package()));
        Require(parsed.Package.Id == "enem-matematica-fundamentos", "package identity changed");
        Require(parsed.Catalog.Lessons.Single().SkillIds.Single() == "mat-equacoes-simples", "lesson skill changed");
        Require(parsed.Catalog.Contents.Single().Id == "mat-equacoes-isolar", "content identity changed");
    }

    private static void RejectsUnknownProperties()
    {
        var root = JsonNode.Parse(LearningContentPackageFormat.Serialize(Package()))!.AsObject();
        root["unexpected"] = true;
        Throws(() => LearningContentPackageFormat.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())), "JSON inválido");
    }

    private static void RejectsDuplicateProperties()
    {
        var json = Encoding.UTF8.GetString(LearningContentPackageFormat.Serialize(Package()));
        var duplicated = json.Replace("\"format\": \"rota-learning-package\"", "\"format\": \"rota-learning-package\",\n  \"format\": \"rota-learning-package\"", StringComparison.Ordinal);
        Throws(() => LearningContentPackageFormat.Parse(Encoding.UTF8.GetBytes(duplicated)), "duplicada");
    }

    private static void RejectsIncompleteMaterial()
    {
        var package = Package();
        package.Catalog.Contents.Clear();
        Throws(() => LearningContentPackageFormat.Serialize(package), "trilha completa");
    }

    private static void CopyDoesNotShareMutableData()
    {
        var original = Package();
        var copy = original.Copy();
        copy.Catalog.Lessons[0].SkillIds.Clear();
        Require(original.Catalog.Lessons[0].SkillIds.Count == 1, "package copy shared lesson skills");
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
