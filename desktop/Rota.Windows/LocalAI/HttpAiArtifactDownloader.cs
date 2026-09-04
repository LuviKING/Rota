using System.Net.Http;

namespace Rota.Desktop.LocalAI;

public sealed class HttpAiArtifactDownloader : IAiArtifactDownloader
{
    private const int BufferSize = 128 * 1024;
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient _client;

    public HttpAiArtifactDownloader(HttpClient? client = null)
    {
        _client = client ?? SharedClient;
    }

    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        long expectedSizeBytes,
        IProgress<AiDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri || !string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new AiContractValidationException("A origem do download da IA precisa usar HTTPS.");
        if (string.IsNullOrWhiteSpace(destinationPath) || !Path.IsPathFullyQualified(destinationPath))
            throw new AiContractValidationException("O destino do download da IA precisa ser um caminho absoluto.");
        if (expectedSizeBytes is <= 0 or > 16L * 1024 * 1024 * 1024)
            throw new AiContractValidationException("O tamanho esperado do download da IA é inválido.");

        cancellationToken.ThrowIfCancellationRequested();
        var createdDestination = false;
        try
        {
            using var response = await _client.GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var finalSource = response.RequestMessage?.RequestUri;
            if (finalSource is null || !string.Equals(finalSource.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException("O servidor tentou redirecionar o download da IA para uma origem sem HTTPS.");

            var totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes.HasValue && totalBytes.Value != expectedSizeBytes)
                throw new AiInstallationException("O tamanho anunciado do download difere do manifesto.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            createdDestination = true;

            var buffer = new byte[BufferSize];
            long received = 0;
            using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            while (true)
            {
                idleTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                var read = await input.ReadAsync(buffer, idleTimeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (read > expectedSizeBytes - received)
                    throw new AiInstallationException("O download excede o tamanho permitido pelo manifesto.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new AiDownloadProgress(received, totalBytes));
            }
            if (received != expectedSizeBytes)
                throw new AiInstallationException("O download foi interrompido antes do tamanho esperado.");

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (createdDestination) TryDeleteFile(destinationPath);
            throw;
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            // ResponseHeadersRead makes this a header timeout; body reads have an idle timeout.
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Rota-Windows/0.2");
        return client;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A limpeza é de melhor esforço; a falha original continua sendo a relevante.
        }
    }
}
