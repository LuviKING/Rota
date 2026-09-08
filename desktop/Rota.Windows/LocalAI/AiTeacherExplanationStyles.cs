namespace Rota.Desktop.LocalAI;

/// <summary>
/// Estilos pedagógicos fixos da professora local. O estilo altera somente a forma
/// da explicação; fontes, validações, gabaritos e autoridade operacional continuam
/// regidos pelo manual canônico e pelo código determinístico do Rota.
/// </summary>
public enum AiTeacherExplanationStyle
{
    StepByStep = 0,
    Simple = 1,
    Visual = 2,
    Detailed = 3
}

public sealed record AiTeacherExplanationStyleDescriptor(
    AiTeacherExplanationStyle Style,
    string Id,
    string DisplayName,
    string Description,
    string SystemInstruction);

public static class AiTeacherExplanationStyles
{
    public const string StepByStepId = "step_by_step";
    public const string SimpleId = "simple";
    public const string VisualId = "visual";
    public const string DetailedId = "detailed";

    private static readonly AiTeacherExplanationStyleDescriptor StepByStepDescriptor = new(
        AiTeacherExplanationStyle.StepByStep,
        StepByStepId,
        "Passo a passo",
        "Explicação equilibrada, sequencial e cumulativa.",
        "Construa uma sequência equilibrada e cumulativa. Em cada passo, explique o que está acontecendo e por que esse passo é necessário antes de avançar.");

    private static readonly AiTeacherExplanationStyleDescriptor SimpleDescriptor = new(
        AiTeacherExplanationStyle.Simple,
        SimpleId,
        "Simples",
        "Vocabulário cotidiano, frases curtas e um conceito por vez.",
        "Use vocabulário comum, frases curtas e um conceito principal por passo. Defina termos técnicos antes de usá-los e evite jargão quando uma palavra simples preservar o significado.");

    private static readonly AiTeacherExplanationStyleDescriptor VisualDescriptor = new(
        AiTeacherExplanationStyle.Visual,
        VisualId,
        "Visual",
        "Relações espaciais, comparações e pequenos esquemas textuais quando ajudarem.",
        "Priorize relações espaciais, comparações, sequências e pequenos esquemas textuais quando isso realmente ajudar a compreensão. Não afirme que exibiu imagem, animação ou diagrama real; a explicação precisa continuar completa em texto.");

    private static readonly AiTeacherExplanationStyleDescriptor DetailedDescriptor = new(
        AiTeacherExplanationStyle.Detailed,
        DetailedId,
        "Detalhado",
        "Mais etapas intermediárias, justificativas e verificações sem ampliar o escopo.",
        "Explicite etapas intermediárias, justificativas, conexões e verificações relevantes. Aumente a profundidade sem ampliar o assunto além da pergunta e do material disponível, e registre limitações quando faltar evidência.");

    private static readonly IReadOnlyList<AiTeacherExplanationStyleDescriptor> Descriptors = Array.AsReadOnly(new[]
    {
        StepByStepDescriptor,
        SimpleDescriptor,
        VisualDescriptor,
        DetailedDescriptor
    });

    public static IReadOnlyList<AiTeacherExplanationStyleDescriptor> All => Descriptors;

    public static bool IsSupported(AiTeacherExplanationStyle style) => style is
        AiTeacherExplanationStyle.StepByStep or
        AiTeacherExplanationStyle.Simple or
        AiTeacherExplanationStyle.Visual or
        AiTeacherExplanationStyle.Detailed;

    public static AiTeacherExplanationStyleDescriptor Get(AiTeacherExplanationStyle style) => style switch
    {
        AiTeacherExplanationStyle.StepByStep => StepByStepDescriptor,
        AiTeacherExplanationStyle.Simple => SimpleDescriptor,
        AiTeacherExplanationStyle.Visual => VisualDescriptor,
        AiTeacherExplanationStyle.Detailed => DetailedDescriptor,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "O estilo de explicação não é suportado.")
    };
}

