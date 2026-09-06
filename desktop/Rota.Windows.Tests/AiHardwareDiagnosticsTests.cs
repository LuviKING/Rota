using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

internal static class AiHardwareDiagnosticsTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    public static readonly (string Name, Action Body)[] Cases =
    {
        ("AI hardware diagnostics combines CPU RAM GPU and storage", CombinesHardwareAndStorage),
        ("AI hardware diagnostics preserves a controlled storage warning", PreservesStorageWarning),
        ("AI hardware diagnostics rejects impossible storage readings", RejectsImpossibleStorage),
        ("AI hardware diagnostics honors cancellation", HonorsCancellation),
        ("Windows AI storage probe reads the real local drive", ReadsRealLocalDrive)
    };

    private static void CombinesHardwareAndStorage()
    {
        var hardware = Hardware();
        var storage = new AiStorageSnapshot(@"C:\Rota\AI", 1_000, 400);
        var service = new AiHardwareDiagnosticsService(
            new StubHardwareDetector(hardware),
            new StubStorageProbe(storage),
            () => CapturedAt);

        var report = service.AnalyzeAsync().GetAwaiter().GetResult();

        Require(ReferenceEquals(hardware, report.Hardware));
        Require(report.Storage == storage);
        Require(report.CapturedAtUtc == CapturedAt);
        Require(report.Warnings.Count == 0);
    }

    private static void PreservesStorageWarning()
    {
        var service = new AiHardwareDiagnosticsService(
            new StubHardwareDetector(Hardware(warnings: new[] { "Aviso de GPU." })),
            new StubStorageProbe(new AiStorageSnapshot(
                @"C:\Rota\AI",
                null,
                null,
                "Aviso de armazenamento.")));

        var report = service.AnalyzeAsync().GetAwaiter().GetResult();

        Require(report.Storage.AvailableBytes is null);
        Require(report.Warnings.SequenceEqual(new[] { "Aviso de GPU.", "Aviso de armazenamento." }));
    }

    private static void RejectsImpossibleStorage()
    {
        var service = new AiHardwareDiagnosticsService(
            new StubHardwareDetector(Hardware()),
            new StubStorageProbe(new AiStorageSnapshot(@"C:\Rota\AI", 100, 101)));

        Expect<AiContractValidationException>(() =>
            service.AnalyzeAsync().GetAwaiter().GetResult());
    }

    private static void HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new AiHardwareDiagnosticsService(
            new StubHardwareDetector(Hardware()),
            new StubStorageProbe(new AiStorageSnapshot(@"C:\Rota\AI", 100, 50)));

        Expect<OperationCanceledException>(() =>
            service.AnalyzeAsync(cancellation.Token).GetAwaiter().GetResult());
    }

    private static void ReadsRealLocalDrive()
    {
        var snapshot = new WindowsAiStorageProbe(Path.GetTempPath())
            .CaptureAsync()
            .GetAwaiter()
            .GetResult();

        Require(Path.IsPathFullyQualified(snapshot.RootPath));
        Require(snapshot.TotalBytes > 0);
        Require(snapshot.AvailableBytes >= 0);
        Require(snapshot.AvailableBytes <= snapshot.TotalBytes);
        Require(string.IsNullOrEmpty(snapshot.Warning));
    }

    private static AiHardwareProfile Hardware(IReadOnlyList<string>? warnings = null) => new(
        "AMD Ryzen 7 5700X",
        16,
        16L * AiProfileRecommendationPolicy.Gibibyte,
        "NVIDIA GeForce RTX 3070",
        8L * AiProfileRecommendationPolicy.Gibibyte,
        AiProfile.Performance,
        warnings ?? Array.Empty<string>());

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

    private sealed class StubHardwareDetector(AiHardwareProfile hardware) : IAiHardwareProfileDetector
    {
        public Task<AiHardwareProfile> DetectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(hardware);
        }
    }

    private sealed class StubStorageProbe(AiStorageSnapshot storage) : IAiStorageProbe
    {
        public Task<AiStorageSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(storage);
        }
    }
}
