using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningPracticeQuestionTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package preserves corrected practice questions", PreservesPracticeQuestion),
        ("Learning practice question rejects a missing answer option", RejectsMissingAnswerOption),
        ("Learning practice question rejects invalid content links", RejectsInvalidContentLink),
        ("Learning practice question copy is independent", CopyIsIndependent)
    };

    private static void PreservesPracticeQuestion()
    {
        var package = Package();
        package.PracticeQuestions.Add(Question());

        var parsed = LearningContentPackageFormat.Parse(LearningContentPackageFormat.Serialize(package));
        var question = parsed.PracticeQuestions.Single();
        Require(question.CorrectOptionId == "opcao-b", "answer key was not preserved");
        Require(question.Explanation.Contains("3 nos dois lados", StringComparison.Ordinal), "explanation was not preserved");
    }

    private static void RejectsMissingAnswerOption()
    {
        var package = Package();
        var question = Question();
        question.CorrectOptionId = "opcao-inexistente";
        package.PracticeQuestions.Add(question);
        Throws(() => LearningContentPackageFormat.Serialize(package), "gabarito");
    }

    private static void RejectsInvalidContentLink()
    {
        var package = Package();
        var question = Question();
        question.ContentId = "conteudo-inventado";
        package.PracticeQuestions.Add(question);
        Throws(() => LearningContentPackageFormat.Serialize(package), "conteúdo existente");
    }

    private static void CopyIsIndependent()
    {
        var question = Question();
        var copy = question.Copy();
        copy.Options[0].Text = "Outro valor";
        Require(question.Options[0].Text != copy.Options[0].Text, "question copy shared option data");
    }

    private static LearningPracticeQuestion Question() => new()
    {
        Id = "mat-equacoes-isolar-q1",
        ContentId = "mat-equacoes-isolar",
        Prompt = "Qual é o valor de x em x + 3 = 8?",
        Options = new()
        {
            new() { Id = "opcao-a", Text = "x = 3" },
            new() { Id = "opcao-b", Text = "x = 5" },
            new() { Id = "opcao-c", Text = "x = 8" }
        },
        CorrectOptionId = "opcao-b",
        Explanation = "Subtraia 3 nos dois lados da igualdade: x = 8 - 3, então x = 5.",
        Difficulty = LearningPracticeDifficulties.Introductory
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
