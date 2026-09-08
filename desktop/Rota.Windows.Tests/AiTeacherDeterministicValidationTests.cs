using System.Globalization;
using Rota.Desktop;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherDeterministicValidationTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher deterministic validator checks the four supported arithmetic operations", ArithmeticOperationsAreDeterministic),
        ("Teacher deterministic validator rejects an incorrect claimed result", IncorrectClaimIsNotVerified),
        ("Teacher deterministic validator rejects division by zero", DivisionByZeroFailsClosed),
        ("Teacher deterministic validator rejects unsupported operations", UnsupportedOperationFailsClosed),
        ("Teacher deterministic validator enforces numeric bounds", NumericBoundsFailClosed),
        ("Teacher deterministic calculation formatting is culture invariant", CalculationFormattingIsInvariant),
        ("Teacher objective validation reuses only the package answer key", ObjectiveValidationUsesPackageAnswerKey),
        ("Teacher objective validation rejects unknown alternatives", ObjectiveValidationRejectsUnknownOption),
        ("Teacher deterministic validation does not mutate package questions", ObjectiveValidationIsReadOnly)
    };

    private static void ArithmeticOperationsAreDeterministic()
    {
        Check(AiTeacherCalculationOperation.Add, 12.5m, 7.25m, 19.75m);
        Check(AiTeacherCalculationOperation.Subtract, 12.5m, 7.25m, 5.25m);
        Check(AiTeacherCalculationOperation.Multiply, -3m, 2.5m, -7.5m);
        Check(AiTeacherCalculationOperation.Divide, 10m, 4m, 2.5m);
    }

    private static void IncorrectClaimIsNotVerified()
    {
        var validation = AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = AiTeacherCalculationOperation.Add,
            LeftOperand = 2m,
            RightOperand = 2m,
            ClaimedResult = 5m
        });

        Require(!validation.IsCorrect, "an incorrect arithmetic claim was marked as verified");
        Require(validation.ComputedResult == 4m, "the authoritative computed result is wrong");
        Require(validation.CanonicalExpression == "2 + 2 = 4", "the canonical expression is unstable");
    }

    private static void DivisionByZeroFailsClosed()
    {
        Expect<AiContractValidationException>(() => AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = AiTeacherCalculationOperation.Divide,
            LeftOperand = 10m,
            RightOperand = 0m,
            ClaimedResult = 0m
        }));
    }

    private static void UnsupportedOperationFailsClosed()
    {
        Expect<AiContractValidationException>(() => AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = (AiTeacherCalculationOperation)999,
            LeftOperand = 1m,
            RightOperand = 1m,
            ClaimedResult = 2m
        }));
    }

    private static void NumericBoundsFailClosed()
    {
        Expect<AiContractValidationException>(() => AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = AiTeacherCalculationOperation.Add,
            LeftOperand = AiTeacherDeterministicValidator.MaximumAbsoluteOperand + 1m,
            RightOperand = 1m,
            ClaimedResult = 1m
        }));

        Expect<AiContractValidationException>(() => AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = AiTeacherCalculationOperation.Add,
            LeftOperand = decimal.MinValue,
            RightOperand = 0m,
            ClaimedResult = 0m
        }));

        Expect<AiContractValidationException>(() => AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = AiTeacherCalculationOperation.Multiply,
            LeftOperand = AiTeacherDeterministicValidator.MaximumAbsoluteOperand,
            RightOperand = AiTeacherDeterministicValidator.MaximumAbsoluteOperand,
            ClaimedResult = 0m
        }));
    }

    private static void CalculationFormattingIsInvariant()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
            var validation = AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
            {
                Operation = AiTeacherCalculationOperation.Divide,
                LeftOperand = 5m,
                RightOperand = 2m,
                ClaimedResult = 2.5m
            });
            Require(validation.CanonicalExpression == "5 ÷ 2 = 2.5", "canonical math formatting depended on the UI culture");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void ObjectiveValidationUsesPackageAnswerKey()
    {
        var question = Question();
        var correct = AiTeacherDeterministicValidator.ValidateObjectiveAnswer(question, "opcao-b");
        var wrong = AiTeacherDeterministicValidator.ValidateObjectiveAnswer(question, "opcao-a");

        Require(correct.IsCorrect, "the package answer key was not accepted");
        Require(!wrong.IsCorrect, "a wrong alternative was accepted");
        Require(correct.CorrectOptionId == question.CorrectOptionId, "the deterministic result did not preserve the package answer id");
        Require(correct.QuestionId == question.Id, "the deterministic result changed the question identity");
    }

    private static void ObjectiveValidationRejectsUnknownOption()
    {
        Expect<InvalidDataException>(() =>
            AiTeacherDeterministicValidator.ValidateObjectiveAnswer(Question(), "opcao-inexistente"));
    }

    private static void ObjectiveValidationIsReadOnly()
    {
        var question = Question();
        var before = question.Copy();

        _ = AiTeacherDeterministicValidator.ValidateObjectiveAnswer(question, "opcao-a");

        Require(question.Id == before.Id, "validation changed the question id");
        Require(question.ContentId == before.ContentId, "validation changed the content id");
        Require(question.Prompt == before.Prompt, "validation changed the prompt");
        Require(question.CorrectOptionId == before.CorrectOptionId, "validation changed the answer key");
        Require(question.Explanation == before.Explanation, "validation changed the package explanation");
        Require(question.Options.Select(option => (option.Id, option.Text)).SequenceEqual(
            before.Options.Select(option => (option.Id, option.Text))), "validation changed the alternatives");
    }

    private static void Check(
        AiTeacherCalculationOperation operation,
        decimal left,
        decimal right,
        decimal expected)
    {
        var validation = AiTeacherDeterministicValidator.ValidateCalculation(new AiTeacherCalculationClaim
        {
            Operation = operation,
            LeftOperand = left,
            RightOperand = right,
            ClaimedResult = expected
        });
        Require(validation.IsCorrect, $"{operation} was not verified");
        Require(validation.ComputedResult == expected, $"{operation} computed an unexpected result");
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

    private static void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
