using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningProgressStoreTests
{
    public static readonly (string Name, Action Body)[] Cases = { ("Learning progress persists completion and assessment results", PersistsProgress) };

    private static void PersistsProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rota-progress-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "progress.json");
        try
        {
            var store = new LearningProgressStore(path);
            store.MarkContentCompleted("mat-conteudo");
            store.RecordAssessment("mat-teste", 80, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
            var loaded = new LearningProgressStore(path).Snapshot();
            if (!loaded.CompletedContentIds.Contains("mat-conteudo") || loaded.AssessmentAttempts.Single().ScorePercentage != 80)
                throw new InvalidOperationException("progress was not persisted");
        }
        finally { try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { } }
    }
}
