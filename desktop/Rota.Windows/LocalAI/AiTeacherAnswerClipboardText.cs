namespace Rota.Desktop.LocalAI;

/// <summary>
/// Monta o texto que o aluno copia por ação explícita. Somente a resposta já
/// exibida é incluída: a pergunta, o histórico e dados internos ficam de fora.
/// </summary>
public static class AiTeacherAnswerClipboardText
{
    public static string Create(AiTeacherAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var sections = new List<string>
        {
            answer.Title,
            answer.Introduction
        };
        foreach (var step in answer.Steps)
        {
            if (step is null) continue;
            sections.Add($"{step.Number}. {step.Title}{Environment.NewLine}{step.Explanation}");
        }
        if (!string.IsNullOrWhiteSpace(answer.Recap))
            sections.Add($"Resumo: {answer.Recap}");
        if (answer.Limitations.Count > 0)
            sections.Add("Limites desta resposta:" + Environment.NewLine +
                string.Join(Environment.NewLine, answer.Limitations.Select(item => $"• {item}")));

        return string.Join(Environment.NewLine + Environment.NewLine,
            sections.Where(item => !string.IsNullOrWhiteSpace(item)));
    }
}
