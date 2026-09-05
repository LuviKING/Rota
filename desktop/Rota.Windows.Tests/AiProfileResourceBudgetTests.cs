using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

internal static class AiProfileResourceBudgetTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Local AI resource budgets cap the product at Performance", BudgetsAreStable),
        ("Pinned models fit their profile resource budgets", PinnedModelsFitBudgets)
    };

    private static void BudgetsAreStable()
    {
        Require(AiProfileResourceBudgets.All.Count == 3);
        var lightweight = AiProfileResourceBudgets.Get(AiProfile.Lightweight);
        Require(lightweight.MinimumSystemMemoryBytes == 8 * AiProfileResourceBudgets.Gibibyte);
        Require(lightweight.MaximumModelArtifactBytes == 2 * AiProfileResourceBudgets.Gibibyte);
        Require(lightweight.MaximumContextSize == 4_096);
        Require(lightweight.RecommendedCompute == AiComputePreference.Cpu);

        var performance = AiProfileResourceBudgets.Get(AiProfile.Performance);
        Require(performance.MinimumSystemMemoryBytes == 15 * AiProfileResourceBudgets.Gibibyte);
        Require(performance.MinimumDedicatedGpuMemoryBytes == 15 * AiProfileResourceBudgets.Gibibyte / 2);
        Require(performance.MaximumModelArtifactBytes == 6 * AiProfileResourceBudgets.Gibibyte);
        Require(performance.MaximumContextSize == 8_192);
        Require(performance.RecommendedCompute == AiComputePreference.Gpu);
        Expect<AiContractValidationException>(() => AiProfileResourceBudgets.Get(AiProfile.Automatic));
    }

    private static void PinnedModelsFitBudgets()
    {
        var manifest = AiInstallationManifest.Default;
        foreach (var package in manifest.ModelPackages)
        {
            var model = AiModelCatalog.Default.GetById(package.ModelId);
            AiProfileResourceBudgets.ValidateModel(model, package.Artifact.ExpectedSizeBytes);
        }

        var lightweight = AiModelCatalog.Default.GetRecommendedModel(AiProfile.Lightweight);
        Expect<AiContractValidationException>(() => AiProfileResourceBudgets.ValidateModel(
            lightweight,
            AiProfileResourceBudgets.Get(AiProfile.Lightweight).MaximumModelArtifactBytes + 1));
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
