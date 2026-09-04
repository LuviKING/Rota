namespace Rota.Desktop.LocalAI;

public sealed class AiModelCatalog : IAiModelCatalog
{
    public const int CurrentCatalogVersion = 1;

    private static readonly IReadOnlyList<AiModelDescriptor> DefaultModels = Array.AsReadOnly(new[]
    {
        new AiModelDescriptor(
            "qwen3-1.7b-q4-k-m",
            "Qwen3 1.7B Q4_K_M",
            AiProfile.Lightweight,
            "1.7B",
            "Q4_K_M",
            "qwen3-1.7b-q4-k-m.gguf",
            4096,
            IsRecommended: true),
        new AiModelDescriptor(
            "qwen3-4b-q4-k-m",
            "Qwen3 4B Q4_K_M",
            AiProfile.Balanced,
            "4B",
            "Q4_K_M",
            "qwen3-4b-q4-k-m.gguf",
            4096,
            IsRecommended: true),
        new AiModelDescriptor(
            "qwen3-8b-q4-k-m",
            "Qwen3 8B Q4_K_M",
            AiProfile.Performance,
            "8B",
            "Q4_K_M",
            "qwen3-8b-q4-k-m.gguf",
            8192,
            IsRecommended: true)
    });

    private readonly IReadOnlyList<AiModelDescriptor> _models;
    private readonly IReadOnlyDictionary<string, AiModelDescriptor> _modelsById;

    public static AiModelCatalog Default { get; } = new(DefaultModels);

    public int Version => CurrentCatalogVersion;
    public IReadOnlyList<AiModelDescriptor> Models => _models;

    public AiModelCatalog(IEnumerable<AiModelDescriptor> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var materialized = models.ToArray();
        Validate(materialized);
        _models = Array.AsReadOnly(materialized);
        _modelsById = materialized.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AiModelDescriptor> GetCompatibleModels(AiProfile profile)
    {
        ValidateConcreteProfile(profile);
        return Array.AsReadOnly(_models.Where(model => model.Profile == profile).ToArray());
    }

    public AiModelDescriptor GetRecommendedModel(AiProfile profile)
    {
        ValidateConcreteProfile(profile);
        return _models.Single(model => model.Profile == profile && model.IsRecommended);
    }

    public AiModelDescriptor GetById(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId) || !_modelsById.TryGetValue(modelId, out var model))
            throw new AiContractValidationException("O modelo selecionado não pertence ao catálogo local compatível do Rota.");
        return model;
    }

    private static void Validate(IReadOnlyList<AiModelDescriptor> models)
    {
        if (models.Count == 0)
            throw new AiContractValidationException("O catálogo local de modelos não pode ficar vazio.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (model is null)
                throw new AiContractValidationException("O catálogo local contém um modelo nulo.");
            ValidateIdentifier(model.Id, "ID do modelo", 160);
            ValidateText(model.DisplayName, "Nome do modelo", 160);
            ValidateConcreteProfile(model.Profile);
            ValidateIdentifier(model.ParameterClass, "Classe de parâmetros", 32);
            ValidateIdentifier(model.Quantization, "Quantização", 32);

            if (!ids.Add(model.Id))
                throw new AiContractValidationException("O catálogo local contém IDs de modelo duplicados.");
            if (string.IsNullOrWhiteSpace(model.FileName) ||
                model.FileName.Length > 240 ||
                !string.Equals(Path.GetFileName(model.FileName), model.FileName, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(model.FileName), ".gguf", StringComparison.OrdinalIgnoreCase))
            {
                throw new AiContractValidationException("O catálogo local contém um nome de arquivo de modelo inseguro.");
            }
            if (!fileNames.Add(model.FileName))
                throw new AiContractValidationException("O catálogo local contém nomes de arquivo duplicados.");
            if (model.DefaultContextSize is < 512 or > 131_072)
                throw new AiContractValidationException("O catálogo local contém um contexto de modelo inválido.");
        }

        foreach (var profile in ConcreteProfiles())
        {
            if (models.Count(model => model.Profile == profile && model.IsRecommended) != 1)
                throw new AiContractValidationException($"O perfil {profile} precisa ter exatamente um modelo recomendado.");
        }
    }

    private static void ValidateConcreteProfile(AiProfile profile)
    {
        if (!Enum.IsDefined(profile) || profile == AiProfile.Automatic)
            throw new AiContractValidationException("O catálogo exige um perfil de IA concreto.");
    }

    private static IEnumerable<AiProfile> ConcreteProfiles()
    {
        yield return AiProfile.Lightweight;
        yield return AiProfile.Balanced;
        yield return AiProfile.Performance;
    }

    private static void ValidateIdentifier(string? value, string field, int maxLength)
    {
        ValidateText(value, field, maxLength);
        if (value!.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new AiContractValidationException($"{field} contém caracteres inválidos.");
    }

    private static void ValidateText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new AiContractValidationException($"{field} contém texto inválido.");
    }
}
