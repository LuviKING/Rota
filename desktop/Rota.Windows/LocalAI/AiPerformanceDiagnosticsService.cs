using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Rota.Desktop.LocalAI;

public enum AiPerformanceRating
{
    Slow,
    Functional,
    Good,
    Excellent
}

public sealed record AiPerformanceProbeResult(
    TimeSpan ResponseDuration,
    int GeneratedTokens,
    double TokensPerSecond);

public sealed record AiPerformanceDiagnosticReport(
    TimeSpan StartupDuration,
    TimeSpan ResponseDuration,
    int GeneratedTokens,
    double TokensPerSecond,
    AiProfile EffectiveProfile,
    AiComputePreference ComputePreference,
    bool RuntimeWasAlreadyReady,
    AiPerformanceRating Rating,
    DateTimeOffset CapturedAtUtc);

public interface IAiPerformanceProbe
{
    Task<AiPerformanceProbeResult> MeasureAsync(
        AiRuntimeConnection connection,
        string modelId,
        CancellationToken cancellationToken = default);
}

public interface IAiPerformanceDiagnosticsService
{
    Task<AiPerformanceDiagnosticReport> MeasureAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AiPerformanceDiagnosticsException : Exception
{
    public AiPerformanceDiagnosticsException(string message) : base(message)
    {
    }

    public AiPerformanceDiagnosticsException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class AiPerformanceDiagnosticsService : IAiPerformanceDiagnosticsService, IDisposable
{
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiModelManager _modelManager;
    private readonly ILocalAiRuntimeHost _runtimeHost;
    private readonly IAiPerformanceProbe _probe;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly bool _ownsProbe;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AiPerformanceDiagnosticsService(
        IAiConfigurationStore configurationStore,
        IAiModelManager modelManager,
        ILocalAiRuntimeHost runtimeHost,
        IAiPerformanceProbe? probe = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _runtimeHost = runtimeHost ?? throw new ArgumentNullException(nameof(runtimeHost));
        _probe = probe ?? new LlamaServerPerformanceProbe();
        _ownsProbe = probe is null;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AiPerformanceDiagnosticReport> MeasureAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            AiContractValidator.ValidateConfiguration(configuration);
            var installation = await _modelManager
                .GetInstallationInfoAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            if (installation.State != AiInstallationState.Ready ||
                !installation.RuntimeAvailable || !installation.ModelAvailable)
            {
                throw new AiPerformanceDiagnosticsException(
                    "Instale completamente a IA local antes de medir o desempenho.");
            }

            var runtimeWasAlreadyReady =
                _runtimeHost.Status.State == AiRuntimeState.Ready && _runtimeHost.Connection is not null;
            var startedForTest = !runtimeWasAlreadyReady;
            var startupStarted = Stopwatch.GetTimestamp();
            var operationFailed = false;
            try
            {
                var runtimeStatus = await _runtimeHost
                    .StartAsync(configuration, cancellationToken)
                    .ConfigureAwait(false);
                if (runtimeStatus.State != AiRuntimeState.Ready || _runtimeHost.Connection is not { } connection)
                {
                    throw new AiPerformanceDiagnosticsException(
                        "A IA local não confirmou que estava pronta para o teste.");
                }

                var startupDuration = runtimeWasAlreadyReady
                    ? TimeSpan.Zero
                    : Stopwatch.GetElapsedTime(startupStarted);
                var sample = await _probe
                    .MeasureAsync(connection, configuration.ModelId, cancellationToken)
                    .ConfigureAwait(false);
                ValidateSample(sample);

                var capturedAt = _utcNow();
                if (capturedAt == default || capturedAt.Offset != TimeSpan.Zero)
                    throw new AiPerformanceDiagnosticsException("O relógio local não forneceu um horário UTC válido.");

                return new AiPerformanceDiagnosticReport(
                    startupDuration,
                    sample.ResponseDuration,
                    sample.GeneratedTokens,
                    sample.TokensPerSecond,
                    runtimeStatus.EffectiveProfile ?? installation.EffectiveProfile,
                    runtimeStatus.ComputePreference ?? configuration.ComputePreference,
                    runtimeWasAlreadyReady,
                    Classify(sample.TokensPerSecond),
                    capturedAt);
            }
            catch (OperationCanceledException)
            {
                operationFailed = true;
                throw;
            }
            catch (AiPerformanceDiagnosticsException)
            {
                operationFailed = true;
                throw;
            }
            catch (Exception ex) when (ex is AiRuntimeException or AiInferenceException or IOException or HttpRequestException or JsonException)
            {
                operationFailed = true;
                throw new AiPerformanceDiagnosticsException(
                    "O teste local de desempenho não pôde ser concluído.", ex);
            }
            finally
            {
                if (startedForTest && _runtimeHost.Status.State != AiRuntimeState.Stopped)
                {
                    try
                    {
                        await _runtimeHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch when (operationFailed)
                    {
                        // Preserve the original controlled failure. The host retains its faulted state.
                    }
                    catch (Exception ex) when (ex is AiRuntimeException or IOException)
                    {
                        throw new AiPerformanceDiagnosticsException(
                            "A IA local foi testada, mas o processo temporário não pôde ser encerrado com segurança.",
                            ex);
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public static AiPerformanceRating Classify(double tokensPerSecond)
    {
        if (!double.IsFinite(tokensPerSecond) || tokensPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
        if (tokensPerSecond >= 20) return AiPerformanceRating.Excellent;
        if (tokensPerSecond >= 10) return AiPerformanceRating.Good;
        if (tokensPerSecond >= 4) return AiPerformanceRating.Functional;
        return AiPerformanceRating.Slow;
    }

    private static void ValidateSample(AiPerformanceProbeResult sample)
    {
        if (sample.ResponseDuration <= TimeSpan.Zero ||
            sample.GeneratedTokens is < 1 or > LlamaServerPerformanceProbe.MaximumGeneratedTokens ||
            !double.IsFinite(sample.TokensPerSecond) || sample.TokensPerSecond <= 0 ||
            sample.TokensPerSecond > LlamaServerPerformanceProbe.MaximumTokensPerSecond)
        {
            throw new AiPerformanceDiagnosticsException(
                "A IA local devolveu métricas de desempenho inválidas.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        if (_ownsProbe && _probe is IDisposable disposable) disposable.Dispose();
    }
}

public sealed class LlamaServerPerformanceProbe : IAiPerformanceProbe, IDisposable
{
    internal const int MaximumGeneratedTokens = 32;
    internal const double MaximumTokensPerSecond = 10_000;
    private const int MaximumResponseBytes = 256 * 1024;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public LlamaServerPerformanceProbe(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            return;
        }

        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsHttpClient = true;
    }

    public async Task<AiPerformanceProbeResult> MeasureAsync(
        AiRuntimeConnection connection,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateConnection(connection);
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 120 || modelId.Any(char.IsControl))
            throw new AiPerformanceDiagnosticsException("O modelo local não possui uma identificação válida.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = modelId,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Este é um teste local de desempenho. Responda somente com a palavra PRONTO."
                },
                new { role = "user", content = "Responda PRONTO." }
            },
            temperature = 0,
            max_tokens = MaximumGeneratedTokens,
            stream = false,
            seed = 0,
            chat_template_kwargs = new { enable_thinking = false }
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(connection.Endpoint, "v1/chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiPerformanceDiagnosticsException(
                    $"A IA local recusou o teste de desempenho (HTTP {(int)response.StatusCode}).");
            }
            var responseBytes = await ReadBoundedAsync(response.Content, linked.Token).ConfigureAwait(false);
            var duration = Stopwatch.GetElapsedTime(started);
            return ParseResponse(responseBytes, duration);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new AiPerformanceDiagnosticsException(
                "O teste de desempenho excedeu o tempo limite e foi cancelado.");
        }
        catch (AiPerformanceDiagnosticsException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or NotSupportedException)
        {
            throw new AiPerformanceDiagnosticsException(
                "A resposta do teste local não pôde ser processada.", ex);
        }
    }

    private static AiPerformanceProbeResult ParseResponse(byte[] responseBytes, TimeSpan duration)
    {
        using var document = JsonDocument.Parse(responseBytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        EnsureNoDuplicateProperties(root);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1 ||
            !choices[0].TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(content.GetString()) ||
            !root.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("completion_tokens", out var completionTokens) ||
            !completionTokens.TryGetInt32(out var generatedTokens) ||
            generatedTokens is < 1 or > MaximumGeneratedTokens)
        {
            throw new AiPerformanceDiagnosticsException(
                "A IA local devolveu uma resposta de teste incompleta.");
        }

        var tokensPerSecond = TryReadServerRate(root, out var serverRate)
            ? serverRate
            : generatedTokens / Math.Max(duration.TotalSeconds, 0.001);
        if (duration <= TimeSpan.Zero || !double.IsFinite(tokensPerSecond) ||
            tokensPerSecond <= 0 || tokensPerSecond > MaximumTokensPerSecond)
        {
            throw new AiPerformanceDiagnosticsException(
                "A IA local devolveu métricas de teste inválidas.");
        }
        return new AiPerformanceProbeResult(duration, generatedTokens, tokensPerSecond);
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new AiPerformanceDiagnosticsException(
                        "A resposta do teste contém propriedades JSON duplicadas.");
                }
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
        }
    }

    private static bool TryReadServerRate(JsonElement root, out double rate)
    {
        rate = 0;
        return root.TryGetProperty("timings", out var timings) &&
               timings.ValueKind == JsonValueKind.Object &&
               timings.TryGetProperty("predicted_per_second", out var value) &&
               value.TryGetDouble(out rate) &&
               double.IsFinite(rate) && rate > 0 && rate <= MaximumTokensPerSecond;
    }

    private static void ValidateConnection(AiRuntimeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Endpoint.IsAbsoluteUri ||
            connection.Endpoint.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(connection.Endpoint.Host, "127.0.0.1", StringComparison.Ordinal) ||
            connection.Endpoint.IsDefaultPort || connection.Endpoint.AbsolutePath != "/" ||
            connection.Endpoint.Query.Length > 0 || connection.Endpoint.Fragment.Length > 0 ||
            connection.ApiKey.Length != 64 || connection.ApiKey.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new AiPerformanceDiagnosticsException(
                "O runtime não forneceu uma conexão local autenticada válida para o teste.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
            throw new AiPerformanceDiagnosticsException("A resposta do teste excedeu o limite seguro.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > MaximumResponseBytes)
                throw new AiPerformanceDiagnosticsException("A resposta do teste excedeu o limite seguro.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
