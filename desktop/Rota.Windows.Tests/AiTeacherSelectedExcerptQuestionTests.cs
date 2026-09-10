using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherSelectedExcerptQuestionTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher selected excerpt question keeps excerpt and student question", FormatsSelectedExcerptQuestion),
        ("Teacher selected excerpt question rejects empty excerpt", RejectsEmptyExcerpt),
        ("Teacher selected excerpt question rejects empty question", RejectsEmptyQuestion),
        ("Teacher selected excerpt question rejects oversized excerpt", RejectsOversizedExcerpt),
        ("Teacher selected excerpt question rejects oversized combined input", RejectsOversizedCombinedInput)
    };
    private static void RejectsOversizedCombinedInput()
    {
        Expect<AiContractValidationException>(() =>
        AiTeacherSelectedExcerptQuestion.Create(
        new string('q', 3000),
        new string('a', 1500)));
    }
    private static void RejectsOversizedExcerpt()
    {
        Expect<AiContractValidationException>(() =>
        AiTeacherSelectedExcerptQuestion.Create(
        "Explique isso.",
        new string('a', AiTeacherSelectedExcerptQuestion.MaximumExcerptCharacters + 1)));
    }
    private static void FormatsSelectedExcerptQuestion()
    {
        var result = AiTeacherSelectedExcerptQuestion.Create(
            "Por que isso acontece?",
            "Uma razão compara duas quantidades.");

        Require(result.Contains("Uma razão compara duas quantidades.", StringComparison.Ordinal));
        Require(result.Contains("Por que isso acontece?", StringComparison.Ordinal));
        Require(result.Length <= AiTeacherContractValidator.MaximumQuestionCharacters);
    }

    private static void RejectsEmptyExcerpt()
    {
        Expect<AiContractValidationException>(() =>
            AiTeacherSelectedExcerptQuestion.Create(
                "Explique isso.",
                "   "));
    }
    private static void RejectsEmptyQuestion()
    {
        Expect<AiContractValidationException>(() =>
        AiTeacherSelectedExcerptQuestion.Create(
        "   ",
        "Uma razão compara duas quantidades."));
    }
    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Teacher selected excerpt question assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}