using Rota.Desktop.LocalAI;
using System.Net;
using System.Net.Http;

namespace Rota.Desktop.Tests;

public static class InstallationRegressionTests
{
    private static readonly Uri Source = new("https://github.com/ggml-org/llama.cpp/releases/download/test/runtime.zip");

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("HTTP AI downloader preserves an existing destination on HTTP failure", ExistingFileSurvivesHttpError),
        ("HTTP AI downloader preserves an existing destination on CreateNew collision", ExistingFileSurvivesCreateNewCollision),
        ("HTTP AI downloader rejects an oversized declared Content-Length", OversizedDeclaredLengthIsRejected),
        ("HTTP AI downloader bounds a body whose Content-Length hides excess bytes", OversizedBodyWithHeaderIsRejected),
        ("HTTP AI downloader bounds a body without Content-Length", OversizedBodyWithoutHeaderIsRejected),
        ("HTTP AI downloader rejects a short body and removes its partial file", ShortBodyIsRejected),
        ("HTTP AI downloader removes a partial file when cancelled during the body", CancellationDuringBodyRemovesPartial),
        ("HTTP AI downloader removes a partial file after a body read failure", BodyReadFailureRemovesPartial),
        ("Installation manifest rejects a null runtime with a contract error", ManifestRejectsNullRuntime),
        ("Installation manifest rejects an unknown runtime compute enum", ManifestRejectsUnknownComputePreference),
        ("Installation manifest rejects an alternate-data-stream artifact filename", ManifestRejectsAlternateDataStreamName)
    };

    private static void ExistingFileSurvivesHttpError()
    {
        InTemporaryDirectory(destination =>
        {
            var original = new byte[] { 7, 19, 41, 83 };
            File.WriteAllBytes(destination, original);
            using var client = Client(new ByteArrayContent(new byte[] { 1 }), HttpStatusCode.ServiceUnavailable);
            Expect<HttpRequestException>(() => Download(client, destination, 1));
            Require(File.Exists(destination), "The HTTP failure deleted the pre-existing destination.");
            Require(File.ReadAllBytes(destination).SequenceEqual(original), "The HTTP failure altered the pre-existing destination.");
        });
    }

    private static void ExistingFileSurvivesCreateNewCollision()
    {
        InTemporaryDirectory(destination =>
        {
            var original = new byte[] { 13, 29, 61 };
            File.WriteAllBytes(destination, original);
            using var client = Client(new ByteArrayContent(new byte[] { 1, 2, 3 }));
            Expect<IOException>(() => Download(client, destination, 3));
            Require(File.Exists(destination), "CreateNew collision deleted the pre-existing destination.");
            Require(File.ReadAllBytes(destination).SequenceEqual(original), "CreateNew collision altered the pre-existing destination.");
        });
    }

    private static void OversizedDeclaredLengthIsRejected()
    {
        InTemporaryDirectory(destination =>
        {
            using var client = Client(new ByteArrayContent(new byte[12]));
            ExpectDownloadRejection(() => Download(client, destination, 8));
            Require(!File.Exists(destination), "An oversized Content-Length left a destination behind.");
        });
    }

    private static void OversizedBodyWithHeaderIsRejected() => RejectOversizedBody(advertisedLength: 8);

    private static void OversizedBodyWithoutHeaderIsRejected() => RejectOversizedBody(advertisedLength: null);

    private static void RejectOversizedBody(long? advertisedLength)
    {
        InTemporaryDirectory(destination =>
        {
            using var content = new StreamContent(new ControlledReadStream(new byte[12], chunkSize: 4));
            content.Headers.ContentLength = advertisedLength;
            using var client = Client(content);
            long reportedBytes = 0;
            var progress = new InlineProgress(value => reportedBytes = value.BytesReceived);
            ExpectDownloadRejection(() => Download(client, destination, 8, progress));
            Require(reportedBytes > 0, "The test did not reach a partially downloaded body.");
            Require(reportedBytes <= 8, "The downloader wrote and reported bytes beyond the manifest limit.");
            Require(!File.Exists(destination), "An oversized body left its partial destination behind.");
        });
    }

    private static void ShortBodyIsRejected()
    {
        InTemporaryDirectory(destination =>
        {
            using var content = new StreamContent(new ControlledReadStream(new byte[4], chunkSize: 2));
            content.Headers.ContentLength = 8;
            using var client = Client(content);
            long reportedBytes = 0;
            ExpectDownloadRejection(() => Download(client, destination, 8,
                new InlineProgress(value => reportedBytes = value.BytesReceived)));
            Require(reportedBytes == 4, "The test did not consume the short body before rejection.");
            Require(!File.Exists(destination), "A short body left its partial destination behind.");
        });
    }

    private static void CancellationDuringBodyRemovesPartial()
    {
        InTemporaryDirectory(destination =>
        {
            using var cancellation = new CancellationTokenSource();
            using var content = new StreamContent(new ControlledReadStream(new byte[12], chunkSize: 4));
            content.Headers.ContentLength = 12;
            using var client = Client(content);
            long reportedBytes = 0;
            var progress = new InlineProgress(value =>
            {
                reportedBytes = value.BytesReceived;
                cancellation.Cancel();
            });
            Expect<OperationCanceledException>(() => Download(client, destination, 12, progress, cancellation.Token));
            Require(reportedBytes == 4, "The test did not cancel after exactly one written body chunk.");
            Require(!File.Exists(destination), "Cancellation left a partially downloaded destination behind.");
        });
    }

    private static void BodyReadFailureRemovesPartial()
    {
        InTemporaryDirectory(destination =>
        {
            using var content = new StreamContent(new ControlledReadStream(new byte[12], chunkSize: 4, failAfterBytes: 4));
            content.Headers.ContentLength = 12;
            using var client = Client(content);
            long reportedBytes = 0;
            Expect<IOException>(() => Download(client, destination, 12,
                new InlineProgress(value => reportedBytes = value.BytesReceived)));
            Require(reportedBytes == 4, "The test did not reach a written chunk before the read failure.");
            Require(!File.Exists(destination), "A body read failure left a partial destination behind.");
        });
    }

    private static void ManifestRejectsNullRuntime()
    {
        var manifest = AiInstallationManifest.Default;
        var runtimes = manifest.RuntimePackages.ToArray();
        runtimes[0] = null!;
        Expect<AiContractValidationException>(() => new AiInstallationManifest(
            manifest.Version, runtimes, manifest.ModelPackages));
    }

    private static void ManifestRejectsUnknownComputePreference()
    {
        var manifest = AiInstallationManifest.Default;
        var unknown = manifest.RuntimePackages[0] with
        {
            ComputePreference = (AiComputePreference)983,
            Archive = manifest.RuntimePackages[0].Archive with
            {
                Id = "unknown-compute-runtime",
                FileName = "unknown-compute-runtime.zip"
            }
        };
        Expect<AiContractValidationException>(() => new AiInstallationManifest(
            manifest.Version, manifest.RuntimePackages.Append(unknown), manifest.ModelPackages));
    }

    private static void ManifestRejectsAlternateDataStreamName()
    {
        var manifest = AiInstallationManifest.Default;
        var runtimes = manifest.RuntimePackages.ToArray();
        runtimes[0] = runtimes[0] with { Archive = runtimes[0].Archive with { FileName = "runtime:payload.zip" } };
        Expect<AiContractValidationException>(() => new AiInstallationManifest(
            manifest.Version, runtimes, manifest.ModelPackages));
    }

    private static void Download(HttpClient client, string destination, long expectedSize,
        IProgress<AiDownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
        new HttpAiArtifactDownloader(client).DownloadAsync(Source, destination, expectedSize, progress, cancellationToken)
            .GetAwaiter().GetResult();

    private static HttpClient Client(HttpContent content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new ResponseHandler(content, status));

    private static void InTemporaryDirectory(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Rota-DownloadRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            body(Path.Combine(directory, "artifact.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but the operation succeeded.");
    }

    private static void ExpectDownloadRejection(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or HttpRequestException or AiContractValidationException or AiInstallationException)
        {
            return;
        }
        throw new InvalidOperationException("The downloader accepted a body that differs from the manifest size.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class InlineProgress(Action<AiDownloadProgress> report) : IProgress<AiDownloadProgress>
    {
        public void Report(AiDownloadProgress value) => report(value);
    }

    private sealed class ResponseHandler(HttpContent content, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }

    private sealed class ControlledReadStream(byte[] bytes, int chunkSize, int? failAfterBytes = null) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (failAfterBytes is int limit && _position >= limit)
                throw new IOException("Simulated body read failure after a partial download.");
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), bytes.Length - _position);
            bytes.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
