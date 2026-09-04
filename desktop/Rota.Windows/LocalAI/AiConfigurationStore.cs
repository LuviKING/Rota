using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public sealed class AiConfigurationStore : IAiConfigurationStore, IDisposable
{
    private const long MaxConfigurationFileBytes = 256 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public string ConfigurationPath { get; }
    public string ConfigurationDirectory => Path.GetDirectoryName(ConfigurationPath)!;
    public string LastLoadWarning { get; private set; } = "";

    public AiConfigurationStore(string? configurationPath = null)
    {
        ConfigurationPath = Path.GetFullPath(configurationPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "AI",
            "config.json"));
    }

    public async Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LastLoadWarning = "";
            if (!File.Exists(ConfigurationPath))
            {
                var fresh = new AiConfiguration();
                await WriteAtomicallyAsync(fresh, cancellationToken).ConfigureAwait(false);
                return fresh;
            }

            try
            {
                return await ReadAsync(ConfigurationPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsInvalidConfigurationError(ex))
            {
                var corruptPath = PreserveInvalidFile(ConfigurationPath, "config.corrupt");
                var backupPath = ConfigurationPath + ".bak";
                try
                {
                    if (File.Exists(backupPath))
                    {
                        var recovered = await ReadAsync(backupPath, cancellationToken).ConfigureAwait(false);
                        await WriteAtomicallyAsync(recovered, cancellationToken).ConfigureAwait(false);
                        LastLoadWarning =
                            $"A configuração principal da IA estava inválida e foi preservada como {Path.GetFileName(corruptPath)}. " +
                            "O Rota recuperou automaticamente a última configuração íntegra.";
                        return recovered;
                    }
                }
                catch (Exception backupError) when (IsInvalidConfigurationError(backupError))
                {
                    PreserveInvalidFile(backupPath, "config.backup-corrupt");
                }

                var fresh = new AiConfiguration();
                await WriteAtomicallyAsync(fresh, cancellationToken).ConfigureAwait(false);
                LastLoadWarning =
                    $"A configuração local da IA estava inválida e foi preservada como {Path.GetFileName(corruptPath)}. " +
                    "Não havia uma cópia íntegra; o Rota criou uma configuração segura padrão.";
                return fresh;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default)
    {
        AiContractValidator.ValidateConfiguration(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicallyAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AiConfiguration> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxConfigurationFileBytes)
            throw new InvalidDataException("A configuração da IA excede o limite seguro de 256 KB.");

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var configuration = JsonSerializer.Deserialize<AiConfiguration>(json, _jsonOptions)
            ?? throw new InvalidDataException("A configuração da IA está vazia.");
        AiContractValidator.ValidateConfiguration(configuration);
        return configuration;
    }

    private async Task WriteAtomicallyAsync(AiConfiguration configuration, CancellationToken cancellationToken)
    {
        AiContractValidator.ValidateConfiguration(configuration);
        Directory.CreateDirectory(ConfigurationDirectory);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration, _jsonOptions);
        if (bytes.LongLength > MaxConfigurationFileBytes)
            throw new IOException("A configuração da IA excedeu o limite seguro de 256 KB.");

        var tempPath = Path.Combine(
            ConfigurationDirectory,
            $".{Path.GetFileName(ConfigurationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(ConfigurationPath))
                File.Move(tempPath, ConfigurationPath);
            else
                File.Replace(tempPath, ConfigurationPath, ConfigurationPath + ".bak", ignoreMetadataErrors: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // A falha de limpeza não pode mascarar a falha original de persistência.
            }
        }
    }

    private string PreserveInvalidFile(string path, string prefix)
    {
        if (!File.Exists(path)) return "";
        Directory.CreateDirectory(ConfigurationDirectory);
        var preserved = Path.Combine(
            ConfigurationDirectory,
            $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Move(path, preserved);
        return preserved;
    }

    private static bool IsInvalidConfigurationError(Exception exception) =>
        exception is JsonException or InvalidDataException or DecoderFallbackException or NotSupportedException or AiContractValidationException;

    public void Dispose() => _gate.Dispose();
}
