using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class LocalAiCompositionTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Local AI production composition stays inert until explicitly used", CompositionStaysInert),
        ("Local AI production composition validates paths and disposes centrally", CompositionValidatesAndDisposes)
    };

    private static void CompositionStaysInert()
    {
        WithPaths((repository, aiRoot) =>
        {
            Require(!Directory.Exists(aiRoot));
            var services = LocalAiServices.Create(repository, aiRoot);
            try
            {
                Require(services.RootDirectory == Path.GetFullPath(aiRoot));
                Require(services.ConfigurationStore.ConfigurationPath == Path.Combine(aiRoot, "config.json"));
                Require(services.ProposalStore.StorePath == Path.Combine(aiRoot, "proposals.json"));
                Require(services.ConversationStore.StorePath == Path.Combine(aiRoot, "conversation.json"));
                Require(services.EnemCatalog.CatalogVersion == EnemCatalogService.CurrentCatalogVersion);
                Require(services.ModelManager.RootDirectory == aiRoot);
                Require(services.HardwareDiagnostics is not null);
                Require(services.RuntimeHost.Status.State == AiRuntimeState.Stopped);
                Require(services.RuntimeHost.Connection is null);
                Require(!Directory.Exists(aiRoot));
            }
            finally
            {
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            Require(!Directory.Exists(aiRoot));
        });
    }

    private static void CompositionValidatesAndDisposes()
    {
        WithPaths((repository, aiRoot) =>
        {
            Expect<AiContractValidationException>(() => LocalAiServices.Create(repository, "relative-ai-root"));
            var services = LocalAiServices.Create(repository, aiRoot);
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Expect<ObjectDisposedException>(() => services.Workflow.PrepareAsync(
                new AiAssistantInput { FreeText = "Teste" },
                AiProposalKind.StudyPlan).GetAwaiter().GetResult());
        });
    }

    private static void WithPaths(Action<StudyRepository, string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaLocalAiCompositionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new StudyRepository(Path.Combine(directory, "state.json"));
            action(repository, Path.Combine(directory, "AI"));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Local AI composition assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
