namespace Rota.Desktop.LocalAI;

public sealed record AiProfileResourceBudget(
    AiProfile Profile,
    long MinimumSystemMemoryBytes,
    long MinimumDedicatedGpuMemoryBytes,
    long MaximumModelArtifactBytes,
    int MaximumContextSize,
    AiComputePreference RecommendedCompute);

public static class AiProfileResourceBudgets
{
    public const long Gibibyte = 1024L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<AiProfile, AiProfileResourceBudget> Budgets =
        new Dictionary<AiProfile, AiProfileResourceBudget>
        {
            [AiProfile.Lightweight] = new(
                AiProfile.Lightweight,
                8 * Gibibyte,
                0,
                2 * Gibibyte,
                4_096,
                AiComputePreference.Cpu),
            [AiProfile.Balanced] = new(
                AiProfile.Balanced,
                12 * Gibibyte,
                0,
                3 * Gibibyte,
                4_096,
                AiComputePreference.Cpu),
            [AiProfile.Performance] = new(
                AiProfile.Performance,
                15 * Gibibyte,
                15 * Gibibyte / 2,
                6 * Gibibyte,
                8_192,
                AiComputePreference.Gpu)
        };

    public static IReadOnlyCollection<AiProfileResourceBudget> All => Budgets.Values.ToArray();

    public static AiProfileResourceBudget Get(AiProfile profile) =>
        Budgets.TryGetValue(profile, out var budget)
            ? budget
            : throw new AiContractValidationException("O orçamento exige um perfil de IA concreto.");

    public static void ValidateModel(AiModelDescriptor model, long artifactBytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = Get(model.Profile);
        if (artifactBytes <= 0 || artifactBytes > budget.MaximumModelArtifactBytes)
            throw new AiContractValidationException($"O modelo {model.DisplayName} excede o orçamento do perfil {model.Profile}.");
        if (model.DefaultContextSize > budget.MaximumContextSize)
            throw new AiContractValidationException($"O contexto padrão de {model.DisplayName} excede o orçamento do perfil {model.Profile}.");
    }
}
