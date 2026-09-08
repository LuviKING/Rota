using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

public interface IAiTeacherSubjectMemoryStore
{
    string RootDirectory { get; }
    Task SaveAsync(AiTeacherSubjectMemorySnapshot memory, CancellationToken cancellationToken = default);
    Task<AiTeacherSubjectMemorySnapshot?> TryLoadAsync(
        string packageId,
        string subjectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache derivado, estrito e atômico por matéria. Um arquivo ausente/corrompido
/// pode ser reconstruído das conversas completas e de seus vínculos verificados.
/// </summary>
public sealed class AiTeacherSubjectMemoryStore : IAiTeacherSubjectMemoryStore, IDisposable
{
    public const long MaximumMemoryFileBytes = 4L * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 48,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private bool _disposed;

    public AiTeacherSubjectMemoryStore(string? rootDirectory = null)
    {
        var selected = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota", "AI", "teacher", "memory", "subjects");
        if (string.IsNullOrWhiteSpace(selected) || !Path.IsPathFullyQualified(selected))
            throw new AiContractValidationException("O diretório da memória por matéria precisa ser absoluto.");
        RootDirectory = Path.GetFullPath(selected);
    }

    public string RootDirectory { get; }

    public async Task SaveAsync(AiTeacherSubjectMemorySnapshot memory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AiTeacherSubjectMemoryFactory.Validate(memory);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(memory, _jsonOptions);
            if (bytes.LongLength > MaximumMemoryFileBytes)
                throw new IOException("A memória por matéria excedeu o limite seguro de armazenamento local.");
            var path = MemoryPath(memory.PackageId, memory.SubjectId);
            var temporary = TemporaryPath(path);
            try
            {
                await WriteTemporaryAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
                else File.Move(temporary, path);
            }
            finally { TryDelete(temporary); }
        }
        finally { _gate.Release(); }
    }

    public async Task<AiTeacherSubjectMemorySnapshot?> TryLoadAsync(
        string packageId,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIdentity(packageId, subjectId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = MemoryPath(packageId, subjectId);
            if (!File.Exists(path)) return null;
            try { return await ReadAsync(path, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidDataException or AiContractValidationException)
            {
                var corrupt = PreserveInvalidFile(path);
                var backup = path + ".bak";
                if (File.Exists(backup))
                {
                    try
                    {
                        var recovered = await ReadAsync(backup, cancellationToken).ConfigureAwait(false);
                        await WriteNewAsync(path, recovered, cancellationToken).ConfigureAwait(false);
                        return recovered;
                    }
                    catch (Exception backupError) when (backupError is JsonException or DecoderFallbackException or InvalidDataException or AiContractValidationException)
                    {
                        PreserveInvalidFile(backup);
                    }
                }
                throw new InvalidDataException(
                    $"A memória por matéria estava inválida e foi preservada como {Path.GetFileName(corrupt)}.", ex);
            }
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<AiTeacherSubjectMemorySnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is < 2 or > MaximumMemoryFileBytes)
            throw new InvalidDataException("O arquivo de memória por matéria possui tamanho inválido.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        EnsureNoDuplicateProperties(document.RootElement);
        var memory = JsonSerializer.Deserialize<AiTeacherSubjectMemorySnapshot>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo de memória por matéria está vazio.");
        AiTeacherSubjectMemoryFactory.Validate(memory);
        return memory;
    }

    private async Task WriteNewAsync(string path, AiTeacherSubjectMemorySnapshot memory, CancellationToken cancellationToken)
    {
        AiTeacherSubjectMemoryFactory.Validate(memory);
        Directory.CreateDirectory(RootDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(memory, _jsonOptions);
        var temporary = TemporaryPath(path);
        try
        {
            await WriteTemporaryAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
        }
        finally { TryDelete(temporary); }
    }

    private static async Task WriteTemporaryAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private string MemoryPath(string packageId, string subjectId) =>
        Path.Combine(RootDirectory, packageId + "--" + subjectId + ".json");
    private string TemporaryPath(string path) =>
        Path.Combine(RootDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static void ValidateIdentity(string packageId, string subjectId)
    {
        if (!LearningCatalogIds.IsValid(packageId) || !LearningCatalogIds.IsValid(subjectId))
            throw new AiContractValidationException("A identidade da memória por matéria é inválida.");
    }

    private static string PreserveInvalidFile(string path)
    {
        if (!File.Exists(path)) return path;
        var preserved = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        File.Move(path, preserved);
        return preserved;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidDataException($"A memória por matéria contém a propriedade JSON duplicada '{property.Name}'.");
                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
                break;
        }
    }
}
