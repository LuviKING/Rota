using Rota.Desktop.LocalAI;
using Rota.Desktop.Tests.Fakes;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Rota.Desktop.Tests;

public static class RuntimeLifecycleTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Local AI runtime binds only to loopback and enables installed GPU runtime", RuntimeUsesSafeGpuCommand),
        ("Local AI runtime forces zero GPU layers for a CPU installation", RuntimeUsesCpuCommand),
        ("Local AI runtime waits for the health endpoint before becoming ready", RuntimeWaitsForHealth),
        ("Local AI runtime start is idempotent while the owned process is ready", RuntimeStartIsIdempotent),
        ("Local AI runtime restarts when the active configuration changes", RuntimeRestartsForChangedConfiguration),
        ("Local AI runtime cancellation terminates its process tree", RuntimeCancellationTerminatesProcess),
        ("Local AI runtime timeout terminates its process tree", RuntimeTimeoutTerminatesProcess),
        ("Local AI runtime reports an early process exit without leaving it active", RuntimeReportsEarlyExit),
        ("Local AI runtime stop is safe and idempotent", RuntimeStopIsIdempotent),
        ("Local AI runtime rejects an incomplete installation before launching", RuntimeRejectsIncompleteInstallation),
        ("Local AI runtime rejects failed integrity before launching", RuntimeRejectsFailedIntegrity),
        ("llama-server health requires an exact local healthy response", HealthClientRequiresLocalOk),
        ("Installed AI artifacts are reverified before execution", IntegrityVerifierDetectsChanges)
    };

    private static void RuntimeUsesSafeGpuCommand()
    {
        var fixture = RuntimeFixture(AiComputePreference.Gpu);
        using var scope = fixture;
        var status = fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        Require(status.State == AiRuntimeState.Ready);
        Require(status.Endpoint?.AbsoluteUri == "http://127.0.0.1:54321/");
        var command = fixture.Factory.Commands.Single();
        Require(command.FileName == TestRuntimePath);
        Require(command.WorkingDirectory == Path.GetDirectoryName(TestRuntimePath));
        Require(ArgumentValue(command.Arguments, "--host") == "127.0.0.1");
        Require(ArgumentValue(command.Arguments, "--port") == "54321");
        Require(ArgumentValue(command.Arguments, "--model") == TestModelPath);
        Require(ArgumentValue(command.Arguments, "--ctx-size") == "4096");
        Require(ArgumentValue(command.Arguments, "--n-gpu-layers") == "all");
        Require(ArgumentValue(command.Arguments, "--cors-origins") == "localhost");
        var apiKey = ArgumentValue(command.Arguments, "--api-key");
        Require(apiKey.Length == 64 && apiKey.All(Uri.IsHexDigit));
        Require(fixture.Host.Connection?.Endpoint == status.Endpoint);
        Require(fixture.Host.Connection?.ApiKey == apiKey);
        Require(!fixture.Host.Connection!.ToString().Contains(apiKey, StringComparison.Ordinal));
    }

    private static void RuntimeUsesCpuCommand()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu);
        fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        Require(ArgumentValue(fixture.Factory.Commands.Single().Arguments, "--n-gpu-layers") == "0");
    }

    private static void RuntimeWaitsForHealth()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu, false, false, true);
        var status = fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        Require(fixture.Health.CallCount == 3);
        Require(status.State == AiRuntimeState.Ready);
    }

    private static void RuntimeStartIsIdempotent()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu);
        var first = fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        var second = fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        Require(first == second);
        Require(fixture.Factory.Commands.Count == 1);
    }

    private static void RuntimeRestartsForChangedConfiguration()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu, true, true);
        fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        var firstProcess = fixture.Factory.LastProcess!;
        fixture.Host.StartAsync(ReadyConfiguration() with { ContextSize = 8192 }).GetAwaiter().GetResult();
        Require(firstProcess.KilledWithTree);
        Require(fixture.Factory.Commands.Count == 2);
        Require(ArgumentValue(fixture.Factory.Commands[1].Arguments, "--ctx-size") == "8192");
    }

    private static void RuntimeCancellationTerminatesProcess()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = RuntimeFixture(AiComputePreference.Cpu, false);
        fixture.Health.OnCall = cancellation.Cancel;
        Expect<OperationCanceledException>(() =>
            fixture.Host.StartAsync(ReadyConfiguration(), cancellation.Token).GetAwaiter().GetResult());
        Require(fixture.Factory.LastProcess!.KilledWithTree);
        Require(fixture.Host.Status.State == AiRuntimeState.Stopped);
    }

    private static void RuntimeTimeoutTerminatesProcess()
    {
        using var fixture = RuntimeFixture(
            AiComputePreference.Cpu,
            startupTimeout: TimeSpan.FromMilliseconds(25),
            healthResults: new[] { false });
        Expect<AiRuntimeException>(() => fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult());
        Require(fixture.Factory.LastProcess!.KilledWithTree);
        Require(fixture.Host.Status.State == AiRuntimeState.Faulted);
    }

    private static void RuntimeReportsEarlyExit()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu);
        fixture.Factory.ExitImmediately = true;
        Expect<AiRuntimeException>(() => fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult());
        Require(fixture.Host.Status.State == AiRuntimeState.Faulted);
        Require(fixture.Host.Status.Message.Contains("código 7", StringComparison.Ordinal));
    }

    private static void RuntimeStopIsIdempotent()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu);
        fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult();
        fixture.Host.StopAsync().GetAwaiter().GetResult();
        fixture.Host.StopAsync().GetAwaiter().GetResult();
        Require(fixture.Factory.LastProcess!.KilledWithTree);
        Require(fixture.Host.Status.State == AiRuntimeState.Stopped);
        Require(fixture.Host.Connection is null);
    }

    private static void RuntimeRejectsIncompleteInstallation()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu);
        fixture.ModelManager.IsReady = false;
        Expect<AiRuntimeException>(() => fixture.Host.StartAsync(new AiConfiguration()).GetAwaiter().GetResult());
        Require(fixture.Factory.Commands.Count == 0);
    }

    private static void RuntimeRejectsFailedIntegrity()
    {
        using var fixture = RuntimeFixture(AiComputePreference.Cpu);
        fixture.Verifier.Failure = new AiRuntimeException("integridade simulada");
        Expect<AiRuntimeException>(() => fixture.Host.StartAsync(ReadyConfiguration()).GetAwaiter().GetResult());
        Require(fixture.Factory.Commands.Count == 0);
    }

    private static void HealthClientRequiresLocalOk()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var health = new LlamaServerHealthClient(http, TimeSpan.FromSeconds(1));
        Require(health.IsHealthyAsync(new Uri("http://127.0.0.1:54321/")).GetAwaiter().GetResult());
        Require(handler.LastRequest?.AbsoluteUri == "http://127.0.0.1:54321/health");

        handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        Require(!health.IsHealthyAsync(new Uri("http://127.0.0.1:54321/")).GetAwaiter().GetResult());
        handler.StatusCode = HttpStatusCode.OK;
        handler.Body = "{\"status\":\"loading\"}";
        Require(!health.IsHealthyAsync(new Uri("http://127.0.0.1:54321/")).GetAwaiter().GetResult());
        handler.Body = new string('x', 5_000);
        Require(!health.IsHealthyAsync(new Uri("http://127.0.0.1:54321/")).GetAwaiter().GetResult());
        Expect<AiContractValidationException>(() =>
            health.IsHealthyAsync(new Uri("http://localhost:54321/")).GetAwaiter().GetResult());
    }

    private static void IntegrityVerifierDetectsChanges()
    {
        var parent = Path.Combine(Path.GetTempPath(), "RotaRuntimeIntegrityTests", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "AI");
        Directory.CreateDirectory(root);
        try
        {
            var setup = CreateInstallationSetup();
            using var store = new AiConfigurationStore(Path.Combine(root, "config.json"));
            var manager = new LocalAiModelManager(root);
            using var installer = new LocalAiInstaller(
                root, store, manager, new FakeAiArtifactDownloader(setup.Artifacts), setup.Manifest);
            var installed = installer.InstallAsync(new AiConfiguration
            {
                Profile = AiProfile.Balanced,
                ComputePreference = AiComputePreference.Cpu
            }).GetAwaiter().GetResult();
            var verifier = new AiInstallationIntegrityVerifier(setup.Manifest);
            var verified = verifier.VerifyAsync(installed.Configuration).GetAwaiter().GetResult();
            Require(verified.RuntimePreference == AiComputePreference.Cpu);
            var archivePath = Path.Combine(installed.InstallationDirectory, ".integrity", "runtime-package.zip");
            Require(File.Exists(archivePath));

            var originalRuntime = File.ReadAllBytes(installed.Configuration.RuntimePath);
            var changedRuntime = originalRuntime.ToArray();
            changedRuntime[0] ^= 0xFF;
            File.WriteAllBytes(installed.Configuration.RuntimePath, changedRuntime);
            Expect<AiRuntimeException>(() => verifier.VerifyAsync(installed.Configuration).GetAwaiter().GetResult());
            File.WriteAllBytes(installed.Configuration.RuntimePath, originalRuntime);

            var originalArchive = File.ReadAllBytes(archivePath);
            var changedArchive = originalArchive.ToArray();
            changedArchive[^1] ^= 0xFF;
            File.WriteAllBytes(archivePath, changedArchive);
            Expect<AiRuntimeException>(() => verifier.VerifyAsync(installed.Configuration).GetAwaiter().GetResult());
            File.WriteAllBytes(archivePath, originalArchive);

            var originalModel = File.ReadAllBytes(installed.Configuration.ModelPath);
            var changedModel = originalModel.ToArray();
            changedModel[0] ^= 0xFF;
            File.WriteAllBytes(installed.Configuration.ModelPath, changedModel);
            Expect<AiRuntimeException>(() => verifier.VerifyAsync(installed.Configuration).GetAwaiter().GetResult());

            File.WriteAllBytes(installed.Configuration.ModelPath, originalModel);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(installed.Configuration.RuntimePath)!, "rogue.dll"), new byte[] { 1 });
            Expect<AiRuntimeException>(() => verifier.VerifyAsync(installed.Configuration).GetAwaiter().GetResult());
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); } catch { }
        }
    }

    private static RuntimeTestFixture RuntimeFixture(
        AiComputePreference preference,
        params bool[] healthResults) => RuntimeFixture(preference, null, healthResults);

    private static RuntimeTestFixture RuntimeFixture(
        AiComputePreference preference,
        TimeSpan? startupTimeout,
        params bool[] healthResults)
    {
        var manager = new FakeModelManager();
        var verifier = new FakeIntegrityVerifier(preference);
        var factory = new FakeProcessFactory();
        var health = new FakeHealthClient(healthResults.Length == 0 ? new[] { true } : healthResults);
        var host = new LocalAiRuntimeHost(
            manager,
            verifier,
            factory,
            health,
            new FixedPortAllocator(),
            startupTimeout ?? TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromSeconds(1));
        return new RuntimeTestFixture(host, manager, verifier, factory, health);
    }

    private static AiConfiguration ReadyConfiguration() => new()
    {
        Profile = AiProfile.Performance,
        ModelId = "qwen3-8b-q4-k-m",
        ModelPath = TestModelPath,
        RuntimePath = TestRuntimePath,
        ContextSize = 4096,
        ComputePreference = AiComputePreference.Automatic,
        InstallationState = AiInstallationState.Ready
    };

    private static string ArgumentValue(IReadOnlyList<string> arguments, string name)
    {
        var index = arguments.IndexOf(name);
        Require(index >= 0 && index + 1 < arguments.Count, $"Missing runtime argument {name}.");
        return arguments[index + 1];
    }

    private static InstallationSetup CreateInstallationSetup()
    {
        var runtime = CreateArchive(
            (LocalAiModelManager.RuntimeFileName, new byte[] { 1, 2, 3, 4 }),
            ("ggml-test.dll", new byte[] { 5, 6, 7 }));
        var light = new byte[] { 11, 12 };
        var balanced = new byte[] { 21, 22, 23 };
        var performance = new byte[] { 31, 32, 33, 34 };
        var cpu = Artifact("runtime-cpu", "https://github.com/Rota/test/releases/download/v2/cpu.zip", "cpu.zip", runtime);
        var gpu = Artifact("runtime-gpu", "https://github.com/Rota/test/releases/download/v2/gpu.zip", "gpu.zip", runtime);
        var lightArtifact = Artifact("model-light", "https://huggingface.co/Qwen/test/resolve/1111111111111111111111111111111111111111/Qwen3-1.7B-Q8_0.gguf", "Qwen3-1.7B-Q8_0.gguf", light);
        var balancedArtifact = Artifact("model-balanced", "https://huggingface.co/Qwen/test/resolve/2222222222222222222222222222222222222222/Qwen3-4B-Q4_K_M.gguf", "Qwen3-4B-Q4_K_M.gguf", balanced);
        var performanceArtifact = Artifact("model-performance", "https://huggingface.co/Qwen/test/resolve/3333333333333333333333333333333333333333/Qwen3-8B-Q4_K_M.gguf", "Qwen3-8B-Q4_K_M.gguf", performance);
        var manifest = new AiInstallationManifest(
            AiInstallationManifest.CurrentManifestVersion,
            new[]
            {
                new AiRuntimePackage(AiComputePreference.Cpu, cpu, LocalAiModelManager.RuntimeFileName),
                new AiRuntimePackage(AiComputePreference.Gpu, gpu, LocalAiModelManager.RuntimeFileName)
            },
            new[]
            {
                new AiModelPackage("qwen3-1.7b-q8-0", lightArtifact),
                new AiModelPackage("qwen3-4b-q4-k-m", balancedArtifact),
                new AiModelPackage("qwen3-8b-q4-k-m", performanceArtifact)
            });
        return new InstallationSetup(manifest, new Dictionary<Uri, byte[]>
        {
            [cpu.Source] = runtime,
            [gpu.Source] = runtime,
            [lightArtifact.Source] = light,
            [balancedArtifact.Source] = balanced,
            [performanceArtifact.Source] = performance
        });
    }

    private static AiDownloadArtifact Artifact(string id, string source, string name, byte[] bytes) => new(
        id, new Uri(source), name, bytes.LongLength,
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static byte[] CreateArchive(params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path);
                using var output = entry.Open();
                output.Write(item.Content);
            }
        }
        return stream.ToArray();
    }

    private static void Require(bool condition, string message = "Runtime lifecycle assertion failed.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        catch (Exception ex) { throw new InvalidOperationException($"Expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static readonly string TestRuntimePath = Path.Combine(Path.GetTempPath(), "Rota", "runtime", "llama-server.exe");
    private static readonly string TestModelPath = Path.Combine(Path.GetTempPath(), "Rota", "models", "Qwen3-8B-Q4_K_M.gguf");

    private sealed class FakeModelManager : IAiModelManager
    {
        public bool IsReady { get; set; } = true;

        public Task<AiModelInstallationInfo> GetInstallationInfoAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = AiModelCatalog.Default.GetById(string.IsNullOrWhiteSpace(configuration.ModelId)
                ? "qwen3-8b-q4-k-m"
                : configuration.ModelId);
            return Task.FromResult(new AiModelInstallationInfo(
                IsReady ? AiInstallationState.Ready : AiInstallationState.NotInstalled,
                AiProfile.Performance,
                model,
                TestRuntimePath,
                TestModelPath,
                IsReady,
                IsReady,
                Array.Empty<string>()));
        }
    }

    private sealed class FakeIntegrityVerifier(AiComputePreference preference) : IAiInstallationIntegrityVerifier
    {
        public Exception? Failure { get; set; }

        public Task<AiInstallationIntegrityResult> VerifyAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) return Task.FromException<AiInstallationIntegrityResult>(Failure);
            return Task.FromResult(new AiInstallationIntegrityResult(Path.GetTempPath(), preference));
        }
    }

    private sealed class FakeProcessFactory : IAiRuntimeProcessFactory
    {
        public List<AiRuntimeLaunchCommand> Commands { get; } = new();
        public FakeProcess? LastProcess { get; private set; }
        public bool ExitImmediately { get; set; }

        public IAiRuntimeProcess Start(AiRuntimeLaunchCommand command)
        {
            Commands.Add(command);
            LastProcess = new FakeProcess { HasExitedValue = ExitImmediately };
            return LastProcess;
        }
    }

    private sealed class FakeProcess : IAiRuntimeProcess
    {
        public int Id => 4242;
        public bool HasExitedValue { get; set; }
        public bool HasExited => HasExitedValue;
        public int? ExitCode => HasExitedValue ? 7 : null;
        public string RecentOutput => HasExitedValue ? "simulated startup failure" : "";
        public bool KilledWithTree { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return HasExitedValue ? Task.CompletedTask : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Kill(bool entireProcessTree)
        {
            KilledWithTree = entireProcessTree;
            HasExitedValue = true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHealthClient : IAiRuntimeHealthClient
    {
        private readonly Queue<bool> _results;
        private bool _last;
        public int CallCount { get; private set; }
        public Action? OnCall { get; set; }

        public FakeHealthClient(IEnumerable<bool> results)
        {
            _results = new Queue<bool>(results);
            _last = _results.Count > 0 && _results.Peek();
        }

        public Task<bool> IsHealthyAsync(Uri endpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            OnCall?.Invoke();
            if (_results.Count > 0) _last = _results.Dequeue();
            return Task.FromResult(_last);
        }
    }

    private sealed class FixedPortAllocator : IAiLoopbackPortAllocator
    {
        public int GetAvailablePort() => 54321;
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = statusCode;
        public string Body { get; set; } = body;
        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }

    private sealed record InstallationSetup(
        AiInstallationManifest Manifest,
        IReadOnlyDictionary<Uri, byte[]> Artifacts);

    private sealed class RuntimeTestFixture(
        LocalAiRuntimeHost host,
        FakeModelManager modelManager,
        FakeIntegrityVerifier verifier,
        FakeProcessFactory factory,
        FakeHealthClient health) : IDisposable
    {
        public LocalAiRuntimeHost Host { get; } = host;
        public FakeModelManager ModelManager { get; } = modelManager;
        public FakeIntegrityVerifier Verifier { get; } = verifier;
        public FakeProcessFactory Factory { get; } = factory;
        public FakeHealthClient Health { get; } = health;

        public void Dispose() => Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

internal static class RuntimeArgumentExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
            if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
        return -1;
    }
}
