using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Rota.Desktop.LocalAI;

public sealed class LlamaServerHealthClient : IAiRuntimeHealthClient, IDisposable
{
    private const int MaximumHealthResponseBytes = 4 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _requestTimeout;

    public LlamaServerHealthClient(HttpClient? client = null, TimeSpan? requestTimeout = null)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2);
        if (_requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        if (client is not null)
        {
            _client = client;
            return;
        }

        _client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsClient = true;
    }

    public async Task<bool> IsHealthyAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ValidateLoopbackEndpoint(endpoint);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(_requestTimeout);

        try
        {
            using var response = await _client.GetAsync(
                new Uri(endpoint, "health"),
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return false;
            if (response.Content.Headers.ContentLength is > MaximumHealthResponseBytes) return false;
            await using var body = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
            var bytes = new byte[MaximumHealthResponseBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await body.ReadAsync(bytes.AsMemory(length), requestTimeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumHealthResponseBytes) return false;
            using var document = JsonDocument.Parse(
                bytes.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 8 });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                string.Equals(status.GetString(), "ok", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return false;
        }
    }

    private static void ValidateLoopbackEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(endpoint.Host, "127.0.0.1", StringComparison.Ordinal) ||
            endpoint.IsDefaultPort || endpoint.UserInfo.Length > 0 ||
            endpoint.AbsolutePath != "/" || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
        {
            throw new AiContractValidationException("O health check da IA precisa usar um endpoint HTTP local seguro.");
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
