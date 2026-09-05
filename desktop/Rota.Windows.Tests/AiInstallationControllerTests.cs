using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiInstallationControllerTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI installation controller prepares a transparent automatic plan without downloading", ControllerPreparesWithoutInstalling),
        ("AI installation controller respects a manual profile", ControllerRespectsManualProfile),
        ("AI installation controller requires the current confirmation", ControllerRequiresCurrentConfirmation),
        ("AI installation controller installs and reports verified completion", ControllerInstallsAndCompletes),
        ("AI installation controller cancels without activating a partial installation", ControllerCancelsInstallation),
        ("AI installation controller contains installer failures", ControllerContainsFailure)
    };

    private static void ControllerPreparesWithoutInstalling()
    {
        var installer = new StubInstaller();
        using var fixture = Create(installer);

        var plan = fixture.Controller.PrepareAsync(AiProfile.Automatic).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("automatic installation plan was not prepared");
        Require(plan.RequestedProfile == AiProfile.Automatic);
        Require(plan.EffectiveProfile == AiProfile.Performance);
        Require(plan.ComputePreference == AiComputePreference.Gpu);
        Require(plan.ModelName.Contains("8B", StringComparison.Ordinal));
        Require(plan.DownloadBytes == 35_208_196L + 5_027_783_488L);
        Require(plan.RecommendedFreeBytes > plan.DownloadBytes);
        Require(installer.CallCount == 0);
        Require(fixture.Controller.State.CanInstall);
    }

    private static void ControllerRespectsManualProfile()
    {
        using var fixture = Create(new StubInstaller());

        var plan = fixture.Controller.PrepareAsync(AiProfile.Balanced).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("balanced installation plan was not prepared");
        Require(plan.RequestedProfile == AiProfile.Balanced);
        Require(plan.EffectiveProfile == AiProfile.Balanced);
        Require(plan.ComputePreference == AiComputePreference.Cpu);
        Require(plan.ModelName.Contains("4B", StringComparison.Ordinal));
        Require(plan.DownloadBytes == 18_389_848L + 2_497_280_256L);
    }

    private static void ControllerRequiresCurrentConfirmation()
    {
        var installer = new StubInstaller();
        using var fixture = Create(installer);
        var plan = fixture.Controller.PrepareAsync(AiProfile.Lightweight).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("lightweight installation plan was not prepared");

        var result = fixture.Controller.InstallAsync(Guid.NewGuid()).GetAwaiter().GetResult();
        var replay = fixture.Controller.InstallAsync(plan.ConfirmationId).GetAwaiter().GetResult();

        Require(result is null && replay is null);
        Require(installer.CallCount == 0);
        Require(fixture.Controller.State.Activity == AiInstallationActivity.Error);
        Require(fixture.Controller.State.StatusMessage.Contains("confirmação", StringComparison.OrdinalIgnoreCase));
    }

    private static void ControllerInstallsAndCompletes()
    {
        var installer = new StubInstaller((configuration, progress, _) =>
        {
            progress?.Report(new AiInstallationProgress(
                AiInstallationStage.DownloadingRuntime,
                "runtime",
                18_389_848,
                18_389_848));
            progress?.Report(new AiInstallationProgress(
                AiInstallationStage.DownloadingModel,
                "model",
                2_497_280_256,
                2_497_280_256));
            return Task.FromResult(Result(configuration, AiProfile.Balanced));
        });
        using var fixture = Create(installer);
        var plan = fixture.Controller.PrepareAsync(AiProfile.Balanced).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("balanced installation plan was not prepared");

        var result = fixture.Controller.InstallAsync(plan.ConfirmationId).GetAwaiter().GetResult();

        Require(result is not null && installer.CallCount == 1);
        Require(installer.Configuration?.Profile == AiProfile.Balanced);
        Require(installer.Configuration?.ContextSize == 4096);
        Require(fixture.Controller.State.Activity == AiInstallationActivity.Completed);
        Require(fixture.Controller.State.ProgressFraction == 1);
        Require(fixture.Controller.State.StatusMessage.Contains("verificada", StringComparison.OrdinalIgnoreCase));
    }

    private static void ControllerCancelsInstallation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new StubInstaller(async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        });
        using var fixture = Create(installer);
        var plan = fixture.Controller.PrepareAsync(AiProfile.Lightweight).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("lightweight installation plan was not prepared");

        var pending = fixture.Controller.InstallAsync(plan.ConfirmationId);
        Require(started.Task.Wait(TimeSpan.FromSeconds(2)));
        fixture.Controller.CancelCurrentOperation();
        var result = pending.GetAwaiter().GetResult();

        Require(result is null);
        Require(fixture.Controller.State.Activity == AiInstallationActivity.Cancelled);
        Require(fixture.Controller.State.StatusMessage.Contains("incompleta", StringComparison.OrdinalIgnoreCase));
    }

    private static void ControllerContainsFailure()
    {
        var installer = new StubInstaller((_, _, _) =>
            throw new InvalidOperationException("secret installer detail"));
        using var fixture = Create(installer);
        var plan = fixture.Controller.PrepareAsync(AiProfile.Lightweight).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("lightweight installation plan was not prepared");

        var result = fixture.Controller.InstallAsync(plan.ConfirmationId).GetAwaiter().GetResult();

        Require(result is null);
        Require(fixture.Controller.State.Activity == AiInstallationActivity.Error);
        Require(fixture.Controller.State.Warnings.Any(item => item.Contains("conteve", StringComparison.OrdinalIgnoreCase)));
        Require(!fixture.Controller.State.Warnings.Any(item => item.Contains("secret", StringComparison.Ordinal)));
    }

    private static ControllerFixture Create(StubInstaller installer)
    {
        var root = Path.Combine(Path.GetTempPath(), "RotaAiInstallationControllerTests", Guid.NewGuid().ToString("N"));
        return new ControllerFixture(
            root,
            new AiInstallationController(
                root,
                new StubConfigurationStore(root),
                new StubModelManager(),
                installer));
    }

    private static AiInstallationResult Result(AiConfiguration configuration, AiProfile profile)
    {
        var model = AiModelCatalog.Default.GetRecommendedModel(profile);
        var installed = configuration with
        {
            ModelId = model.Id,
            ModelPath = "model",
            RuntimePath = "runtime",
            InstallationState = AiInstallationState.Ready
        };
        return new AiInstallationResult(
            installed,
            new AiModelInstallationInfo(
                AiInstallationState.Ready,
                profile,
                model,
                "runtime",
                "model",
                true,
                true,
                Array.Empty<string>()),
            "installation");
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("AI installation controller assertion failed.");
    }

    private sealed class ControllerFixture : IDisposable
    {
        public ControllerFixture(string root, AiInstallationController controller)
        {
            Root = root;
            Controller = controller;
        }

        public string Root { get; }
        public AiInstallationController Controller { get; }

        public void Dispose()
        {
            Controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private sealed class StubConfigurationStore : IAiConfigurationStore
    {
        public StubConfigurationStore(string root) => ConfigurationPath = Path.Combine(root, "config.json");
        public string ConfigurationPath { get; }
        public string LastLoadWarning => "";
        public Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiConfiguration());
        public Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubModelManager : IAiModelManager
    {
        public Task<AiModelInstallationInfo> GetInstallationInfoAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = configuration.Profile == AiProfile.Automatic
                ? AiProfile.Performance
                : configuration.Profile;
            var model = AiModelCatalog.Default.GetRecommendedModel(profile);
            return Task.FromResult(new AiModelInstallationInfo(
                AiInstallationState.NotInstalled,
                profile,
                model,
                "",
                "",
                false,
                false,
                Array.Empty<string>()));
        }
    }

    private sealed class StubInstaller : IAiInstaller
    {
        private readonly Func<AiConfiguration, IProgress<AiInstallationProgress>?, CancellationToken, Task<AiInstallationResult>> _handler;

        public StubInstaller(
            Func<AiConfiguration, IProgress<AiInstallationProgress>?, CancellationToken, Task<AiInstallationResult>>? handler = null)
        {
            _handler = handler ?? ((configuration, _, _) =>
                Task.FromResult(Result(configuration, configuration.Profile == AiProfile.Automatic
                    ? AiProfile.Performance
                    : configuration.Profile)));
        }

        public int CallCount { get; private set; }
        public AiConfiguration? Configuration { get; private set; }

        public Task<AiInstallationResult> InstallAsync(
            AiConfiguration configuration,
            IProgress<AiInstallationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Configuration = configuration;
            return _handler(configuration, progress, cancellationToken);
        }
    }
}
