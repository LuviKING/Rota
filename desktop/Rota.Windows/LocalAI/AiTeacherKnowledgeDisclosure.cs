namespace Rota.Desktop.LocalAI;

/// <summary>
/// Estado determinístico da cobertura de conhecimento para a resposta atual.
/// Ele descreve somente se o recorte interno verificado permite uma resposta
/// substantiva; não é uma medida de inteligência, certeza geral ou domínio do aluno.
/// </summary>
public enum AiTeacherKnowledgeStatus
{
    Supported,
    InsufficientEvidence
}

/// <summary>
/// Metadados de transparência anexados pelo Rota depois da validação do contexto.
/// O modelo local não recebe autoridade para escolher este estado ou a mensagem.
/// </summary>
public sealed record AiTeacherKnowledgeDisclosure
{
    public AiTeacherKnowledgeStatus Status { get; init; } = AiTeacherKnowledgeStatus.InsufficientEvidence;
    public bool CanAnswerSubstantively { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>
/// Decide e valida a disponibilidade de evidência exclusivamente a partir do
/// contexto pedagógico já verificado. Sem teoria interna, a produção não inicia
/// inferência: responde com uma limitação estruturada em vez de completar a lacuna
/// com memória paramétrica ou uma fonte externa presumida.
/// </summary>
public static class AiTeacherKnowledgeDisclosureFactory
{
    public const string SupportedReason =
        "O Rota forneceu material teórico interno verificado para responder dentro deste recorte.";
    public const string InsufficientEvidenceReason =
        "O pacote interno não forneceu material teórico suficiente para responder a esta pergunta com segurança.";

    public static AiTeacherKnowledgeDisclosure Create(AiTeacherLessonContext? context) =>
        Build(context);

    public static void Validate(
        AiTeacherKnowledgeDisclosure disclosure,
        AiTeacherLessonContext? context)
    {
        ArgumentNullException.ThrowIfNull(disclosure);
        var expected = Build(context);
        if (disclosure.Status != expected.Status ||
            disclosure.CanAnswerSubstantively != expected.CanAnswerSubstantively ||
            !string.Equals(disclosure.Reason, expected.Reason, StringComparison.Ordinal))
        {
            throw new AiContractValidationException(
                "O estado de conhecimento da Professora Local não corresponde ao contexto interno verificado.");
        }
    }

    public static string GetStatusLabel(AiTeacherKnowledgeStatus status) => status switch
    {
        AiTeacherKnowledgeStatus.Supported => "Material interno suficiente",
        AiTeacherKnowledgeStatus.InsufficientEvidence => "Ainda não sei com segurança",
        _ => throw new AiContractValidationException("O estado de conhecimento pedagógico não é suportado.")
    };

    /// <summary>
    /// Produz uma resposta segura e útil para a interface sem executar o modelo.
    /// A mensagem não interpreta a pergunta do aluno nem a repete, impedindo que
    /// texto não confiável controle a resposta de limitação.
    /// </summary>
    public static AiTeacherAnswer CreateInsufficientEvidenceAnswer(
        AiTeacherRequest request,
        AiTeacherManual manual)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(manual);
        var context = request.LessonContext ??
            throw new AiContractValidationException(
                "Uma resposta pedagógica sem evidência precisa preservar o contexto validado.");

        var baseAnswer = new AiTeacherAnswer
        {
            Title = "Ainda não sei com segurança",
            Introduction =
                "Não tenho material interno verificado suficiente para explicar este ponto sem inventar informação.",
            Steps = new[]
            {
                new AiTeacherStep
                {
                    Number = 1,
                    Title = "Próximo passo seguro",
                    Explanation =
                        "Adicione ou atualize o material teórico deste conteúdo no pacote pedagógico e tente novamente."
                }
            },
            Recap =
                "Prefiro reconhecer esta limitação a preencher a lacuna com conhecimento não verificado.",
            Limitations = new[] { InsufficientEvidenceReason },
            Mode = request.Mode,
            ExplanationStyle = request.ExplanationStyle,
            ManualId = manual.ManualId,
            ManualVersion = manual.ManualVersion,
            ManualFingerprintSha256 = manual.FingerprintSha256,
            UsedLessonContext = true,
            ContentId = context.ContentId
        };

        if (request.Mode != AiTeacherRequestMode.GuidedCorrection)
            return baseAnswer;

        return baseAnswer with
        {
            CorrectionFeedback = new AiTeacherCorrectionFeedback
            {
                WhatIsWorking =
                    "Você trouxe uma tentativa para estudar, mas o pacote atual não permite confirmá-la com segurança.",
                FirstIssue =
                    "Não há material teórico interno suficiente para verificar o procedimento ou o resultado.",
                Hint =
                    "Não use uma resposta presumida; procure o material verificado do conteúdo antes de continuar.",
                NextAction =
                    "Depois que o material for atualizado, faça uma nova tentativa e envie o seu raciocínio.",
                RequiresStudentRetry = true,
                FinalAnswerDisclosed = false
            }
        };
    }

    private static AiTeacherKnowledgeDisclosure Build(AiTeacherLessonContext? context)
    {
        if (context is not null)
            AiTeacherContractValidator.ValidateLessonContext(context);

        return context?.Theory is null
            ? new AiTeacherKnowledgeDisclosure
            {
                Status = AiTeacherKnowledgeStatus.InsufficientEvidence,
                CanAnswerSubstantively = false,
                Reason = InsufficientEvidenceReason
            }
            : new AiTeacherKnowledgeDisclosure
            {
                Status = AiTeacherKnowledgeStatus.Supported,
                CanAnswerSubstantively = true,
                Reason = SupportedReason
            };
    }
}
