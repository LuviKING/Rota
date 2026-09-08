using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherAnswerClipboardTextTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher answer copy text contains only the displayed answer", FormatsDisplayedAnswer),
        ("Teacher answer copy text omits an empty limitations section", OmitsEmptyLimitations)
    };

    private static void FormatsDisplayedAnswer()
    {
        var text = AiTeacherAnswerClipboardText.Create(Answer() with
        {
            Limitations = new[] { "Use somente o material fornecido." }
        });

        Require(text.Contains("Explicação de razão", StringComparison.Ordinal));
        Require(text.Contains("1. Compare as quantidades", StringComparison.Ordinal));
        Require(text.Contains("Resumo: Razão compara duas quantidades.", StringComparison.Ordinal));
        Require(text.Contains("Limites desta resposta:", StringComparison.Ordinal));
        Require(!text.Contains("pergunta privada", StringComparison.OrdinalIgnoreCase));
    }

    private static void OmitsEmptyLimitations()
    {
        var text = AiTeacherAnswerClipboardText.Create(Answer());
        Require(!text.Contains("Limites desta resposta:", StringComparison.Ordinal));
    }

    private static AiTeacherAnswer Answer() => new()
    {
        Title = "Explicação de razão",
        Introduction = "Uma razão compara quantidades.",
        Steps = new[]
        {
            new AiTeacherStep { Number = 1, Title = "Compare as quantidades", Explanation = "Escreva a divisão correspondente." }
        },
        Recap = "Razão compara duas quantidades."
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher answer clipboard text assertion failed.");
    }
}
