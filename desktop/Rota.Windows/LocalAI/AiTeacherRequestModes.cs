namespace Rota.Desktop.LocalAI;

/// <summary>
/// Modos pedagógicos suportados pela Professora Local. O modo de correção guiada
/// analisa somente a tentativa atual do aluno e exige uma nova tentativa, sem
/// entregar ou confirmar a resposta final.
/// </summary>
public enum AiTeacherRequestMode
{
    Explain = 0,
    GuidedCorrection = 1
}

public static class AiTeacherRequestModes
{
    // Mantém o wire contract já usado pelos blocos anteriores.
    public const string ExplainId = "step_by_step_explanation";
    public const string GuidedCorrectionId = "guided_correction_without_answer";

    public static bool IsSupported(AiTeacherRequestMode mode) =>
        mode is AiTeacherRequestMode.Explain or AiTeacherRequestMode.GuidedCorrection;

    public static string GetId(AiTeacherRequestMode mode) => mode switch
    {
        AiTeacherRequestMode.Explain => ExplainId,
        AiTeacherRequestMode.GuidedCorrection => GuidedCorrectionId,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

/// <summary>
/// Metadados estruturados da correção guiada. São preenchidos pelo modelo sob
/// schema estrito e revalidados pelo Rota. A resposta final nunca pode ser marcada
/// como entregue e o fluxo sempre termina pedindo uma nova tentativa do aluno.
/// </summary>
public sealed record AiTeacherCorrectionFeedback
{
    public string WhatIsWorking { get; init; } = "";
    public string FirstIssue { get; init; } = "";
    public string Hint { get; init; } = "";
    public string NextAction { get; init; } = "";
    public bool RequiresStudentRetry { get; init; }
    public bool FinalAnswerDisclosed { get; init; }
}

