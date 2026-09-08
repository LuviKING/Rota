namespace Rota.Desktop.LocalAI;

/// <summary>
/// Sinal local e verificável de um ponto que apareceu em mais de uma conversa
/// da mesma matéria. Ele não mede capacidade, não diagnostica o aluno e não é
/// enviado ao modelo: apenas resume categorias fechadas já arquivadas pela aula.
/// </summary>
public sealed record AiTeacherRecurringLearningSignal
{
    public AiTeacherHiddenDoubtKind Kind { get; init; }
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public int ConversationCount { get; init; }
    public int ExchangeCount { get; init; }
    public DateTimeOffset FirstObservedAtUtc { get; init; }
    public DateTimeOffset LastObservedAtUtc { get; init; }
    public IReadOnlyList<string> RecentContentTitles { get; init; } = Array.Empty<string>();
}

public sealed record AiTeacherRecurringLearningSignalsSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string PackageId { get; init; } = "";
    public string SubjectId { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public int ConversationsScanned { get; init; }
    public int CompletedExchangesScanned { get; init; }
    public IReadOnlyList<AiTeacherRecurringLearningSignal> Signals { get; init; } =
        Array.Empty<AiTeacherRecurringLearningSignal>();
}

public sealed record AiTeacherRecurringLearningSignalSource
{
    public AiTeacherConversationSnapshot Conversation { get; init; } = new();
    public AiTeacherSubjectBinding Binding { get; init; } = new();
}

public static class AiTeacherRecurringLearningSignalFactory
{
    public const int MinimumDistinctConversations = 2;
    public const int MaximumRecentContentTitles = 3;

    public static AiTeacherRecurringLearningSignalsSnapshot Build(
        AiTeacherSubjectBinding subject,
        IEnumerable<AiTeacherRecurringLearningSignalSource> sources)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(sources);
        AiTeacherSubjectBindingFactory.Validate(subject);

        var observations = new List<Observation>();
        var conversations = new HashSet<Guid>();
        var completedExchanges = 0;
        foreach (var source in sources)
        {
            if (source is null || source.Conversation is null || source.Binding is null)
                throw new InvalidDataException("O sinal recorrente contém uma fonte local inválida.");
            ValidateSource(subject, source);
            conversations.Add(source.Conversation.ConversationId);

            foreach (var exchange in source.Conversation.Exchanges)
            {
                if (exchange.Status != AiTeacherConversationExchangeStatus.Completed) continue;
                completedExchanges++;
                var answer = exchange.Answer ?? throw new InvalidDataException(
                    "Uma conversa concluída está sem resposta para o sinal recorrente.");
                if (answer.HiddenDoubts is null)
                    throw new InvalidDataException("Uma resposta concluída está sem dúvidas estruturadas para o sinal recorrente.");
                foreach (var kind in answer.HiddenDoubts
                             .Select(item => item.Kind)
                             .Distinct())
                {
                    observations.Add(new Observation(
                        kind,
                        source.Conversation.ConversationId,
                        source.Binding.ContentTitle,
                        exchange.UpdatedAtUtc));
                }
            }
        }

