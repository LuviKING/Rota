using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningCatalogTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning catalog accepts a complete stable hierarchy", AcceptsCompleteHierarchy),
        ("Learning catalog rejects broken parent references", RejectsBrokenReferences),
        ("Learning catalog rejects cyclic prerequisites", RejectsCyclicPrerequisites),
        ("Learning catalog copies without sharing mutable data", CopyDoesNotShareMutableData)
    };

    private static void AcceptsCompleteHierarchy() => LearningCatalogValidator.Validate(Example());

    private static void RejectsBrokenReferences()
    {
        var catalog = Example();
        catalog.Courses[0].SubjectId = "inexistente";
        Throws(() => LearningCatalogValidator.Validate(catalog), "referência");
    }

    private static void RejectsCyclicPrerequisites()
    {
        var catalog = Example();
        catalog.Contents.Add(new LearningContent
        {
            Id = "mat-equacoes-2",
            LessonId = "mat-equacoes-aula",
            Title = "Equações com incógnitas dos dois lados",
            Position = 2
        });
        catalog.Prerequisites.Add(new LearningPrerequisite
        {
            ContentId = "mat-equacoes-1",
            RequiredContentId = "mat-equacoes-2"
        });
        catalog.Prerequisites.Add(new LearningPrerequisite
        {
            ContentId = "mat-equacoes-2",
            RequiredContentId = "mat-equacoes-1"
        });
        Throws(() => LearningCatalogValidator.Validate(catalog), "ciclo");
    }

    private static void CopyDoesNotShareMutableData()
    {
        var copy = Example().Copy();
        copy.Lessons[0].SkillIds.Add("outra-habilidade");
        if (Example().Lessons[0].SkillIds.Count != 1)
            throw new InvalidOperationException("The fixture was unexpectedly shared.");
        var original = Example();
        var independentCopy = original.Copy();
        independentCopy.Lessons[0].SkillIds.Clear();
        if (original.Lessons[0].SkillIds.Count != 1)
            throw new InvalidOperationException("The catalog copy shared lesson skills.");
    }

    private static LearningCatalog Example() => new()
    {
        Subjects = new List<LearningSubject>
        {
            new() { Id = "matematica", Name = "Matemática" }
        },
        Courses = new List<LearningCourse>
        {
            new() { Id = "mat-fundamentos", SubjectId = "matematica", Name = "Fundamentos", Level = "Ensino médio", Position = 1 }
        },
        Modules = new List<LearningModule>
        {
            new() { Id = "mat-equacoes", CourseId = "mat-fundamentos", Name = "Equações", Position = 1 }
        },
        Lessons = new List<LearningLesson>
        {
            new() { Id = "mat-equacoes-aula", ModuleId = "mat-equacoes", Name = "Equações de primeiro grau", Position = 1, SkillIds = new List<string> { "mat-resolver-equacao" } }
        },
        Contents = new List<LearningContent>
        {
            new() { Id = "mat-equacoes-1", LessonId = "mat-equacoes-aula", Title = "Isolando a incógnita", Position = 1 }
        },
        Skills = new List<LearningSkill>
        {
            new() { Id = "mat-resolver-equacao", SubjectId = "matematica", Name = "Resolver equações lineares" }
        }
    };

    private static void Throws(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException exception) when (exception.Message.Contains(message, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected InvalidDataException containing '{message}'.");
    }
}
