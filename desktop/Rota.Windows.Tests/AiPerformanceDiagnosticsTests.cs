using Rota.Desktop.LocalAI;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Rota.Desktop.Tests;

internal static class AiPerformanceDiagnosticsTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 6, 15, 0, 0, TimeSpan.Zero);

    public static readonly (string Name, Action Body)[] Cases =
    {
        ("AI performance diagnostics measures and stops its temporary runtime", MeasuresAndStopsTemporaryRuntime),
        ("AI performance diagnostics preserves an already active runtime", PreservesActiveRuntime),
        ("AI performance diagnostics rejects an incomplete installation", RejectsIncompleteInstallation),
        ("AI performance diagnostics cleans up after a probe failure", CleansUpAfterProbeFailure),
        ("AI performance diagnostics honors cancellation and cleans up", HonorsCancellation),
        ("AI performance ratings use stable thresholds", UsesStableRatingThresholds),
        ("llama performance probe uses authenticated loopback and server timing", ProbeUsesSafeLocalRequest),
        ("llama performance probe falls back to measured response duration", ProbeUsesMeasuredFallback),
        ("llama performance probe rejects remote endpoints", ProbeRejectsRemoteEndpoint),
        ("llama performance probe rejects malformed metrics", ProbeRejectsMalformedMetrics),
        ("llama performance probe rejects duplicate response properties", ProbeRejectsDuplicateProperties),
        ("llama performance probe bounds its response body", ProbeBoundsResponseBody),
        ("llama performance probe honors request cancellation", ProbeHonorsCancellation),
        ("llama performance probe contains HTTP failures", ProbeContainsHttpFailures)
    };

    private static void MeasuresAndStopsTemporaryRuntime()
    {
        var runtime = new StubRuntimeHost(initiallyReady: false);
        var probe = new StubProbe(new AiPerformanceProbeResult(TimeSpan.FromSeconds(2), 25, 12.5));
        using var service = Service(runtime, probe);

        var report = service.MeasureAsync().GetAwaiter().GetResult();

        Require(runtime.StartCalls == 1 && runtime.StopCalls == 1);
        Require(runtime.Status.State == AiRuntimeState.Stopped);
        Require(probe.Calls == 1);
        Require(report.ResponseDuration == TimeSpan.FromSeconds(2));
        Require(report.GeneratedTokens == 25);
        Require(report.TokensPerSecond == 12.5);
        Require(report.EffectiveProfile == AiProfile.Performance);
        Require(report.ComputePreference == AiComputePreference.Gpu);
        Require(!report.RuntimeWasAlreadyReady);
        Require(report.Rating == AiPerformanceRating.Good);
        Require(report.CapturedAtUtc == CapturedAt);
    }

    private static void PreservesActiveRuntime()
    {
        var runtime = new StubRuntimeHost(initiallyReady: true);
        var probe = new StubProbe(new AiPerformanceProbeResult(TimeSpan.FromSeconds(1), 30, 30));
        using var service = Service(runtime, probe);

        var report = service.MeasureAsync().GetAwaiter().GetResult();

        Require(runtime.StartCalls == 1 && runtime.StopCalls == 0);
        Require(runtime.Status.State == AiRuntimeState.Ready);
        Require(report.RuntimeWasAlreadyReady);
        Require(report.StartupDuration == TimeSpan.Zero);
        Require(report.Rating == AiPerformanceRating.Excellent);
    }

    private static void RejectsIncompleteInstallation()
    {
        var runtime = new StubRuntimeHost(initiallyReady: false);
        var probe = new StubProbe(new AiPerformanceProbeResult(TimeSpan.FromSeconds(1), 10, 10));
        using var service = Service(runtime, probe, installationReady: false);

        Expect<AiPerformanceDiagnosticsException>(() =>
            service.MeasureAsync().GetAwaiter().GetResult());
        Require(runtime.StartCalls == 0 && runtime.StopCalls == 0 && probe.Calls == 0);
    }

    private static void CleansUpAfterProbeFailure()
    {
        var runtime = new StubRuntimeHost(initiallyReady: false);
        var probe = new StubProbe(new AiPerformanceProbeResult(TimeSpan.Zero, 0, 0));
        using var service = Service(runtime, probe);

        Expect<AiPerformanceDiagnosticsException>(() =>
            service.MeasureAsync().GetAwaiter().GetResult());
        Require(runtime.StopCalls == 1);
        Require(runtime.Status.State == AiRuntimeState.Stopped);
    }

    private static void HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new StubRuntimeHost(initiallyReady: false);
        var probe = new StubProbe(new AiPerformanceProbeResult(TimeSpan.FromSeconds(1), 10, 10))
        {
            OnMeasure = cancellation.Cancel
        };
        using var service = Service(runtime, probe);

        Expect<OperationCanceledException>(() =>
            service.MeasureAsync(cancellation.Token).GetAwaiter().GetResult());
        Require(runtime.StopCalls == 1);
        Require(runtime.Status.State == AiRuntimeState.Stopped);
    }

    private static void UsesStableRatingThresholds()
    {
        Require(AiPerformanceDiagnosticsService.Classify(3.99) == AiPerformanceRating.Slow);
        Require(AiPerformanceDiagnosticsService.Classify(4) == AiPerformanceRating.Functional);
        Require(AiPerformanceDiagnosticsService.Classify(9.99) == AiPerformanceRating.Functional);
        Require(AiPerformanceDiagnosticsService.Classify(10) == AiPerformanceRating.Good);
        Require(AiPerformanceDiagnosticsService.Classify(19.99) == AiPerformanceRating.Good);
        Require(AiPerformanceDiagnosticsService.Classify(20) == AiPerformanceRating.Excellent);
        Expect<ArgumentOutOfRangeException>(() => AiPerformanceDiagnosticsService.Classify(0));
    }

    private static void ProbeUsesSafeLocalRequest()
    {
        const string response = """
            {
              "choices": [{"message": {"content": "PRONTO"}, "finish_reason": "stop"}],
              "usage": {"completion_tokens": 7},
              "timings": {"predicted_per_second": 24.5}
            }
            """;
        var handler = new RecordingHandler(HttpStatusCode.OK, response);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http, TimeSpan.FromSeconds(2));

        var result = probe.MeasureAsync(Connection(), "qwen3-8b-q4-k-m").GetAwaiter().GetResult();

        Require(handler.RequestUri?.AbsoluteUri == "http://127.0.0.1:54321/v1/chat/completions");
        Require(handler.Authorization == "Bearer " + ApiKey);
        Require(handler.ContentType == "application/json; charset=utf-8");
        using var request = JsonDocument.Parse(handler.Body);
        Require(request.RootElement.GetProperty("stream").ValueKind == JsonValueKind.False);
        Require(request.RootElement.GetProperty("max_tokens").GetInt32() == 32);
        Require(request.RootElement.GetProperty("chat_template_kwargs")
            .GetProperty("enable_thinking").ValueKind == JsonValueKind.False);
        Require(result.GeneratedTokens == 7);
        Require(result.TokensPerSecond == 24.5);
        Require(result.ResponseDuration > TimeSpan.Zero);
    }

    private static void ProbeUsesMeasuredFallback()
    {
        const string response = """
            {"choices":[{"message":{"content":"PRONTO"}}],"usage":{"completion_tokens":2}}
            """;
        var handler = new RecordingHandler(HttpStatusCode.OK, response);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);

        var result = probe.MeasureAsync(Connection(), "qwen3-8b-q4-k-m").GetAwaiter().GetResult();

        Require(result.ResponseDuration > TimeSpan.Zero);
        Require(result.GeneratedTokens == 2);
        Require(result.TokensPerSecond > 0);
    }

    private static void ProbeRejectsRemoteEndpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);

        Expect<AiPerformanceDiagnosticsException>(() => probe.MeasureAsync(
            new AiRuntimeConnection(new Uri("http://example.com:54321/"), ApiKey),
            "qwen3-8b-q4-k-m").GetAwaiter().GetResult());
        Require(handler.RequestUri is null);
    }

    private static void ProbeRejectsMalformedMetrics()
    {
        const string response = """
            {"choices":[{"message":{"content":"PRONTO"}}],"usage":{"completion_tokens":0}}
            """;
        var handler = new RecordingHandler(HttpStatusCode.OK, response);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);

        Expect<AiPerformanceDiagnosticsException>(() =>
            probe.MeasureAsync(Connection(), "qwen3-8b-q4-k-m").GetAwaiter().GetResult());
    }

    private static void ProbeContainsHttpFailures()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "{}");
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);

        Expect<AiPerformanceDiagnosticsException>(() =>
            probe.MeasureAsync(Connection(), "qwen3-8b-q4-k-m").GetAwaiter().GetResult());
    }

    private static void ProbeRejectsDuplicateProperties()
    {
        const string response = """
            {"choices":[{"message":{"content":"PRONTO"}}],"usage":{"completion_tokens":2,"completion_tokens":3}}
            """;
        var handler = new RecordingHandler(HttpStatusCode.OK, response);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);

        Expect<AiPerformanceDiagnosticsException>(() =>
            probe.MeasureAsync(Connection(), "qwen3-8b-q4-k-m").GetAwaiter().GetResult());
    }

    private static void ProbeBoundsResponseBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, new string('x', 300_000));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);

        Expect<AiPerformanceDiagnosticsException>(() =>
            probe.MeasureAsync(Connection(), "qwen3-8b-q4-k-m").GetAwaiter().GetResult());
    }

    private static void ProbeHonorsCancellation()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var probe = new LlamaServerPerformanceProbe(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Expect<OperationCanceledException>(() => probe.MeasureAsync(
            Connection(), "qwen3-8b-q4-k-m", cancellation.Token).GetAwaiter().GetResult());
    }

    private static AiPerformanceDiagnosticsService Service(
        StubRuntimeHost runtime,
        StubProbe probe,
        bool installationReady = true) => new(
            new StubConfigurationStore(ReadyConfiguration()),
            new StubModelManager(installationReady),
            runtime,
            probe,
            () => CapturedAt);

    private static AiRuntimeConnection Connection() =>
        new(new Uri("http://127.0.0.1:54321/"), ApiKey);

    private static AiConfiguration ReadyConfiguration() => new()
    {
        Profile = AiProfile.Performance,
        ModelId = "qwen3-8b-q4-k-m",
        ModelPath = Path.Combine(Path.GetTempPath(), "Rota", "model.gguf"),
        RuntimePath = Path.Combine(Path.GetTempPath(), "Rota", "llama-server.exe"),
        ContextSize = 4096,
        ComputePreference = AiComputePreference.Gpu,
        InstallationState = AiInstallationState.Ready
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("AI performance diagnostics assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private const string ApiKey =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private sealed class StubConfigurationStore(AiConfiguration configuration) : IAiConfigurationStore
    {
        public string ConfigurationPath => Path.Combine(Path.GetTempPath(), "Rota", "config.json");
        public string LastLoadWarning => "";

        public Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(configuration);
        }

        public Task SaveAsync(AiConfiguration value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubModelManager(bool installationReady) : IAiModelManager
    {
        public Task<AiModelInstallationInfo> GetInstallationInfoAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = AiModelCatalog.Default.GetById(configuration.ModelId);
            return Task.FromResult(new AiModelInstallationInfo(
                installationReady ? AiInstallationState.Ready : AiInstallationState.RuntimeInstalled,
                AiProfile.Performance,
                model,
                configuration.RuntimePath,
                configuration.ModelPath,
                RuntimeAvailable: true,
                ModelAvailable: installationReady,
                Array.Empty<string>()));
        }
    }

    private sealed class StubRuntimeHost : ILocalAiRuntimeHost
    {
        private AiRuntimeStatus _status;

        public StubRuntimeHost(bool initiallyReady)
        {
            _status = initiallyReady ? ReadyStatus : StoppedStatus;
            Connection = initiallyReady ? AiPerformanceDiagnosticsTests.Connection() : null;
        }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public AiRuntimeStatus Status => _status;
        public AiRuntimeConnection? Connection { get; private set; }

        public Task<AiRuntimeStatus> StartAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCalls++;
            _status = ReadyStatus;
            Connection = AiPerformanceDiagnosticsTests.Connection();
            return Task.FromResult(_status);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            _status = StoppedStatus;
            Connection = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static readonly AiRuntimeStatus ReadyStatus = new(
            AiRuntimeState.Ready,
            new Uri("http://127.0.0.1:54321/"),
            123,
            AiProfile.Performance,
            AiComputePreference.Gpu,
            "IA local pronta.");
        private static readonly AiRuntimeStatus StoppedStatus = new(
            AiRuntimeState.Stopped,
            null,
            null,
            null,
            null,
            "Runtime local parado.");
    }

    private sealed class StubProbe(AiPerformanceProbeResult result) : IAiPerformanceProbe
    {
        public int Calls { get; private set; }
        public Action? OnMeasure { get; init; }

        public Task<AiPerformanceProbeResult> MeasureAsync(
            AiRuntimeConnection connection,
            string modelId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            OnMeasure?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Authorization { get; private set; } = "";
        public string ContentType { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString() ?? "";
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? "";
            Body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
