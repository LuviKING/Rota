using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

/// <summary>
/// Escolha explícita de formato feita pelo estudante para uma matéria verificada.
/// Não é inferida do histórico, não descreve o aluno e não amplia o conteúdo que a
/// professora pode ensinar. O estilo continua sendo enviado somente na requisição
/// atual, como já acontece quando o aluno o escolhe na tela.
/// </summary>
public sealed record AiTeacherSubjectStylePreference
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string PackageId { get; init; } = "";
    public string SubjectId { get; init; } = "";
    public AiTeacherExplanationStyle Style { get; init; }
    /// <summary>Escolha explícita para enviar o recap limitado quando a conversa for retomada.</summary>
    public bool UseConversationContinuity { get; init; } = true;
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public static class AiTeacherSubjectStylePreferenceFactory
{
    public static AiTeacherSubjectStylePreference Create(
        AiTeacherSubjectBinding subject,
        AiTeacherExplanationStyle style,
        DateTimeOffset updatedAtUtc,
        bool useConversationContinuity = true)
    {
        ArgumentNullException.ThrowIfNull(subject);
        AiTeacherSubjectBindingFactory.Validate(subject);
        if (!AiTeacherExplanationStyles.IsSupported(style))
            throw new AiContractValidationException("O estilo de explicação escolhido não é suportado.");
        if (updatedAtUtc == default || updatedAtUtc.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("A preferência de estilo precisa usar um timestamp UTC válido.");

        return new AiTeacherSubjectStylePreference
        {
            PackageId = subject.PackageId,
            SubjectId = subject.SubjectId,
            Style = style,
            UseConversationContinuity = useConversationContinuity,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    public static void Validate(AiTeacherSubjectStylePreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (preference.SchemaVersion != AiTeacherSubjectStylePreference.CurrentSchemaVersion ||
            !LearningCatalogIds.IsValid(preference.PackageId) ||
            !LearningCatalogIds.IsValid(preference.SubjectId) ||
            !AiTeacherExplanationStyles.IsSupported(preference.Style) ||
            preference.UpdatedAtUtc == default || preference.UpdatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("A preferência de estilo da Professora Local é inválida.");
        }
    }

    public static bool Matches(AiTeacherSubjectStylePreference preference, AiTeacherSubjectBinding subject)
    {
        ArgumentNullException.ThrowIfNull(preference);
        ArgumentNullException.ThrowIfNull(subject);
        return string.Equals(preference.PackageId, subject.PackageId, StringComparison.Ordinal) &&
               string.Equals(preference.SubjectId, subject.SubjectId, StringComparison.Ordinal);
    }

    public static AiTeacherSubjectStylePreference Copy(AiTeacherSubjectStylePreference source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source with { };
    }
}

public interface IAiTeacherStylePreferenceStore
{
    string RootDirectory { get; }
    Task SaveAsync(AiTeacherSubjectStylePreference preference, CancellationToken cancellationToken = default);
    Task<AiTeacherSubjectStylePreference?> TryLoadAsync(
        string packageId,
        string subjectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Armazenamento local de uma escolha de UI por pacote e matéria. Cada registro é
/// pequeno, validado, escrito de forma atômica e recuperável pelo último backup.
/// </summary>
public sealed class AiTeacherStylePreferenceStore : IAiTeacherStylePreferenceStore, IDisposable
{
    public const long MaximumPreferenceFileBytes = 32 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 12,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private bool _disposed;

    public AiTeacherStylePreferenceStore(string? rootDirectory = null, Func<DateTimeOffset>? utcNow = null)
    {
        var selected = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota", "AI", "teacher", "preferences", "styles");
        if (string.IsNullOrWhiteSpace(selected) || !Path.IsPathFullyQualified(selected))
            throw new AiContractValidationException("O diretório das preferências de estilo precisa ser absoluto.");
        RootDirectory = Path.GetFullPath(selected);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string RootDirectory { get; }

    public async Task SaveAsync(AiTeacherSubjectStylePreference preference, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AiTeacherSubjectStylePreferenceFactory.Validate(preference);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(preference, _jsonOptions);
            if (bytes.LongLength > MaximumPreferenceFileBytes)
                throw new IOException("A preferência de estilo excedeu o limite seguro de armazenamento local.");
            var path = PreferencePath(preference.PackageId, preference.SubjectId);
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

    public async Task<AiTeacherSubjectStylePreference?> TryLoadAsync(
        string packageId,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIdentity(packageId, subjectId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PreferencePath(packageId, subjectId);
            if (!File.Exists(path)) return null;
            try { return await ReadAsync(path, packageId, subjectId, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsInvalidPreferenceError(ex))
            {
                var corrupt = PreserveInvalidFile(path);
                var backup = path + ".bak";
                if (File.Exists(backup))
                {
                    try
                    {
                        var recovered = await ReadAsync(backup, packageId, subjectId, cancellationToken).ConfigureAwait(false);
                        await WriteNewAsync(path, recovered, cancellationToken).ConfigureAwait(false);
                        return recovered;
                    }
                    catch (Exception backupError) when (IsInvalidPreferenceError(backupError))
                    {
                        PreserveInvalidFile(backup);
                    }
                }
                throw new InvalidDataException(
                    $"A preferência de estilo estava inválida e foi preservada como {Path.GetFileName(corrupt)}.", ex);
            }
        }
        finally { _gate.Release(); }
    }

    public DateTimeOffset GetUtcNow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var value = _utcNow();
        if (value == default || value.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("O relógio local não forneceu um timestamp UTC válido.");
        return value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<AiTeacherSubjectStylePreference> ReadAsync(
        string path,
        string packageId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is < 2 or > MaximumPreferenceFileBytes)
            throw new InvalidDataException("O arquivo de preferência de estilo possui tamanho inválido.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        EnsureNoDuplicateProperties(document.RootElement);
        var preference = JsonSerializer.Deserialize<AiTeacherSubjectStylePreference>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo de preferência de estilo está vazio.");
        AiTeacherSubjectStylePreferenceFactory.Validate(preference);
        if (!string.Equals(preference.PackageId, packageId, StringComparison.Ordinal) ||
            !string.Equals(preference.SubjectId, subjectId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A preferência de estilo não corresponde ao arquivo solicitado.");
        }
        return AiTeacherSubjectStylePreferenceFactory.Copy(preference);
    }

    private async Task WriteNewAsync(string path, AiTeacherSubjectStylePreference preference, CancellationToken cancellationToken)
    {
        AiTeacherSubjectStylePreferenceFactory.Validate(preference);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(preference, _jsonOptions);
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

    private string PreferencePath(string packageId, string subjectId) =>
        Path.Combine(RootDirectory, packageId + "--" + subjectId + ".json");

    private string TemporaryPath(string path) => Path.Combine(
        RootDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static void ValidateIdentity(string packageId, string subjectId)
    {
        if (!LearningCatalogIds.IsValid(packageId) || !LearningCatalogIds.IsValid(subjectId))
            throw new AiContractValidationException("A matéria da preferência de estilo é inválida.");
    }

    private static bool IsInvalidPreferenceError(Exception exception) =>
        exception is JsonException or DecoderFallbackException or InvalidDataException or AiContractValidationException;

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
                        throw new InvalidDataException($"A preferência contém a propriedade JSON duplicada '{property.Name}'.");
                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
                break;
        }
    }
}

public interface IAiTeacherStylePreferenceService
{
    Task<AiTeacherSubjectStylePreference?> GetPreferenceAsync(
        AiTeacherSubjectBinding subject,
        CancellationToken cancellationToken = default);

    Task<AiTeacherExplanationStyle?> GetAsync(
        AiTeacherSubjectBinding subject,
        CancellationToken cancellationToken = default);

    Task<AiTeacherExplanationStyle> SetAsync(
        AiTeacherSubjectBinding subject,
        AiTeacherExplanationStyle style,
        CancellationToken cancellationToken = default);

    Task<AiTeacherExplanationStyle> SetAsync(
        AiTeacherSubjectBinding subject,
        AiTeacherExplanationStyle style,
        bool useConversationContinuity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mantém a preferência limitada a uma escolha consciente na interface. Não lê
/// conversas, não chama modelo e não escreve plano, agenda ou progresso.
/// </summary>
public sealed class AiTeacherStylePreferenceService : IAiTeacherStylePreferenceService
{
    private readonly IAiTeacherStylePreferenceStore _store;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AiTeacherStylePreferenceService(
        IAiTeacherStylePreferenceStore store,
        Func<DateTimeOffset>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AiTeacherExplanationStyle?> GetAsync(
        AiTeacherSubjectBinding subject,
        CancellationToken cancellationToken = default)
    {
        var preference = await GetPreferenceAsync(subject, cancellationToken).ConfigureAwait(false);
        return preference?.Style;
    }

    public async Task<AiTeacherSubjectStylePreference?> GetPreferenceAsync(
        AiTeacherSubjectBinding subject,
        CancellationToken cancellationToken = default)
    {
        AiTeacherSubjectBindingFactory.Validate(subject);
        var preference = await _store.TryLoadAsync(subject.PackageId, subject.SubjectId, cancellationToken)
            .ConfigureAwait(false);
        return preference is null ? null : AiTeacherSubjectStylePreferenceFactory.Copy(preference);
    }

    public async Task<AiTeacherExplanationStyle> SetAsync(
        AiTeacherSubjectBinding subject,
        AiTeacherExplanationStyle style,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetPreferenceAsync(subject, cancellationToken).ConfigureAwait(false);
        return await SetAsync(subject, style, existing?.UseConversationContinuity ?? true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AiTeacherExplanationStyle> SetAsync(
        AiTeacherSubjectBinding subject,
        AiTeacherExplanationStyle style,
        bool useConversationContinuity,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _utcNow();
            var preference = AiTeacherSubjectStylePreferenceFactory.Create(
                subject,
                style,
                now,
                useConversationContinuity);
            await _store.SaveAsync(preference, cancellationToken).ConfigureAwait(false);
            return style;
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
