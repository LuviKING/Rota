using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rota.Desktop;

public sealed record AppUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    Uri InstallerUri,
    string InstallerSha256,
    long InstallerBytes,
    string Notes,
    bool IsUpdateAvailable);

public sealed record AppUpdateDownload(AppUpdateInfo Update, string InstallerPath);

public interface IAppUpdateService
{
    Task<AppUpdateInfo> CheckAsync(CancellationToken cancellationToken = default);
    Task<AppUpdateDownload> DownloadAsync(AppUpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    void LaunchInstaller(AppUpdateDownload download);
}

public sealed class AppUpdateService : IAppUpdateService
{
    public const string DefaultManifestUrl = "https://github.com/LuviKING/Rota/releases/latest/download/windows-update.json";
    public const int MaximumManifestBytes = 64 * 1024;
    public const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateSharedClient();
    private static readonly HashSet<string> TrustedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;
    private readonly Version _currentVersion;
    private readonly string _downloadDirectory;

    public AppUpdateService(
        HttpClient httpClient,
        Uri manifestUri,
        Version currentVersion,
        string downloadDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _manifestUri = ValidateHttpsUri(manifestUri, "endereço do manifesto");
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _downloadDirectory = Path.GetFullPath(downloadDirectory ?? throw new ArgumentNullException(nameof(downloadDirectory)));
    }

    public static AppUpdateService CreateDefault()
    {
        var version = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "Updates");
        return new AppUpdateService(SharedHttpClient, new Uri(DefaultManifestUrl), version, updateDirectory);
    }

    public async Task<AppUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _manifestUri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await ReadBoundedAsync(response.Content, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
        return ParseManifest(body, _currentVersion, _manifestUri);
    }

    public async Task<AppUpdateDownload> DownloadAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.IsUpdateAvailable || update.LatestVersion <= _currentVersion)
            throw new InvalidOperationException("Não existe uma atualização aplicável para baixar.");
        ValidateInstallerUri(update.InstallerUri, _manifestUri);
        ValidateSha256(update.InstallerSha256);
        if (update.InstallerBytes is < 10_000_000 or > MaximumInstallerBytes)
            throw new InvalidDataException("O tamanho informado para o instalador é inválido.");

        Directory.CreateDirectory(_downloadDirectory);
        var finalPath = Path.Combine(_downloadDirectory, $"Rota-Windows-v{ThreePartVersion(update.LatestVersion)}-Setup-x64.exe");
        var partialPath = finalPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.InstallerUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long declared && declared != update.InstallerBytes)
                throw new InvalidDataException("O tamanho recebido não corresponde ao manifesto da atualização.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total = checked(total + read);
                if (total > update.InstallerBytes || total > MaximumInstallerBytes)
                    throw new InvalidDataException("O instalador recebido excede o tamanho permitido.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                progress?.Report((double)total / update.InstallerBytes);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (total != update.InstallerBytes)
                throw new InvalidDataException("O instalador recebido está incompleto.");

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, update.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A verificação de integridade do instalador falhou.");

            await destination.DisposeAsync().ConfigureAwait(false);
            File.Move(partialPath, finalPath, overwrite: true);
            progress?.Report(1);
            return new AppUpdateDownload(update, finalPath);
        }
        catch
        {
            try { File.Delete(partialPath); } catch { }
            throw;
        }
    }

    public void LaunchInstaller(AppUpdateDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        var installer = Path.GetFullPath(download.InstallerPath);
        var relative = Path.GetRelativePath(_downloadDirectory, installer);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ||
            !string.Equals(Path.GetExtension(installer), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(installer))
        {
            throw new InvalidOperationException("O instalador validado não está na pasta segura de atualizações.");
        }

        var info = new FileInfo(installer);
        using var installerStream = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (info.Length != download.Update.InstallerBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(installerStream)), download.Update.InstallerSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O instalador mudou depois da verificação e não será aberto.");
        }

        _ = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true })
            ?? throw new InvalidOperationException("O Windows não iniciou o instalador.");
    }

    public static AppUpdateInfo ParseManifest(byte[] utf8, Version currentVersion, Uri manifestUri)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ValidateHttpsUri(manifestUri, "endereço do manifesto");
        if (utf8.Length is 0 or > MaximumManifestBytes)
            throw new InvalidDataException("O manifesto de atualização está vazio ou é grande demais.");

        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("O manifesto de atualização precisa ser um objeto JSON.");

        var allowed = new HashSet<string>(new[] { "version", "installer_url", "sha256", "bytes", "notes" }, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("O manifesto contém campos desconhecidos ou duplicados.");
        }
        if (seen.Count != allowed.Count)
            throw new InvalidDataException("O manifesto de atualização está incompleto.");

        var versionText = RequiredString(document.RootElement, "version", 32);
        if (!Version.TryParse(versionText, out var latestVersion) || latestVersion.Major < 0 || latestVersion.Build < 0)
            throw new InvalidDataException("A versão da atualização é inválida.");
        var installerUri = ValidateHttpsUri(new Uri(RequiredString(document.RootElement, "installer_url", 2_048), UriKind.Absolute), "endereço do instalador");
        ValidateInstallerUri(installerUri, manifestUri);
        var sha256 = RequiredString(document.RootElement, "sha256", 64);
        ValidateSha256(sha256);
        if (!document.RootElement.GetProperty("bytes").TryGetInt64(out var bytes) || bytes is < 10_000_000 or > MaximumInstallerBytes)
            throw new InvalidDataException("O tamanho do instalador é inválido.");
        var notes = RequiredString(document.RootElement, "notes", 2_000);

        return new AppUpdateInfo(
            currentVersion,
            latestVersion,
            installerUri,
            sha256.ToUpperInvariant(),
            bytes,
            notes,
            latestVersion > currentVersion);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declared && declared > maximumBytes)
            throw new InvalidDataException("A resposta de atualização excede o limite permitido.");
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
                throw new InvalidDataException("A resposta de atualização excede o limite permitido.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static string RequiredString(JsonElement root, string name, int maximumLength)
    {
        var property = root.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"O campo {name} possui tipo inválido.");
        var value = property.GetString() ?? "";
        if (value.Length is 0 || value.Length > maximumLength || value.Any(char.IsControl))
            throw new InvalidDataException($"O campo {name} possui conteúdo inválido.");
        return value;
    }

    private static Uri ValidateHttpsUri(Uri? uri, string field)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException($"O {field} precisa usar HTTPS.");
        return uri;
    }

    private static void ValidateInstallerUri(Uri installerUri, Uri manifestUri)
    {
        ValidateHttpsUri(installerUri, "endereço do instalador");
        if (!TrustedDownloadHosts.Contains(installerUri.Host) &&
            !string.Equals(installerUri.Host, manifestUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O instalador aponta para um servidor não confiável.");
        }
    }

    private static void ValidateSha256(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("O SHA-256 do instalador é inválido.");
    }

    private static string ThreePartVersion(Version version) =>
        string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}");

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Rota-Windows-Updater/0.4.0");
        return client;
    }
}
