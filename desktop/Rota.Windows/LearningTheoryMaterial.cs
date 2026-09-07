namespace Rota.Desktop;

/// <summary>
/// Material explicativo local ligado a um conteúdo do catálogo. Ele contém apenas
/// texto pedagógico estruturado; não aceita HTML, scripts ou instruções executáveis.
/// </summary>
public sealed class LearningTheoryMaterial
{
    public string Id { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public string LearningGoal { get; set; } = "";
    public List<LearningTheorySection> Sections { get; set; } = new();

    public LearningTheoryMaterial Copy() => new()
    {
        Id = Id,
        ContentId = ContentId,
        Title = Title,
        LearningGoal = LearningGoal,
        Sections = Sections.Select(section => section.Copy()).ToList()
    };
}

public sealed class LearningTheorySection
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Position { get; set; }

    public LearningTheorySection Copy() => new()
    {
        Kind = Kind,
        Title = Title,
        Body = Body,
        Position = Position
    };
}

public static class LearningTheorySectionKinds
{
    public const string Explanation = "explanation";
    public const string WorkedExample = "worked_example";
    public const string Tip = "tip";
    public const string Warning = "warning";
    public const string Recap = "recap";

    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        Explanation, WorkedExample, Tip, Warning, Recap
    };

    public static bool IsValid(string? value) => value is not null && Values.Contains(value);
}

public static class LearningTheoryMaterialValidator
{
    public const int MaximumMaterials = 10_000;
    private const int MaximumSectionsPerMaterial = 30;

    public static void Validate(IReadOnlyList<LearningTheoryMaterial>? materials, LearningCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(catalog);
        if (materials.Count > MaximumMaterials)
            throw new InvalidDataException("O pacote possui materiais teóricos demais.");

        var knownContents = catalog.Contents.Select(content => content.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var contentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var material in materials)
        {
            if (material is null || material.Sections is null)
                throw new InvalidDataException("O pacote contém um material teórico incompleto.");
            if (!LearningCatalogIds.IsValid(material.Id) || !ids.Add(material.Id))
                throw new InvalidDataException("O identificador do material teórico é inválido ou duplicado.");
            if (!LearningCatalogIds.IsValid(material.ContentId) || !knownContents.Contains(material.ContentId) || !contentIds.Add(material.ContentId))
                throw new InvalidDataException("O material teórico precisa referenciar um único conteúdo existente.");
            ValidateText(material.Title, "título do material teórico", 160, allowEmpty: false);
            ValidateText(material.LearningGoal, "objetivo de aprendizagem", 600, allowEmpty: false);
            if (material.Sections.Count is < 1 or > MaximumSectionsPerMaterial)
                throw new InvalidDataException("O material teórico precisa ter entre uma e trinta seções.");

            var positions = new HashSet<int>();
            var hasExplanation = false;
            foreach (var section in material.Sections)
            {
                if (section is null || !LearningTheorySectionKinds.IsValid(section.Kind))
                    throw new InvalidDataException("O tipo de seção teórica é inválido.");
                if (section.Kind == LearningTheorySectionKinds.Explanation) hasExplanation = true;
                ValidateText(section.Title, "título da seção teórica", 160, allowEmpty: false);
                ValidateText(section.Body, "texto da seção teórica", 6_000, allowEmpty: false);
                if (section.Position is < 1 or > MaximumSectionsPerMaterial || !positions.Add(section.Position))
                    throw new InvalidDataException("A posição da seção teórica é inválida ou duplicada.");
            }
            if (!hasExplanation)
                throw new InvalidDataException("O material teórico precisa ter ao menos uma explicação.");
        }
    }

    private static void ValidateText(string? value, string field, int maximum, bool allowEmpty)
    {
        if (value is null || value.Length > maximum || value.Any(char.IsControl) ||
            (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidDataException($"O campo {field} é inválido.");
        }
    }
}
