using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningPracticeQuestionTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package preserves corrected practice questions", PreservesPracticeQuestion),
        ("Learning practice question rejects a missing answer option", RejectsMissingAnswerOption),
        ("Learning practice question rejects invalid content links", RejectsInvalidContentLink),
        ("Learning practice question copy is independent", CopyIsIndependent),
        ("Learning practice grader uses only the package answer key", GraderUsesPackageAnswerKey),
        ("Learning practice grader rejects an unknown option", GraderRejectsUnknownOption),
        ("Learning question attempts persist and can be undone", QuestionAttemptsPersistAndUndo),
        ("Learning question attempts reject non UTC timestamps without mutation", QuestionAttemptsRejectNonUtc)
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

    private static void GraderUsesPackageAnswerKey()
    {
        var question = Question();
        var correct = LearningPracticeQuestionGrader.Grade(question, "opcao-b");
        var wrong = LearningPracticeQuestionGrader.Grade(question, "opcao-a");

        Require(correct.IsCorrect, "correct package option was not accepted");
        Require(!wrong.IsCorrect, "wrong package option was accepted");
        Require(wrong.CorrectOptionId == question.CorrectOptionId, "grader did not preserve package answer key");
        Require(wrong.CorrectOptionText == "x = 5", "grader did not expose the package answer text");
        Require(wrong.Explanation == question.Explanation, "grader did not preserve the package explanation");
    }

    private static void GraderRejectsUnknownOption()
    {
        Throws(() => LearningPracticeQuestionGrader.Grade(Question(), "opcao-inexistente"), "não pertence");
    }

    private static void QuestionAttemptsPersistAndUndo()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rota-learning-question-attempts-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "question-attempts.json");
        try
        {
            var store = new LearningQuestionAttemptStore(path);
            store.Record("mat-equacoes-isolar-q1", "mat-equacoes-isolar", "opcao-a", false,
                new DateTimeOffset(2026, 9, 7, 20, 0, 0, TimeSpan.Zero));
            store.Record("mat-equacoes-isolar-q1", "mat-equacoes-isolar", "opcao-b", true,
                new DateTimeOffset(2026, 9, 7, 20, 5, 0, TimeSpan.Zero));

            var reloaded = new LearningQuestionAttemptStore(path);
            var beforeUndo = reloaded.Snapshot().Attempts;
            Require(beforeUndo.Count == 2, "attempt history did not persist");
            Require(beforeUndo[0].SelectedOptionId == "opcao-a" && !beforeUndo[0].IsCorrect, "first attempt changed after reload");
            Require(beforeUndo[1].SelectedOptionId == "opcao-b" && beforeUndo[1].IsCorrect, "second attempt changed after reload");

            Require(reloaded.UndoLatest("mat-equacoes-isolar-q1"), "latest question attempt was not removed");
            var afterUndo = new LearningQuestionAttemptStore(path).Snapshot().Attempts;
            Require(afterUndo.Count == 1 && afterUndo[0].SelectedOptionId == "opcao-a", "undo removed the wrong attempt");
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void QuestionAttemptsRejectNonUtc()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rota-learning-question-attempts-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "question-attempts.json");
        try
        {
            var store = new LearningQuestionAttemptStore(path);
            try
            {
                store.Record("mat-equacoes-isolar-q1", "mat-equacoes-isolar", "opcao-b", true,
                    new DateTimeOffset(2026, 9, 7, 20, 0, 0, TimeSpan.FromHours(-3)));
            }
            catch (ArgumentException exception) when (exception.Message.Contains("UTC", StringComparison.OrdinalIgnoreCase))
            {
                Require(store.Snapshot().Attempts.Count == 0, "invalid attempt mutated in-memory history");
                Require(!File.Exists(path), "invalid attempt created a persistence file");
                return;
            }
            throw new InvalidOperationException("Expected non-UTC attempt to be rejected.");
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch { }
        }
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
