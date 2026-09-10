namespace Rota.Desktop.LocalAI;

/// <summary>
/// Monta uma pergunta pedagógica limitada a partir de um trecho que o aluno
/// selecionou na aula e da dúvida escrita por ele. O trecho continua sendo dado
/// não confiável e nunca recebe autoridade operacional.
/// </summary>
public static class AiTeacherSelectedExcerptQuestion
{
    public const int MaximumExcerptCharacters = 1_500;

    public static string Create(string question, string selectedExcerpt)
    {
        var normalizedQuestion = (question ?? string.Empty).Trim();
        var normalizedExcerpt = (selectedExcerpt ?? string.Empty).Trim();

        if (normalizedQuestion.Length == 0)
            throw new AiContractValidationException(
                "Escreva uma pergunta sobre o trecho selecionado.");

        if (normalizedExcerpt.Length == 0)
            throw new AiContractValidationException(
                "Selecione um trecho da aula antes de fazer esta pergunta.");

        if (normalizedExcerpt.Length > MaximumExcerptCharacters)
            throw new AiContractValidationException(
                $"O trecho selecionado deve ter no máximo {MaximumExcerptCharacters:N0} caracteres.");

        var result =
            "Trecho selecionado da aula:\n" +
            normalizedExcerpt +
            "\n\nPergunta do aluno sobre esse trecho:\n" +
            normalizedQuestion;

        if (result.Length > AiTeacherContractValidator.MaximumQuestionCharacters)
            throw new AiContractValidationException(
                "O trecho selecionado e a pergunta juntos excedem o limite seguro da Professora Local.");

        return result;
    }
}