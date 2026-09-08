using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop.LocalAI;

/// <summary>
/// Recorte compacto e determinístico de uma troca concluída. É derivado somente
/// do histórico pedagógico já validado; não representa memória ativa, diagnóstico
/// do aluno ou nova inferência do modelo.
/// </summary>
public sealed record AiTeacherLessonSummaryExchange
{
    public Guid ExchangeId { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public AiTeacherRequestMode Mode { get; init; }
    public AiTeacherExplanationStyle ExplanationStyle { get; init; }
    public string Question { get; init; } = "";
    public bool QuestionTruncated { get; init; }
    public string AnswerTitle { get; init; } = "";
    public string Recap { get; init; } = "";
    public bool RecapTruncated { get; init; }
    public AiTeacherGroundingConfidence GroundingConfidence { get; init; }
    public AiTeacherKnowledgeStatus KnowledgeStatus { get; init; }
}

public sealed record AiTeacherLessonSummarySnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid ConversationId { get; init; }
    public string ContentId { get; init; } = "";
    public string ContentTitle { get; init; } = "";
    public DateTimeOffset LessonCreatedAtUtc { get; init; }
    public DateTimeOffset SourceUpdatedAtUtc { get; init; }
    public int SourceExchangeCount { get; init; }
    public int CompletedExchangeCount { get; init; }
    public int UnansweredExchangeCount { get; init; }
    public IReadOnlyList<AiTeacherLessonSummaryExchange> Exchanges { get; init; } = Array.Empty<AiTeacherLessonSummaryExchange>();
}

/// <summary>
/// Produz o resumo exclusivamente a partir do histórico completo. O resultado é
/// estável para a mesma fotografia da conversa e nunca afirma domínio, dificuldade,
/// preferência ou progresso que não estejam explicitamente registrados.
/// </summary>
public static class AiTeacherLessonSummaryFactory
{
    public const int MaximumQuestionCharacters = 240;
    public const int MaximumRecapCharacters = 420;
    public const int MaximumSummaryExchanges = AiTeacherConversationStore.MaximumExchangesPerConversation;

    public static AiTeacherLessonSummarySnapshot Create(AiTeacherConversationSnapshot conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ValidateSource(conversation);

        var completed = conversation.Exchanges
            .Where(item => item.Status == AiTeacherConversationExchangeStatus.Completed)
            .Select(CreateExchange)
            .ToList();

        var result = new AiTeacherLessonSummarySnapshot
        {
            ConversationId = conversation.ConversationId,
            ContentId = conversation.LessonContext.ContentId,
            ContentTitle = conversation.LessonContext.ContentTitle,
            LessonCreatedAtUtc = conversation.CreatedAtUtc,
            SourceUpdatedAtUtc = conversation.UpdatedAtUtc,
            SourceExchangeCount = conversation.Exchanges.Count,
            CompletedExchangeCount = completed.Count,
            UnansweredExchangeCount = conversation.Exchanges.Count - completed.Count,
            Exchanges = completed.AsReadOnly()
        };
        Validate(result);
        return result;
    }

    public static void Validate(AiTeacherLessonSummarySnapshot summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.SchemaVersion != AiTeacherLessonSummarySnapshot.CurrentSchemaVersion ||
            summary.ConversationId == Guid.Empty ||
            summary.LessonCreatedAtUtc == default || summary.LessonCreatedAtUtc.Offset != TimeSpan.Zero ||
            summary.SourceUpdatedAtUtc == default || summary.SourceUpdatedAtUtc.Offset != TimeSpan.Zero ||
            summary.SourceUpdatedAtUtc < summary.LessonCreatedAtUtc ||
            string.IsNullOrWhiteSpace(summary.ContentId) || summary.ContentId.Length > 160 ||
            string.IsNullOrWhiteSpace(summary.ContentTitle) || summary.ContentTitle.Length > 240 ||
            summary.SourceExchangeCount < 0 || summary.SourceExchangeCount > AiTeacherConversationStore.MaximumExchangesPerConversation ||
            summary.CompletedExchangeCount < 0 || summary.UnansweredExchangeCount < 0 ||
            summary.CompletedExchangeCount + summary.UnansweredExchangeCount != summary.SourceExchangeCount ||
            summary.Exchanges is null || summary.Exchanges.Count != summary.CompletedExchangeCount ||
            summary.Exchanges.Count > MaximumSummaryExchanges)
        {
            throw new InvalidDataException("A estrutura do resumo automático da Professora Local é inválida.");
        }

