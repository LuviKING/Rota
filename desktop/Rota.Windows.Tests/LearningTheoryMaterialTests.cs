using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningTheoryMaterialTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package preserves structured teaching material", PreservesStructuredTheory),
        ("Learning theory material rejects invalid content references", RejectsInvalidContentReference),
        ("Learning theory material requires a real explanation", RequiresExplanation),
        ("Learning theory material copy is independent", CopyIsIndependent)
    };

    private static void PreservesStructuredTheory()
    {
        var package = Package();
        package.TheoryMaterials.Add(Material());

        var parsed = LearningContentPackageFormat.Parse(LearningContentPackageFormat.Serialize(package));
        var material = parsed.TheoryMaterials.Single();
        Require(material.ContentId == "mat-equacoes-isolar", "theory material was not linked to its content");
        Require(material.Sections[1].Kind == LearningTheorySectionKinds.WorkedExample, "worked example was not preserved");
    }

    private static void RejectsInvalidContentReference()
    {
        var package = Package();
        var material = Material();
        material.ContentId = "conteudo-inventado";
        package.TheoryMaterials.Add(material);
        Throws(() => LearningContentPackageFormat.Serialize(package), "conteúdo existente");
    }

    private static void RequiresExplanation()
    {
        var package = Package();
        var material = Material();
        material.Sections[0].Kind = LearningTheorySectionKinds.Tip;
        package.TheoryMaterials.Add(material);
        Throws(() => LearningContentPackageFormat.Serialize(package), "ao menos uma explicação");
    }

    private static void CopyIsIndependent()
    {
        var material = Material();
        var copy = material.Copy();
        copy.Sections[0].Body = "Outro texto";
        Require(material.Sections[0].Body != copy.Sections[0].Body, "theory material copy shared section data");
    }

    private static LearningTheoryMaterial Material() => new()
    {
        Id = "mat-equacoes-isolar-teoria",
        ContentId = "mat-equacoes-isolar",
        Title = "Como isolar a incógnita",
        LearningGoal = "Resolver equações simples preservando a igualdade.",
        Sections = new()
        {
            new() { Kind = LearningTheorySectionKinds.Explanation, Title = "Ideia central", Body = "Faça a mesma operação nos dois lados da igualdade.", Position = 1 },
            new() { Kind = LearningTheorySectionKinds.WorkedExample, Title = "Exemplo", Body = "Em x + 3 = 8, subtraia 3 nos dois lados e obtenha x = 5.", Position = 2 },
            new() { Kind = LearningTheorySectionKinds.Recap, Title = "Resumo", Body = "O equilíbrio da igualdade deve ser mantido a cada passo.", Position = 3 }
        }
    };

    private static LearningContentPackage Package() => new()
    {
        Package = new LearningPackageIdentity { Id = "enem-matematica-fundamentos", Version = "1.0.0", Title = "Matemática — Fundamentos", Locale = "pt-BR" },
        Catalog = new LearningCatalog
        {
            Subjects = new() { new() { Id = "matematica", Name = "Matemática" } },
            Courses = new() { new() { Id = "mat-fundamentos", SubjectId = "matematica", Name = "Fundamentos", Position = 1 } },
            Modules = new() { new() { Id = "mat-equacoes", CourseId = "mat-fundamentos", Name = "Equações", Position = 1 } },
            Skills = new() { new() { Id = "mat-equacoes-simples", SubjectId = "matematica", Name = "Resolver equações" } },
            Lessons = new() { new() { Id = "mat-equacoes-aula", ModuleId = "mat-equacoes", Name = "Equações de primeiro grau", Position = 1, SkillIds = new() { "mat-equacoes-simples" } } },
            Contents = new() { new() { Id = "mat-equacoes-isolar", LessonId = "mat-equacoes-aula", Title = "Isolando a incógnita", Position = 1 } }
        }
    };

    private static void Throws(Action action, string expectedMessage)
    {
        try { action(); }
        catch (InvalidDataException exception) when (exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected InvalidDataException containing '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