        var signals = observations
            .GroupBy(item => item.Kind)
            .Select(group => CreateSignal(group))
            .Where(item => item.ConversationCount >= MinimumDistinctConversations)
            .OrderByDescending(item => item.ConversationCount)
            .ThenByDescending(item => item.ExchangeCount)
            .ThenByDescending(item => item.LastObservedAtUtc)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        var result = new AiTeacherRecurringLearningSignalsSnapshot
        {
            PackageId = subject.PackageId,
            SubjectId = subject.SubjectId,
            SubjectName = subject.SubjectName,
            ConversationsScanned = conversations.Count,
            CompletedExchangesScanned = completedExchanges,
            Signals = signals
        };
        Validate(result);
        return result;
    }

    public static void Validate(AiTeacherRecurringLearningSignalsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != AiTeacherRecurringLearningSignalsSnapshot.CurrentSchemaVersion ||
            !LearningCatalogIds.IsValid(snapshot.PackageId) ||
            !LearningCatalogIds.IsValid(snapshot.SubjectId) ||
            string.IsNullOrWhiteSpace(snapshot.SubjectName) || snapshot.SubjectName.Length > 160 ||
            snapshot.ConversationsScanned < 0 ||
            snapshot.CompletedExchangesScanned < 0 ||
            snapshot.Signals is null || snapshot.Signals.Count > AiTeacherHiddenDoubtKinds.All.Count)
        {
            throw new InvalidDataException("A estrutura dos sinais recorrentes da Professora Local é inválida.");
        }

        var kinds = new HashSet<AiTeacherHiddenDoubtKind>();
        foreach (var signal in snapshot.Signals)
        {
            if (signal is null || !AiTeacherHiddenDoubtKinds.IsSupported(signal.Kind) ||
                !kinds.Add(signal.Kind) || signal.ConversationCount < MinimumDistinctConversations ||
                signal.ConversationCount > snapshot.ConversationsScanned ||
                signal.ExchangeCount < signal.ConversationCount ||
                signal.ExchangeCount > snapshot.CompletedExchangesScanned ||
                signal.FirstObservedAtUtc == default || signal.FirstObservedAtUtc.Offset != TimeSpan.Zero ||
                signal.LastObservedAtUtc == default || signal.LastObservedAtUtc.Offset != TimeSpan.Zero ||
                signal.LastObservedAtUtc < signal.FirstObservedAtUtc ||
                signal.RecentContentTitles is null || signal.RecentContentTitles.Count > MaximumRecentContentTitles)
            {
                throw new InvalidDataException("Um sinal recorrente da Professora Local é inválido.");
            }

            var descriptor = AiTeacherHiddenDoubtKinds.Get(signal.Kind);
            if (!string.Equals(signal.Id, descriptor.Id, StringComparison.Ordinal) ||
                !string.Equals(signal.DisplayName, descriptor.DisplayName, StringComparison.Ordinal) ||
                !string.Equals(signal.Description, descriptor.Description, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Um sinal recorrente não corresponde ao catálogo pedagógico fechado.");
            }

            var titles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var title in signal.RecentContentTitles)
            {
                if (string.IsNullOrWhiteSpace(title) || title.Length > 240 || !titles.Add(title))
                    throw new InvalidDataException("O sinal recorrente possui conteúdos recentes inválidos.");
            }
        }
    }

    private static AiTeacherRecurringLearningSignal CreateSignal(
        IGrouping<AiTeacherHiddenDoubtKind, Observation> group)
    {
        var descriptor = AiTeacherHiddenDoubtKinds.Get(group.Key);
        var ordered = group.OrderByDescending(item => item.ObservedAtUtc)
            .ThenBy(item => item.ConversationId)
            .ToList();
        return new AiTeacherRecurringLearningSignal
        {
            Kind = group.Key,
            Id = descriptor.Id,
            DisplayName = descriptor.DisplayName,
            Description = descriptor.Description,
            ConversationCount = group.Select(item => item.ConversationId).Distinct().Count(),
            ExchangeCount = group.Count(),
            FirstObservedAtUtc = group.Min(item => item.ObservedAtUtc),
            LastObservedAtUtc = group.Max(item => item.ObservedAtUtc),
            RecentContentTitles = ordered.Select(item => item.ContentTitle)
                .Distinct(StringComparer.Ordinal)
                .Take(MaximumRecentContentTitles)
                .ToList()
                .AsReadOnly()
        };
    }

    private static void ValidateSource(
        AiTeacherSubjectBinding subject,
        AiTeacherRecurringLearningSignalSource source)
    {
        var conversation = source.Conversation;
        var binding = source.Binding;
        if (conversation.SchemaVersion != AiTeacherConversationSnapshot.CurrentSchemaVersion ||
            conversation.ConversationId == Guid.Empty || conversation.LessonContext is null ||
            conversation.Exchanges is null || conversation.Exchanges.Count > AiTeacherConversationStore.MaximumExchangesPerConversation)
        {
            throw new InvalidDataException("A conversa usada pelos sinais recorrentes é inválida.");
        }
        AiTeacherSubjectBindingFactory.Validate(binding, conversation.LessonContext);
        if (!AiTeacherSubjectBindingFactory.SameSubject(subject, binding))
            throw new AiContractValidationException("A conversa não pertence à matéria consultada.");
    }

    private sealed record Observation(
        AiTeacherHiddenDoubtKind Kind,
        Guid ConversationId,
        string ContentTitle,
        DateTimeOffset ObservedAtUtc);
}

public interface IAiTeacherRecurringLearningSignalService
{
    Task<AiTeacherRecurringLearningSignalsSnapshot> GetAsync(
        AiTeacherSubjectBinding subject,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lê apenas conversas já vinculadas a uma matéria verificada. Não cria perfil,
/// não persiste uma cópia adicional, não chama modelo e não recupera dados para o
/// prompt. A fonte de verdade continua sendo o histórico completo da conversa.
/// </summary>
public sealed class AiTeacherRecurringLearningSignalService : IAiTeacherRecurringLearningSignalService
{
    private readonly IAiTeacherConversationStore _conversationStore;
    private readonly IAiTeacherSubjectBindingStore _bindingStore;

    public AiTeacherRecurringLearningSignalService(
        IAiTeacherConversationStore conversationStore,
        IAiTeacherSubjectBindingStore bindingStore)
    {
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _bindingStore = bindingStore ?? throw new ArgumentNullException(nameof(bindingStore));
    }

    public async Task<AiTeacherRecurringLearningSignalsSnapshot> GetAsync(
        AiTeacherSubjectBinding subject,
        CancellationToken cancellationToken = default)
    {
        AiTeacherSubjectBindingFactory.Validate(subject);
        var bindings = await _bindingStore.ListBySubjectAsync(
                subject.PackageId,
                subject.SubjectId,
                cancellationToken)
            .ConfigureAwait(false);
        var sources = new List<AiTeacherRecurringLearningSignalSource>();
        foreach (var item in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var conversation = await _conversationStore.LoadAsync(item.ConversationId, cancellationToken)
                    .ConfigureAwait(false);
                sources.Add(new AiTeacherRecurringLearningSignalSource
                {
                    Conversation = conversation,
                    Binding = item.Binding
                });
            }
            catch (KeyNotFoundException)
            {
                // Um sidecar órfão não deve produzir um sinal imaginado.
            }
        }
        return AiTeacherRecurringLearningSignalFactory.Build(subject, sources);
    }
}