        var ids = new HashSet<Guid>();
        foreach (var exchange in summary.Exchanges)
        {
            if (exchange is null || exchange.ExchangeId == Guid.Empty || !ids.Add(exchange.ExchangeId) ||
                exchange.UpdatedAtUtc == default || exchange.UpdatedAtUtc.Offset != TimeSpan.Zero ||
                exchange.UpdatedAtUtc > summary.SourceUpdatedAtUtc ||
                !Enum.IsDefined(exchange.Mode) || !Enum.IsDefined(exchange.ExplanationStyle) ||
                !Enum.IsDefined(exchange.GroundingConfidence) || !Enum.IsDefined(exchange.KnowledgeStatus))
            {
                throw new InvalidDataException("O resumo automático contém uma troca inválida.");
            }

            ValidateCompactText(exchange.Question, MaximumQuestionCharacters, allowEmpty: false);
            ValidateCompactText(exchange.AnswerTitle, 160, allowEmpty: false);
            ValidateCompactText(exchange.Recap, MaximumRecapCharacters, allowEmpty: false);
            if (exchange.QuestionTruncated && exchange.Question.Length != MaximumQuestionCharacters)
                throw new InvalidDataException("O marcador de truncamento da pergunta é inconsistente.");
            if (exchange.RecapTruncated && exchange.Recap.Length != MaximumRecapCharacters)
                throw new InvalidDataException("O marcador de truncamento do resumo é inconsistente.");
        }
    }

    public static bool Equivalent(AiTeacherLessonSummarySnapshot left, AiTeacherLessonSummarySnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.SchemaVersion == right.SchemaVersion &&
               left.ConversationId == right.ConversationId &&
               left.ContentId == right.ContentId &&
               left.ContentTitle == right.ContentTitle &&
               left.LessonCreatedAtUtc == right.LessonCreatedAtUtc &&
               left.SourceUpdatedAtUtc == right.SourceUpdatedAtUtc &&
               left.SourceExchangeCount == right.SourceExchangeCount &&
               left.CompletedExchangeCount == right.CompletedExchangeCount &&
               left.UnansweredExchangeCount == right.UnansweredExchangeCount &&
               left.Exchanges.SequenceEqual(right.Exchanges);
    }

    private static AiTeacherLessonSummaryExchange CreateExchange(AiTeacherConversationExchange exchange)
    {
        var answer = exchange.Answer ?? throw new InvalidDataException("Uma troca concluída está sem resposta no histórico.");
        var grounding = exchange.Grounding ?? throw new InvalidDataException("Uma troca concluída está sem grounding no histórico.");
        var knowledge = exchange.Knowledge ?? throw new InvalidDataException("Uma troca concluída está sem estado de conhecimento no histórico.");
        var question = Compact(exchange.Question, MaximumQuestionCharacters);
        var recap = Compact(answer.Recap, MaximumRecapCharacters);
        return new AiTeacherLessonSummaryExchange
        {
            ExchangeId = exchange.ExchangeId,
            UpdatedAtUtc = exchange.UpdatedAtUtc,
            Mode = exchange.Mode,
            ExplanationStyle = exchange.ExplanationStyle,
            Question = question.Text,
            QuestionTruncated = question.Truncated,
            AnswerTitle = answer.Title,
            Recap = recap.Text,
            RecapTruncated = recap.Truncated,
            GroundingConfidence = grounding.Confidence,
            KnowledgeStatus = knowledge.Status
        };
    }

    private static void ValidateSource(AiTeacherConversationSnapshot conversation)
    {
        if (conversation.SchemaVersion != AiTeacherConversationSnapshot.CurrentSchemaVersion ||
            conversation.ConversationId == Guid.Empty ||
            conversation.CreatedAtUtc == default || conversation.CreatedAtUtc.Offset != TimeSpan.Zero ||
            conversation.UpdatedAtUtc == default || conversation.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            conversation.UpdatedAtUtc < conversation.CreatedAtUtc ||
            conversation.LessonContext is null || conversation.Exchanges is null ||
            conversation.Exchanges.Count > AiTeacherConversationStore.MaximumExchangesPerConversation)
        {
            throw new InvalidDataException("A conversa usada para o resumo automático é inválida.");
        }
        AiTeacherContractValidator.ValidateLessonContext(conversation.LessonContext);
        foreach (var exchange in conversation.Exchanges)
        {
            if (exchange is null || exchange.ExchangeId == Guid.Empty || !Enum.IsDefined(exchange.Status))
                throw new InvalidDataException("A conversa usada para o resumo contém uma troca inválida.");
            if (exchange.Status == AiTeacherConversationExchangeStatus.Completed &&
                (exchange.Answer is null || exchange.Grounding is null || exchange.Knowledge is null))
                throw new InvalidDataException("Uma troca concluída está incompleta para resumir.");
        }
    }

    private static (string Text, bool Truncated) Compact(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("O histórico não possui texto suficiente para gerar um resumo seguro.");
        var normalized = value.Trim();
        if (normalized.Length <= maximum) return (normalized, false);
        return (normalized[..maximum], true);
    }

    private static void ValidateCompactText(string? value, int maximum, bool allowEmpty)
    {
        if (value is null || value.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
            value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            throw new InvalidDataException("O resumo automático contém texto inválido.");
    }
}

