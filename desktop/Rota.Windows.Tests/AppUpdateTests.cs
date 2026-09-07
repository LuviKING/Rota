using Rota.Desktop;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Rota.Desktop.Tests;

internal static class AppUpdateTests
{
    public static IReadOnlyList<(string Name, Action Body)> Cases { get; } = new (string, Action)[]
    {
        ("Windows updater parses a strict trusted manifest", ParsesStrictTrustedManifest),
        ("Windows updater checks the bounded manifest over HTTPS", ChecksManifestOverHttps),
        ("Windows updater rejects unsafe and duplicate manifest fields", RejectsUnsafeManifest),
        ("Windows updater downloads and verifies the exact installer", DownloadsAndVerifiesInstaller),
        ("Windows updater removes a corrupted partial installer", RemovesCorruptedPartialInstaller)
    };

    private static void ParsesStrictTrustedManifest()
    {
        var manifest = Manifest("0.5.0", new string('a', 64), 10_000_000);
        var update = AppUpdateService.ParseManifest(
            Encoding.UTF8.GetBytes(manifest),
            new Version(0, 4, 0),
            new Uri(AppUpdateService.DefaultManifestUrl));

        True(update.IsUpdateAvailable);
        Equal(new Version(0, 5, 0), update.LatestVersion);
        Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", update.InstallerSha256);

        var current = AppUpdateService.ParseManifest(
            Encoding.UTF8.GetBytes(Manifest("0.4.0", new string('b', 64), 10_000_000)),
            new Version(0, 4, 0),
            new Uri(AppUpdateService.DefaultManifestUrl));
        True(!current.IsUpdateAvailable);
    }

    private static void RejectsUnsafeManifest()
    {
        Throws<InvalidDataException>(() => AppUpdateService.ParseManifest(
            Encoding.UTF8.GetBytes(Manifest("0.5.0", new string('a', 64), 10_000_000)
                .Replace("https://github.com/", "http://github.com/", StringComparison.Ordinal)),
            new Version(0, 4, 0),
            new Uri(AppUpdateService.DefaultManifestUrl)));

        Throws<InvalidDataException>(() => AppUpdateService.ParseManifest(
            Encoding.UTF8.GetBytes(Manifest("0.5.0", new string('a', 64), 10_000_000)
                .Replace("{", "{\"version\":\"9.9.9\",", StringComparison.Ordinal)),
            new Version(0, 4, 0),
            new Uri(AppUpdateService.DefaultManifestUrl)));

        Throws<InvalidDataException>(() => AppUpdateService.ParseManifest(
            Encoding.UTF8.GetBytes(Manifest("0.5.0", new string('a', 64), 10_000_000)
                .Replace("github.com", "example.org", StringComparison.Ordinal)),
            new Version(0, 4, 0),
            new Uri(AppUpdateService.DefaultManifestUrl)));
    }

    private static void ChecksManifestOverHttps()
    {
        var manifest = Encoding.UTF8.GetBytes(Manifest("0.5.0", new string('c', 64), 10_000_000));
        using var client = new HttpClient(new PayloadHandler(manifest));
        var service = new AppUpdateService(
            client,
            new Uri(AppUpdateService.DefaultManifestUrl),
            new Version(0, 4, 0),
            Path.Combine(Path.GetTempPath(), "RotaUpdateTests", Guid.NewGuid().ToString("N")));

        var result = service.CheckAsync().GetAwaiter().GetResult();

        True(result.IsUpdateAvailable);
        Equal(new Version(0, 5, 0), result.LatestVersion);
    }

    private static void DownloadsAndVerifiesInstaller()
    {
        var payload = Enumerable.Range(0, 10_000_000).Select(index => (byte)(index % 251)).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var directory = Path.Combine(Path.GetTempPath(), "RotaUpdateTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new PayloadHandler(payload));
            var service = new AppUpdateService(
                client,
                new Uri(AppUpdateService.DefaultManifestUrl),
                new Version(0, 4, 0),
                directory);
            var update = AppUpdateService.ParseManifest(
                Encoding.UTF8.GetBytes(Manifest("0.5.0", hash, payload.LongLength)),
                new Version(0, 4, 0),
                new Uri(AppUpdateService.DefaultManifestUrl));

            var downloaded = service.DownloadAsync(update).GetAwaiter().GetResult();

            True(File.Exists(downloaded.InstallerPath));
            Equal(payload.LongLength, new FileInfo(downloaded.InstallerPath).Length);
            Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(downloaded.InstallerPath))));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void RemovesCorruptedPartialInstaller()
    {
        var payload = new byte[10_000_000];
        var directory = Path.Combine(Path.GetTempPath(), "RotaUpdateTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new PayloadHandler(payload));
            var service = new AppUpdateService(
                client,
                new Uri(AppUpdateService.DefaultManifestUrl),
                new Version(0, 4, 0),
                directory);
            var update = AppUpdateService.ParseManifest(
                Encoding.UTF8.GetBytes(Manifest("0.5.0", new string('f', 64), payload.LongLength)),
                new Version(0, 4, 0),
                new Uri(AppUpdateService.DefaultManifestUrl));

            Throws<InvalidDataException>(() => service.DownloadAsync(update).GetAwaiter().GetResult());

            True(!Directory.Exists(directory) || Directory.GetFiles(directory).Length == 0);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string Manifest(string version, string sha256, long bytes) =>
        $$"""
        {"version":"{{version}}","installer_url":"https://github.com/LuviKING/Rota/releases/download/v{{version}}/Rota-Windows-v{{version}}-Setup-x64.exe","sha256":"{{sha256}}","bytes":{{bytes}},"notes":"Atualização de teste segura."}
        """;

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        catch (Exception ex) { throw new InvalidOperationException($"Expected {typeof(T).Name}, got {ex.GetType().Name}.", ex); }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = request
            });
    }
}
