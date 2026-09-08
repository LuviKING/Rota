using System.Globalization;
using Rota.Desktop;

namespace Rota.Desktop.LocalAI;

/// <summary>
/// Operações aritméticas deliberadamente fechadas que o Rota consegue conferir sem
/// delegar o resultado ao modelo. Operações fora deste catálogo não devem ser
/// apresentadas como "verificadas por código".
/// </summary>
public enum AiTeacherCalculationOperation
{
    Add,
    Subtract,
    Multiply,
    Divide
}

public sealed record AiTeacherCalculationClaim
{
    public AiTeacherCalculationOperation Operation { get; init; }
    public decimal LeftOperand { get; init; }
    public decimal RightOperand { get; init; }
    public decimal ClaimedResult { get; init; }
}

public sealed record AiTeacherCalculationValidation
{
    public AiTeacherCalculationClaim Claim { get; init; } = new();
    public decimal ComputedResult { get; init; }
    public bool IsCorrect { get; init; }
    public string CanonicalExpression { get; init; } = "";
}

/// <summary>
/// Resultado de uma resposta objetiva conferida exclusivamente contra o gabarito do
/// pacote já validado. O gabarito não precisa ser enviado ao modelo para esta checagem.
/// </summary>
public sealed record AiTeacherObjectiveAnswerValidation
{
    public string QuestionId { get; init; } = "";
    public string SelectedOptionId { get; init; } = "";
    public string CorrectOptionId { get; init; } = "";
    public bool IsCorrect { get; init; }
}

/// <summary>
/// Fronteira determinística para fatos que o Rota pode conferir por código. Esta
/// classe não interpreta linguagem natural, não consulta internet, não chama IA e
/// não recebe autoridade sobre calendário, progresso ou histórico.
/// </summary>
public static class AiTeacherDeterministicValidator
{
    public const decimal MaximumAbsoluteOperand = 1_000_000_000_000m;
    public const decimal MaximumAbsoluteResult = 1_000_000_000_000_000_000m;

    public static AiTeacherCalculationValidation ValidateCalculation(AiTeacherCalculationClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateOperation(claim.Operation);
        ValidateMagnitude(claim.LeftOperand, MaximumAbsoluteOperand, "operando esquerdo");
        ValidateMagnitude(claim.RightOperand, MaximumAbsoluteOperand, "operando direito");
        ValidateMagnitude(claim.ClaimedResult, MaximumAbsoluteResult, "resultado declarado");

        decimal computed;
        try
        {
            computed = claim.Operation switch
            {
                AiTeacherCalculationOperation.Add => checked(claim.LeftOperand + claim.RightOperand),
                AiTeacherCalculationOperation.Subtract => checked(claim.LeftOperand - claim.RightOperand),
                AiTeacherCalculationOperation.Multiply => checked(claim.LeftOperand * claim.RightOperand),
                AiTeacherCalculationOperation.Divide when claim.RightOperand != 0m =>
                    checked(claim.LeftOperand / claim.RightOperand),
                AiTeacherCalculationOperation.Divide =>
                    throw new AiContractValidationException("A validação determinística não aceita divisão por zero."),
                _ => throw new AiContractValidationException("A operação de cálculo não é suportada pelo validador determinístico.")
            };
        }
        catch (OverflowException ex)
        {
            throw new AiContractValidationException("O cálculo excede o intervalo numérico seguro do validador determinístico.", ex);
        }

        ValidateMagnitude(computed, MaximumAbsoluteResult, "resultado calculado");
        return new AiTeacherCalculationValidation
        {
            Claim = claim,
            ComputedResult = computed,
            IsCorrect = computed == claim.ClaimedResult,
            CanonicalExpression = BuildCanonicalExpression(claim, computed)
        };
    }

    /// <summary>
    /// Confere uma alternativa sem IA, reutilizando exatamente o grader canônico da
    /// área Aprender. A explicação e o texto do gabarito não são copiados para este
    /// envelope para evitar ampliar acidentalmente o contexto da professora.
    /// </summary>
    public static AiTeacherObjectiveAnswerValidation ValidateObjectiveAnswer(
        LearningPracticeQuestion question,
        string selectedOptionId)
    {
        ArgumentNullException.ThrowIfNull(question);
        var result = LearningPracticeQuestionGrader.Grade(question, selectedOptionId);
        return new AiTeacherObjectiveAnswerValidation
        {
            QuestionId = result.QuestionId,
            SelectedOptionId = result.SelectedOptionId,
            CorrectOptionId = result.CorrectOptionId,
            IsCorrect = result.IsCorrect
        };
    }

    private static void ValidateOperation(AiTeacherCalculationOperation operation)
    {
        if (!Enum.IsDefined(operation))
            throw new AiContractValidationException("A operação de cálculo não é suportada pelo validador determinístico.");
    }

    private static void ValidateMagnitude(decimal value, decimal maximum, string field)
    {
        // Comparação por intervalo evita Math.Abs(decimal.MinValue), que estoura antes
        // de podermos converter a entrada hostil em uma falha controlada do contrato.
        if (value > maximum || value < -maximum)
            throw new AiContractValidationException($"O {field} excede o limite numérico seguro da validação determinística.");
    }

    private static string BuildCanonicalExpression(AiTeacherCalculationClaim claim, decimal computed)
    {
        var symbol = claim.Operation switch
        {
            AiTeacherCalculationOperation.Add => "+",
            AiTeacherCalculationOperation.Subtract => "-",
            AiTeacherCalculationOperation.Multiply => "×",
            AiTeacherCalculationOperation.Divide => "÷",
            _ => throw new AiContractValidationException("A operação de cálculo não é suportada pelo validador determinístico.")
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{claim.LeftOperand:G29} {symbol} {claim.RightOperand:G29} = {computed:G29}");
    }
}