public interface IAiTeacherLessonSummaryStore
{
    string RootDirectory { get; }
    Task SaveAsync(AiTeacherLessonSummarySnapshot summary, CancellationToken cancellationToken = default);
    Task<AiTeacherLessonSummarySnapshot?> TryLoadAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache derivado dos resumos de aula. O histórico completo continua sendo a fonte
/// de verdade e permite reconstruir qualquer sidecar ausente, antigo ou corrompido.
/// </summary>
public sealed class AiTeacherLessonSummaryStore : IAiTeacherLessonSummaryStore, IDisposable
{
    public const long MaximumSummaryFileBytes = 1L * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private bool _disposed;

    public AiTeacherLessonSummaryStore(string? rootDirectory = null)
    {
        var selected = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota", "AI", "teacher", "summaries");
        if (string.IsNullOrWhiteSpace(selected) || !Path.IsPathFullyQualified(selected))
            throw new AiContractValidationException("O diretório dos resumos da Professora Local precisa ser absoluto.");
        RootDirectory = Path.GetFullPath(selected);
    }

    public string RootDirectory { get; }

    public async Task SaveAsync(AiTeacherLessonSummarySnapshot summary, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AiTeacherLessonSummaryFactory.Validate(summary);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(summary, _jsonOptions);
            if (bytes.LongLength > MaximumSummaryFileBytes)
                throw new IOException("O resumo da aula excedeu o limite seguro de armazenamento local.");
            var path = SummaryPath(summary.ConversationId);
            var temporary = TemporaryPath(path);
            try
            {
                await WriteTemporaryAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
                else File.Move(temporary, path);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AiTeacherLessonSummarySnapshot?> TryLoadAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (conversationId == Guid.Empty)
            throw new AiContractValidationException("O resumo precisa de um ID de conversa válido.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = SummaryPath(conversationId);
            if (!File.Exists(path)) return null;
            try
            {
                return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsInvalidSummaryError(ex))
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
                    catch (Exception backupError) when (IsInvalidSummaryError(backupError))
                    {
                        PreserveInvalidFile(backup);
                    }
                }
                throw new InvalidDataException(
                    $"O resumo automático estava inválido e foi preservado como {Path.GetFileName(corrupt)}.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<AiTeacherLessonSummarySnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is < 2 or > MaximumSummaryFileBytes)
            throw new InvalidDataException("O arquivo de resumo possui tamanho inválido.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        EnsureNoDuplicateProperties(document.RootElement);
        var summary = JsonSerializer.Deserialize<AiTeacherLessonSummarySnapshot>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo de resumo está vazio.");
        AiTeacherLessonSummaryFactory.Validate(summary);
        return summary;
    }

    private async Task WriteNewAsync(string path, AiTeacherLessonSummarySnapshot summary, CancellationToken cancellationToken)
    {
        AiTeacherLessonSummaryFactory.Validate(summary);
        Directory.CreateDirectory(RootDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(summary, _jsonOptions);
        var temporary = TemporaryPath(path);
        try
        {
            await WriteTemporaryAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task WriteTemporaryAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private string SummaryPath(Guid id) => Path.Combine(RootDirectory, id.ToString("D") + ".json");
    private string TemporaryPath(string path) => Path.Combine(RootDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static string PreserveInvalidFile(string path)
    {
        if (!File.Exists(path)) return path;
        var preserved = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        File.Move(path, preserved);
        return preserved;
    }

    private static bool IsInvalidSummaryError(Exception exception) =>
        exception is JsonException or DecoderFallbackException or InvalidDataException or AiContractValidationException;

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
                        throw new InvalidDataException($"O resumo contém a propriedade JSON duplicada '{property.Name}'.");
                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
                break;
        }
    }
}

public interface IAiTeacherLessonSummaryService
{
    Task<AiTeacherLessonSummarySnapshot> RefreshAsync(
        AiTeacherConversationSnapshot conversation,
        CancellationToken cancellationToken = default);

    Task<AiTeacherLessonSummarySnapshot> GetAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mantém o sidecar sincronizado com o histórico completo. Um sidecar ausente,
/// antigo, alterado ou corrompido é reconstruído localmente sem chamar o modelo.
/// </summary>
public sealed class AiTeacherLessonSummaryService : IAiTeacherLessonSummaryService
{
    private readonly IAiTeacherConversationStore _conversationStore;
    private readonly IAiTeacherLessonSummaryStore _summaryStore;

    public AiTeacherLessonSummaryService(
        IAiTeacherConversationStore conversationStore,
        IAiTeacherLessonSummaryStore summaryStore)
    {
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _summaryStore = summaryStore ?? throw new ArgumentNullException(nameof(summaryStore));
    }

    public async Task<AiTeacherLessonSummarySnapshot> RefreshAsync(
        AiTeacherConversationSnapshot conversation,
        CancellationToken cancellationToken = default)
    {
        var expected = AiTeacherLessonSummaryFactory.Create(conversation);
        await _summaryStore.SaveAsync(expected, cancellationToken).ConfigureAwait(false);
        return expected;
    }

    public async Task<AiTeacherLessonSummarySnapshot> GetAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
            throw new AiContractValidationException("O resumo precisa de um ID de conversa válido.");
        var conversation = await _conversationStore.LoadAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var expected = AiTeacherLessonSummaryFactory.Create(conversation);
        AiTeacherLessonSummarySnapshot? stored = null;
        try
        {
            stored = await _summaryStore.TryLoadAsync(conversationId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            // O histórico completo é a fonte de verdade. O sidecar inválido já foi
            // preservado pelo store e pode ser reconstruído com segurança abaixo.
        }

        if (stored is not null && AiTeacherLessonSummaryFactory.Equivalent(stored, expected))
            return stored;

        await _summaryStore.SaveAsync(expected, cancellationToken).ConfigureAwait(false);
        return expected;
    }
}
