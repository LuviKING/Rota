namespace Rota.Desktop.LocalAI;

public interface IAiTeacherSubjectMemoryService
{
    Task EnsureBindingAsync(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default);

    Task<AiTeacherSubjectMemorySnapshot> RefreshAsync(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default);

    Task<AiTeacherSubjectMemorySnapshot> GetAsync(
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Memória factual por matéria. O conteúdo vem do histórico completo e a associação
/// vem de vínculos verificados contra cada contexto congelado. Não consulta o LLM,
/// não altera calendário/progresso e não injeta memória em requisições da professora.
/// O arquivo por matéria é somente um índice derivado: toda leitura pública o
/// reconstrói das fontes locais verificadas para não aceitar cache desatualizado.
/// </summary>
public sealed class AiTeacherSubjectMemoryService : IAiTeacherSubjectMemoryService
{
    private readonly IAiTeacherConversationStore _conversationStore;
    private readonly IAiTeacherSubjectBindingStore _bindingStore;
    private readonly IAiTeacherSubjectMemoryStore _memoryStore;

    public AiTeacherSubjectMemoryService(
        IAiTeacherConversationStore conversationStore,
        IAiTeacherSubjectBindingStore bindingStore,
        IAiTeacherSubjectMemoryStore memoryStore)
    {
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _bindingStore = bindingStore ?? throw new ArgumentNullException(nameof(bindingStore));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
    }

    public async Task EnsureBindingAsync(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        AiTeacherSubjectBindingFactory.Validate(binding, conversation.LessonContext);

        // Passe primeiro pelo caminho de leitura com recuperação. Assim um sidecar
        // semanticamente corrompido é preservado/recuperado antes de uma nova escrita,
        // sem transformar a matéria da conversa por heurística.
        var existing = await _bindingStore.TryLoadAsync(conversation.ConversationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && !AiTeacherSubjectBindingFactory.Equivalent(existing.Binding, binding))
            throw new AiContractValidationException(
                "Uma conversa da Professora Local não pode trocar de matéria ou origem pedagógica.");

        await _bindingStore.BindAsync(conversation, binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiTeacherSubjectMemorySnapshot> RefreshAsync(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        await EnsureBindingAsync(conversation, binding, cancellationToken).ConfigureAwait(false);
        await InspectDerivedCacheAsync(binding, cancellationToken).ConfigureAwait(false);
        return await RebuildAsync(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiTeacherSubjectMemorySnapshot> GetAsync(
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken = default)
    {
        AiTeacherSubjectBindingFactory.Validate(binding);
        await InspectDerivedCacheAsync(binding, cancellationToken).ConfigureAwait(false);
        return await RebuildAsync(binding, cancellationToken).ConfigureAwait(false);
    }

    private async Task InspectDerivedCacheAsync(
        AiTeacherSubjectBinding binding,
        CancellationToken cancellationToken)
    {
        try
        {
            // A leitura existe para acionar validação, preservação de corrupção e
            // recuperação de backup. Mesmo um cache válido é reconstruído depois,
            // pois ele pode estar desatualizado em relação aos vínculos/histórico.
            _ = await _memoryStore.TryLoadAsync(binding.PackageId, binding.SubjectId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            // O store já preservou os arquivos inválidos. O índice pode nascer de
            // novo somente das conversas e vínculos locais verificados.
        }
    }

    private async Task<AiTeacherSubjectMemorySnapshot> RebuildAsync(
        AiTeacherSubjectBinding identity,
        CancellationToken cancellationToken)
    {
        var bindings = await _bindingStore
            .ListBySubjectAsync(identity.PackageId, identity.SubjectId, cancellationToken)
            .ConfigureAwait(false);
        var indexes = new List<AiTeacherSubjectMemoryConversation>();
        var lessons = new List<AiTeacherSubjectMemoryLesson>();

        foreach (var entry in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AiTeacherConversationSnapshot conversation;
            try
            {
                conversation = await _conversationStore.LoadAsync(entry.ConversationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                // Vínculo órfão não cria memória fictícia.
                continue;
            }

            AiTeacherSubjectBindingFactory.Validate(entry.Binding, conversation.LessonContext);
            indexes.Add(AiTeacherSubjectMemoryFactory.CreateConversationIndex(conversation, entry.Binding));
            lessons.Add(AiTeacherSubjectMemoryFactory.CreateLesson(conversation, entry.Binding));
        }

        if (indexes.Count == 0)
            throw new KeyNotFoundException("Ainda não existe histórico vinculado a esta matéria.");

        var rebuilt = AiTeacherSubjectMemoryFactory.Build(identity.PackageId, identity.SubjectId, indexes, lessons);
        await _memoryStore.SaveAsync(rebuilt, cancellationToken).ConfigureAwait(false);
        return rebuilt;
    }
}
