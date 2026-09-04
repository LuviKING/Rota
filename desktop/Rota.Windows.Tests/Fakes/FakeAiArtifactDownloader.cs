using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests.Fakes;

public sealed class FakeAiArtifactDownloader : IAiArtifactDownloader
{
    private readonly IReadOnlyDictionary<Uri, byte[]> _artifacts;

    public int CallCount { get; private set; }
    public int? CancelBeforeCall { get; init; }
    public Action? CancelAction { get; init; }
    public List<Uri> RequestedSources { get; } = new();

    public FakeAiArtifactDownloader(IReadOnlyDictionary<Uri, byte[]> artifacts)
    {
        _artifacts = artifacts;
    }

    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        long expectedSizeBytes,
        IProgress<AiDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        RequestedSources.Add(source);
        if (CancelBeforeCall == CallCount)
        {
            CancelAction?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!_artifacts.TryGetValue(source, out var content))
            throw new InvalidOperationException($"No fake artifact registered for {source}.");

        await File.WriteAllBytesAsync(destinationPath, content, cancellationToken).ConfigureAwait(false);
        progress?.Report(new AiDownloadProgress(content.LongLength, content.LongLength));
    }
}
