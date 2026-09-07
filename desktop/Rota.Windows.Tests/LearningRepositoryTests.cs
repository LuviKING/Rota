using System.Text.Json;
using System.Text.Json.Nodes;
using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningRepositoryTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Existing study state migrates to the learning foundation", ExistingStateMigrates),
        ("Learning catalog persists independently of calendar data", LearningCatalogPersists),
        ("Invalid learning catalog cannot mutate local state", InvalidCatalogIsTransactional)
    };

    private static void ExistingStateMigrates()
    {
        WithRepository((repo, path) =>
        {
            var plan = new PlanPackage("legacy-plan", 1, "Plano legado", "Objetivo", "2027-01-20", new[]
            {
                new SessionItem
                {
                    Id = "legacy-session", PlanId = "legacy-plan", PlanRevision = 1, Date = "2027-01-10",
                    Subject = "Matemática", Topic = "Equações", Minutes = 60, Target = "Resolver exercícios"
                }
            });
            if (!repo.ApplyPlan(plan).Success) throw new InvalidOperationException("Could not create the legacy calendar.");

            var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            node["StateVersion"] = 1;
            node.Remove("LearningCatalog");
            File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var migrated = new StudyRepository(path, () => new DateTime(2027, 1, 1, 9, 0, 0));
            if (migrated.SessionsForDate(new DateOnly(2027, 1, 10)).Single().Id != "legacy-session")
                throw new InvalidOperationException("The migration changed the existing calendar.");
            if (migrated.LearningCatalog.SchemaVersion != LearningCatalog.CurrentSchemaVersion)
                throw new InvalidOperationException("The learning catalog was not created by migration.");
            var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (saved["StateVersion"]?.GetValue<int>() != 2 || saved["LearningCatalog"] is null)
                throw new InvalidOperationException("The migrated schema was not persisted.");
        });
    }

    private static void LearningCatalogPersists()
    {
        WithRepository((repo, path) =>
        {
            var catalog = Catalog();
            repo.SaveLearningCatalog(catalog);
            var reopened = new StudyRepository(path, () => new DateTime(2027, 1, 1, 9, 0, 0));
            if (reopened.LearningCatalog.Contents.Single().Id != "mat-equacoes-isolar")
                throw new InvalidOperationException("The learning catalog did not survive restart.");
        });
    }

    private static void InvalidCatalogIsTransactional()
    {
        WithRepository((repo, path) =>
        {
            var before = File.ReadAllText(path);
            var invalid = Catalog();
            invalid.Subjects.Add(new LearningSubject { Id = "matematica", Name = "Duplicada" });
            try
            {
                repo.SaveLearningCatalog(invalid);
            }
            catch (InvalidDataException)
            {
                if (File.ReadAllText(path) == before && repo.LearningCatalog.Subjects.Count == 0) return;
                throw new InvalidOperationException("The invalid catalog partially changed the repository.");
            }
            throw new InvalidOperationException("The invalid catalog was accepted.");
        });
    }

    private static LearningCatalog Catalog() => new()
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
    };

    private static void WithRepository(Action<StudyRepository, string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rota-learning-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "desktop-state.json");
        try
        {
            body(new StudyRepository(path, () => new DateTime(2027, 1, 1, 9, 0, 0)), path);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
