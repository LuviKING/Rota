using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

/// <summary>
/// Sidecar autoritativo somente para a associação conversa→matéria. O conteúdo da
/// conversa continua no histórico completo. O registro só nasce de um binding
/// verificado contra o contexto congelado da própria conversa.
/// </summary>
public sealed record AiTeacherConversationSubjectBinding
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid ConversationId { get; init; }
    public DateTimeOffset BoundAtUtc { get; init; }
    public AiTeacherSubjectBinding Binding { get; init; } = new();
}

public interface IAiTeacherSubjectBindingStore
{
    string RootDirectory { get; }

    Task<AiTeacherConversationSubjectBinding> BindAsync(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default);

    Task<AiTeacherConversationSubjectBinding?> TryLoadAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiTeacherConversationSubjectBinding>> ListBySubjectAsync(
        string packageId,
        string subjectId,
        CancellationToken cancellationToken = default);
}

public sealed class AiTeacherSubjectBindingStore : IAiTeacherSubjectBindingStore, IDisposable
{
    public const long MaximumBindingFileBytes = 32 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private bool _disposed;

    public AiTeacherSubjectBindingStore(string? rootDirectory = null, Func<DateTimeOffset>? utcNow = null)
    {
        var selected = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota", "AI", "teacher", "memory", "subject-bindings");
        if (string.IsNullOrWhiteSpace(selected) || !Path.IsPathFullyQualified(selected))
            throw new AiContractValidationException("O diretório dos vínculos de matéria precisa ser absoluto.");
        RootDirectory = Path.GetFullPath(selected);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string RootDirectory { get; }

    public async Task<AiTeacherConversationSubjectBinding> BindAsync(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateConversation(conversation);
        AiTeacherSubjectBindingFactory.Validate(binding, conversation.LessonContext);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var path = BindingPath(conversation.ConversationId);
            if (File.Exists(path))
            {
                try
                {
                    var existing = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                    ValidateAgainstConversation(existing, conversation);
                    if (!AiTeacherSubjectBindingFactory.Equivalent(existing.Binding, binding))
                        throw new AiContractValidationException(
                            "Uma conversa da Professora Local não pode trocar de matéria ou origem pedagógica.");
                    return Copy(existing);
                }
                catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidDataException)
                {
                    PreserveInvalidFile(path);
                }
            }

            var now = _utcNow();
            if (now == default || now.Offset != TimeSpan.Zero)
                throw new AiContractValidationException("O relógio local não forneceu um timestamp UTC válido.");
            var record = new AiTeacherConversationSubjectBinding
            {
                ConversationId = conversation.ConversationId,
                BoundAtUtc = now,
                Binding = AiTeacherSubjectBindingFactory.Copy(binding)
            };
            Validate(record);
            await WriteAtomicallyAsync(path, record, cancellationToken).ConfigureAwait(false);
            return Copy(record);
        }
        finally { _gate.Release(); }
    }

    public async Task<AiTeacherConversationSubjectBinding?> TryLoadAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (conversationId == Guid.Empty)
            throw new AiContractValidationException("O vínculo de matéria precisa de um ID de conversa válido.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = BindingPath(conversationId);
            if (!File.Exists(path)) return null;
            return Copy(await LoadWithRecoveryAsync(path, cancellationToken).ConfigureAwait(false));
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AiTeacherConversationSubjectBinding>> ListBySubjectAsync(
        string packageId,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSubjectIdentity(packageId, subjectId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(RootDirectory))
                return Array.Empty<AiTeacherConversationSubjectBinding>();
            var files = Directory.EnumerateFiles(RootDirectory, "*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count > AiTeacherConversationStore.MaximumConversationFiles)
                throw new InvalidDataException("Há vínculos de matéria demais para uma leitura segura.");

            var result = new List<AiTeacherConversationSubjectBinding>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var expectedId) || expectedId == Guid.Empty)
                {
                    PreserveInvalidFile(file);
                    continue;
                }
                try
                {
                    var item = await LoadWithRecoveryAsync(file, cancellationToken).ConfigureAwait(false);
                    if (item.ConversationId != expectedId)
                        throw new InvalidDataException("O arquivo de vínculo não corresponde ao ID da conversa.");
                    if (string.Equals(item.Binding.PackageId, packageId, StringComparison.Ordinal) &&
                        string.Equals(item.Binding.SubjectId, subjectId, StringComparison.Ordinal))
                        result.Add(Copy(item));
                }
                catch (InvalidDataException)
                {
                    // Arquivo inválido já foi preservado. Não adivinha a matéria da conversa.
                }
            }
            return result.OrderBy(item => item.ConversationId).ToList().AsReadOnly();
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<AiTeacherConversationSubjectBinding> LoadWithRecoveryAsync(
        string path,
        CancellationToken cancellationToken)
    {
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
                $"O vínculo de matéria estava inválido e foi preservado como {Path.GetFileName(corrupt)}.", ex);
        }
    }

    private async Task<AiTeacherConversationSubjectBinding> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is < 2 or > MaximumBindingFileBytes)
            throw new InvalidDataException("O arquivo de vínculo de matéria possui tamanho inválido.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        EnsureNoDuplicateProperties(document.RootElement);
        var result = JsonSerializer.Deserialize<AiTeacherConversationSubjectBinding>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo de vínculo de matéria está vazio.");
        Validate(result);
        return result;
    }

    private async Task WriteAtomicallyAsync(
        string path,
        AiTeacherConversationSubjectBinding record,
        CancellationToken cancellationToken)
    {
        Validate(record);
        Directory.CreateDirectory(RootDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, _jsonOptions);
        if (bytes.LongLength > MaximumBindingFileBytes)
            throw new IOException("O vínculo de matéria excedeu o limite seguro de armazenamento local.");
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

    private async Task WriteNewAsync(
        string path,
        AiTeacherConversationSubjectBinding record,
        CancellationToken cancellationToken)
    {
        Validate(record);
        Directory.CreateDirectory(RootDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, _jsonOptions);
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
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void Validate(AiTeacherConversationSubjectBinding record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != AiTeacherConversationSubjectBinding.CurrentSchemaVersion ||
            record.ConversationId == Guid.Empty || record.BoundAtUtc == default || record.BoundAtUtc.Offset != TimeSpan.Zero ||
            record.Binding is null)
            throw new InvalidDataException("A estrutura do vínculo de matéria é inválida.");
        AiTeacherSubjectBindingFactory.Validate(record.Binding);
    }

    private static void ValidateConversation(AiTeacherConversationSnapshot conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.ConversationId == Guid.Empty || conversation.LessonContext is null)
            throw new AiContractValidationException("A conversa vinculada à matéria é inválida.");
        AiTeacherContractValidator.ValidateLessonContext(conversation.LessonContext);
    }

    private static void ValidateAgainstConversation(
        AiTeacherConversationSubjectBinding record,
        AiTeacherConversationSnapshot conversation)
    {
        if (record.ConversationId != conversation.ConversationId)
            throw new AiContractValidationException("O vínculo de matéria pertence a outra conversa.");
        AiTeacherSubjectBindingFactory.Validate(record.Binding, conversation.LessonContext);
    }

    private static void ValidateSubjectIdentity(string packageId, string subjectId)
    {
        if (!LearningCatalogIds.IsValid(packageId) || !LearningCatalogIds.IsValid(subjectId))
            throw new AiContractValidationException("A identidade da memória por matéria é inválida.");
    }

    private string BindingPath(Guid id) => Path.Combine(RootDirectory, id.ToString("D") + ".json");
    private string TemporaryPath(string path) =>
        Path.Combine(RootDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static string PreserveInvalidFile(string path)
    {
        if (!File.Exists(path)) return path;
        var preserved = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        File.Move(path, preserved);
        return preserved;
    }

    private static AiTeacherConversationSubjectBinding Copy(AiTeacherConversationSubjectBinding source) => source with
    {
        Binding = AiTeacherSubjectBindingFactory.Copy(source.Binding)
    };

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
                        throw new InvalidDataException($"O vínculo de matéria contém a propriedade JSON duplicada '{property.Name}'.");
                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
                break;
        }
    }
}
