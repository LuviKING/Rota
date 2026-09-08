namespace Rota.Desktop.LocalAI;

/// <summary>
/// Tipo de referência interna exposta junto da resposta da Professora Local.
/// Esses valores são produzidos pelo Rota a partir do contexto validado; o modelo
/// não escolhe nem inventa fontes.
/// </summary>
public enum AiTeacherSourceKind
{
    Content,
    TheoryMaterial,
    PrerequisiteReference
}

public enum AiTeacherGroundingConfidence
{
    Unavailable,
    Limited,
    Medium,
    High
}

public sealed record AiTeacherSourceReference
{
    public AiTeacherSourceKind Kind { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string ParentContentId { get; init; } = "";
}

public sealed record AiTeacherGroundingMetadata
{
    public IReadOnlyList<AiTeacherSourceReference> Sources { get; init; } = Array.Empty<AiTeacherSourceReference>();
    public AiTeacherGroundingConfidence Confidence { get; init; } = AiTeacherGroundingConfidence.Unavailable;
    public string ConfidenceReason { get; init; } = "";
}

/// <summary>
/// Envelope pronto para apresentação. A resposta textual vem do backend local;
/// Sources e Grounding são anexados depois, deterministicamente, pelo Rota.
/// </summary>
public sealed record AiTeacherGroundedAnswer
{
    public AiTeacherAnswer Answer { get; init; } = new();
    public AiTeacherGroundingMetadata Grounding { get; init; } = new();
    public AiTeacherKnowledgeDisclosure Knowledge { get; init; } = new();
}

/// <summary>
/// Calcula proveniência e nível de confiança exclusivamente com dados já validados
/// do pacote pedagógico. O nível descreve a cobertura do grounding interno; não é
/// uma probabilidade de que toda frase gerada pelo modelo esteja correta.
/// </summary>
public static class AiTeacherGroundingMetadataFactory
{
    public const int MaximumSources = 14;

    public static AiTeacherGroundingMetadata Create(AiTeacherLessonContext? context)
    {
        if (context is null)
        {
            return new AiTeacherGroundingMetadata
            {
                Sources = Array.Empty<AiTeacherSourceReference>(),
                Confidence = AiTeacherGroundingConfidence.Unavailable,
                ConfidenceReason = "Nenhum contexto pedagógico interno foi usado nesta resposta."
            };
        }

        AiTeacherContractValidator.ValidateLessonContext(context);
        var sources = new List<AiTeacherSourceReference>(MaximumSources)
        {
            new()
            {
                Kind = AiTeacherSourceKind.Content,
                Id = context.ContentId,
                Title = context.ContentTitle,
                ParentContentId = ""
            }
        };

        if (context.Theory is { } theory)
        {
            sources.Add(new AiTeacherSourceReference
            {
                Kind = AiTeacherSourceKind.TheoryMaterial,
                Id = theory.MaterialId,
                Title = theory.Title,
                ParentContentId = context.ContentId
            });
        }

        foreach (var prerequisite in context.Prerequisites)
        {
            if (sources.Count >= MaximumSources)
                break;
            sources.Add(new AiTeacherSourceReference
            {
                Kind = AiTeacherSourceKind.PrerequisiteReference,
                Id = prerequisite.ContentId,
                Title = prerequisite.Title,
                ParentContentId = context.ContentId
            });
        }

        var confidence = DetermineConfidence(context);
        return new AiTeacherGroundingMetadata
        {
            Sources = sources.AsReadOnly(),
            Confidence = confidence,
            ConfidenceReason = confidence switch
            {
                AiTeacherGroundingConfidence.High =>
                    "O Rota forneceu material interno verificado sem truncamento do recorte usado nesta aula.",
                AiTeacherGroundingConfidence.Medium =>
                    "O Rota forneceu material interno verificado, mas parte do recorte foi limitada para caber no contexto seguro.",
                AiTeacherGroundingConfidence.Limited =>
                    "O pacote interno identifica o conteúdo, mas não forneceu material teórico suficiente neste recorte.",
                _ => "Nenhum contexto pedagógico interno foi usado nesta resposta."
            }
        };
    }

    /// <summary>
    /// Confere a proveniência antes de ela chegar à interface. Isso impede que um
    /// backend alternativo, um teste ou uma resposta do modelo transforme a
    /// indicação de fonte/confiança em uma afirmação livre.
    /// </summary>
    public static void Validate(
        AiTeacherGroundingMetadata grounding,
        AiTeacherLessonContext? context)
    {
        ArgumentNullException.ThrowIfNull(grounding);
        var expected = Create(context);
        if (grounding.Sources is null ||
            !grounding.Sources.SequenceEqual(expected.Sources) ||
            grounding.Confidence != expected.Confidence ||
            !string.Equals(grounding.ConfidenceReason, expected.ConfidenceReason, StringComparison.Ordinal))
        {
            throw new AiContractValidationException(
                "A proveniência pedagógica da Professora Local não corresponde ao contexto interno verificado.");
        }
    }

    public static string GetConfidenceLabel(AiTeacherGroundingConfidence confidence) => confidence switch
    {
        AiTeacherGroundingConfidence.High => "Alta",
        AiTeacherGroundingConfidence.Medium => "Média",
        AiTeacherGroundingConfidence.Limited => "Limitada",
        AiTeacherGroundingConfidence.Unavailable => "Indisponível",
        _ => throw new AiContractValidationException("O nível de confiança pedagógica não é suportado.")
    };

    public static string GetSourceKindLabel(AiTeacherSourceKind kind) => kind switch
    {
        AiTeacherSourceKind.Content => "Conteúdo",
        AiTeacherSourceKind.TheoryMaterial => "Material teórico",
        AiTeacherSourceKind.PrerequisiteReference => "Pré-requisito",
        _ => throw new AiContractValidationException("O tipo de fonte pedagógica não é suportado.")
    };

    private static AiTeacherGroundingConfidence DetermineConfidence(AiTeacherLessonContext context)
    {
        if (context.Theory is null)
            return AiTeacherGroundingConfidence.Limited;
        if (context.HasMorePrerequisites || context.Theory.HasMoreSections)
            return AiTeacherGroundingConfidence.Medium;
        return AiTeacherGroundingConfidence.High;
    }
}
