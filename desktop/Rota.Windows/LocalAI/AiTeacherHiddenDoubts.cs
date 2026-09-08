namespace Rota.Desktop.LocalAI;

/// <summary>
/// Categorias fechadas de possíveis dúvidas implícitas observáveis na pergunta do aluno.
/// Elas descrevem somente o raciocínio necessário para a resposta atual; não são
/// diagnósticos pessoais, memória do aluno nem evidência de domínio ou deficiência.
/// </summary>
public enum AiTeacherHiddenDoubtKind
{
    Prerequisite = 0,
    ConceptConfusion = 1,
    NotationOrVocabulary = 2,
    ProceduralReasoning = 3
}

public sealed record AiTeacherHiddenDoubtKindDescriptor(
    AiTeacherHiddenDoubtKind Kind,
    string Id,
    string DisplayName,
    string Description);

/// <summary>
/// Catálogo canônico das categorias usadas no contrato JSON da professora local.
/// IDs são estáveis porque fazem parte do protocolo entre o Rota e o runtime local.
/// </summary>
public static class AiTeacherHiddenDoubtKinds
{
    public const string PrerequisiteId = "prerequisite";
    public const string ConceptConfusionId = "concept_confusion";
    public const string NotationOrVocabularyId = "notation_or_vocabulary";
    public const string ProceduralReasoningId = "procedural_reasoning";

    private static readonly AiTeacherHiddenDoubtKindDescriptor PrerequisiteDescriptor = new(
        AiTeacherHiddenDoubtKind.Prerequisite,
        PrerequisiteId,
        "Pré-requisito",
        "A pergunta sugere que um conhecimento anterior pode estar faltando para compreender o conteúdo atual.");

    private static readonly AiTeacherHiddenDoubtKindDescriptor ConceptConfusionDescriptor = new(
        AiTeacherHiddenDoubtKind.ConceptConfusion,
        ConceptConfusionId,
        "Confusão de conceitos",
        "A pergunta mistura, troca ou aproxima conceitos que precisam ser distinguidos antes da resposta principal.");

    private static readonly AiTeacherHiddenDoubtKindDescriptor NotationOrVocabularyDescriptor = new(
        AiTeacherHiddenDoubtKind.NotationOrVocabulary,
        NotationOrVocabularyId,
        "Notação ou vocabulário",
        "A formulação indica que um símbolo, termo ou modo de ler a linguagem da matéria pode não estar claro.");

    private static readonly AiTeacherHiddenDoubtKindDescriptor ProceduralReasoningDescriptor = new(
        AiTeacherHiddenDoubtKind.ProceduralReasoning,
        ProceduralReasoningId,
        "Raciocínio do procedimento",
        "A pergunta sugere que o aluno pode conhecer um passo mecânico sem compreender por que ele funciona.");

    private static readonly IReadOnlyList<AiTeacherHiddenDoubtKindDescriptor> Descriptors = Array.AsReadOnly(new[]
    {
        PrerequisiteDescriptor,
        ConceptConfusionDescriptor,
        NotationOrVocabularyDescriptor,
        ProceduralReasoningDescriptor
    });

    public static IReadOnlyList<AiTeacherHiddenDoubtKindDescriptor> All => Descriptors;

    public static bool IsSupported(AiTeacherHiddenDoubtKind kind) => kind is
        AiTeacherHiddenDoubtKind.Prerequisite or
        AiTeacherHiddenDoubtKind.ConceptConfusion or
        AiTeacherHiddenDoubtKind.NotationOrVocabulary or
        AiTeacherHiddenDoubtKind.ProceduralReasoning;

    public static string GetId(AiTeacherHiddenDoubtKind kind) => Get(kind).Id;

    public static AiTeacherHiddenDoubtKindDescriptor Get(AiTeacherHiddenDoubtKind kind) => kind switch
    {
        AiTeacherHiddenDoubtKind.Prerequisite => PrerequisiteDescriptor,
        AiTeacherHiddenDoubtKind.ConceptConfusion => ConceptConfusionDescriptor,
        AiTeacherHiddenDoubtKind.NotationOrVocabulary => NotationOrVocabularyDescriptor,
        AiTeacherHiddenDoubtKind.ProceduralReasoning => ProceduralReasoningDescriptor,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A categoria de dúvida implícita não é suportada.")
    };

    public static bool TryParseId(string? id, out AiTeacherHiddenDoubtKind kind)
    {
        kind = id switch
        {
            PrerequisiteId => AiTeacherHiddenDoubtKind.Prerequisite,
            ConceptConfusionId => AiTeacherHiddenDoubtKind.ConceptConfusion,
            NotationOrVocabularyId => AiTeacherHiddenDoubtKind.NotationOrVocabulary,
            ProceduralReasoningId => AiTeacherHiddenDoubtKind.ProceduralReasoning,
            _ => default
        };
        return id is PrerequisiteId or ConceptConfusionId or NotationOrVocabularyId or ProceduralReasoningId;
    }
}

