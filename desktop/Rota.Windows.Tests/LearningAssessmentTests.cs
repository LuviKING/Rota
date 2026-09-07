using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningAssessmentTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning assessment calculates score and incorrect questions locally", CalculatesResult),
        ("Learning assessment rejects question references outside its package", RejectsUnknownQuestion),
        ("Learning assessment rejects answers outside the assessment", RejectsUnsafeAnswers),
        ("Learning package preserves assessment definitions", PreservesAssessmentDefinition)
    };

    private static void CalculatesResult()
    {
        var result = LearningAssessmentGrader.Grade(
            Assessment(),
            Questions(),
            new Dictionary<string, string> { ["mat-q1"] = "b", ["mat-q2"] = "a" });

        Require(result.CorrectAnswers == 1, "correct answer count is wrong");
        Require(result.TotalQuestions == 2 && result.ScorePercentage == 50, "score is wrong");
        Require(!result.Passed && result.IncorrectQuestionIds.Single() == "mat-q2", "incorrect result details are wrong");
    }

    private static void RejectsUnknownQuestion()
    {
        var assessment = Assessment();
        assessment.QuestionIds[1] = "questao-inventada";
        Throws(() => LearningAssessmentValidator.Validate(new[] { assessment }, Questions()), "questão inexistente");
    }

    private static void RejectsUnsafeAnswers()
    {
        var answers = new Dictionary<string, string> { ["questao-de-fora"] = "a" };
        Throws(() => LearningAssessmentGrader.Grade(Assessment(), Questions(), answers), "não pertence");
    }

    private static void PreservesAssessmentDefinition()
    {
        var package = Package();
        package.PracticeQuestions = Questions();
        package.Assessments.Add(Assessment());
        var parsed = LearningContentPackageFormat.Parse(LearningContentPackageFormat.Serialize(package));
        Require(parsed.Assessments.Single().TimeLimitMinutes == 20, "assessment time was not preserved");
        Require(parsed.Assessments.Single().QuestionIds.Count == 2, "assessment question list was not preserved");
    }

    private static LearningAssessment Assessment() => new()
    {
        Id = "mat-equacoes-teste-1",
        Title = "Verificação: equações simples",
        Instructions = "Resolva as duas questões sem consultar o resumo.",
        QuestionIds = new() { "mat-q1", "mat-q2" },
        TimeLimitMinutes = 20,
        PassingScorePercentage = 60
    };

    private static List<LearningPracticeQuestion> Questions() => new()
    {
        new() { Id = "mat-q1", ContentId = "mat-equacoes-isolar", Prompt = "x + 3 = 8", Options = new() { new() { Id = "a", Text = "3" }, new() { Id = "b", Text = "5" } }, CorrectOptionId = "b", Explanation = "Subtraia 3 dos dois lados.", Difficulty = LearningPracticeDifficulties.Basic },
        new() { Id = "mat-q2", ContentId = "mat-equacoes-isolar", Prompt = "x - 2 = 9", Options = new() { new() { Id = "a", Text = "9" }, new() { Id = "b", Text = "11" } }, CorrectOptionId = "b", Explanation = "Some 2 aos dois lados.", Difficulty = LearningPracticeDifficulties.Basic }
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
