namespace Rota.Desktop.LocalAI;

/// <summary>
/// Constrói memória factual por matéria somente a partir de conversas completas e
/// vínculos verificados. Não infere domínio, dificuldade, erro ou preferência.
/// </summary>
public static class AiTeacherSubjectMemoryFactory
{
    public const int MaximumDetailedLessons = 64;
    public const int MaximumRecentExchangesPerLesson = 6;

    public static AiTeacherSubjectMemoryConversation CreateConversationIndex(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding)
    {
        ValidateBoundConversation(conversation, binding);
        var completed = conversation.Exchanges.Count(item => item.Status == AiTeacherConversationExchangeStatus.Completed);
        return new AiTeacherSubjectMemoryConversation
        {
            ConversationId = conversation.ConversationId,
            PackageVersion = binding.PackageVersion,
            SubjectName = binding.SubjectName,
            ContentId = binding.ContentId,
            ContentTitle = binding.ContentTitle,
            CreatedAtUtc = conversation.CreatedAtUtc,
            UpdatedAtUtc = conversation.UpdatedAtUtc,
            SourceExchangeCount = conversation.Exchanges.Count,
            CompletedExchangeCount = completed,
            UnansweredExchangeCount = conversation.Exchanges.Count - completed
        };
    }

    public static AiTeacherSubjectMemoryLesson CreateLesson(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding)
    {
        ValidateBoundConversation(conversation, binding);
        var summary = AiTeacherLessonSummaryFactory.Create(conversation);
        // O histórico já possui ordem canônica validada. Preservá-la evita que
        // timestamps empatados reordenem perguntas por GUID aleatório.
        var selected = summary.Exchanges
            .TakeLast(MaximumRecentExchangesPerLesson)
            .Select(item => item with { })
            .ToList()
            .AsReadOnly();
        return new AiTeacherSubjectMemoryLesson
        {
            ConversationId = conversation.ConversationId,
            ContentId = summary.ContentId,
            ContentTitle = summary.ContentTitle,
            CreatedAtUtc = summary.LessonCreatedAtUtc,
            UpdatedAtUtc = summary.SourceUpdatedAtUtc,
            HasEarlierCompletedExchanges = summary.CompletedExchangeCount > selected.Count,
            RecentExchanges = selected
        };
    }

    public static AiTeacherSubjectMemorySnapshot Build(
        string packageId,
        string subjectId,
        IEnumerable<AiTeacherSubjectMemoryConversation> conversations,
        IEnumerable<AiTeacherSubjectMemoryLesson> detailedLessons)
    {
        if (!LearningCatalogIds.IsValid(packageId) || !LearningCatalogIds.IsValid(subjectId))
            throw new AiContractValidationException("A identidade da memória por matéria é inválida.");
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(detailedLessons);

        var indexes = conversations
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.ConversationId)
            .ToList();
        if (indexes.Count is < 1 or > AiTeacherConversationStore.MaximumConversationFiles)
            throw new InvalidDataException("A memória por matéria possui uma quantidade inválida de conversas.");
        if (indexes.Select(item => item.ConversationId).Distinct().Count() != indexes.Count)
            throw new InvalidDataException("A memória por matéria possui conversas duplicadas.");

        var detailGroups = detailedLessons.GroupBy(item => item.ConversationId).ToList();
        if (detailGroups.Any(group => group.Count() != 1))
            throw new InvalidDataException("A memória por matéria possui detalhes de aula duplicados.");
        var detailsById = detailGroups.ToDictionary(group => group.Key, group => group.Single());
        var recent = indexes
            .Take(MaximumDetailedLessons)
            .Where(item => detailsById.ContainsKey(item.ConversationId))
            .Select(item => detailsById[item.ConversationId])
            .ToList()
            .AsReadOnly();
        var latest = indexes[0];

        var result = new AiTeacherSubjectMemorySnapshot
        {
            PackageId = packageId,
            SubjectId = subjectId,
            SubjectName = latest.SubjectName,
            LatestPackageVersion = latest.PackageVersion,
            UpdatedAtUtc = latest.UpdatedAtUtc,
            ConversationCount = indexes.Count,
            CompletedExchangeCount = indexes.Sum(item => item.CompletedExchangeCount),
            UnansweredExchangeCount = indexes.Sum(item => item.UnansweredExchangeCount),
            HasMoreDetailedLessons = indexes.Count > recent.Count,
            Conversations = indexes.AsReadOnly(),
            RecentLessons = recent
        };
        Validate(result);
        return result;
    }

    public static AiTeacherSubjectMemorySnapshot Upsert(
        AiTeacherSubjectMemorySnapshot existing,
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding)
    {
        Validate(existing);
        ValidateBoundConversation(conversation, binding);
        if (!string.Equals(existing.PackageId, binding.PackageId, StringComparison.Ordinal) ||
            !string.Equals(existing.SubjectId, binding.SubjectId, StringComparison.Ordinal))
            throw new AiContractValidationException("A conversa pertence a outra memória de matéria.");

        var index = CreateConversationIndex(conversation, binding);
        var lesson = CreateLesson(conversation, binding);
        var indexes = existing.Conversations
            .Where(item => item.ConversationId != conversation.ConversationId)
            .Append(index)
            .ToList();
        var details = existing.RecentLessons
            .Where(item => item.ConversationId != conversation.ConversationId)
            .Append(lesson)
            .ToList();
        return Build(existing.PackageId, existing.SubjectId, indexes, details);
    }

    public static void Validate(AiTeacherSubjectMemorySnapshot memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (memory.SchemaVersion != AiTeacherSubjectMemorySnapshot.CurrentSchemaVersion ||
            !LearningCatalogIds.IsValid(memory.PackageId) || !LearningCatalogIds.IsValid(memory.SubjectId) ||
            memory.UpdatedAtUtc == default || memory.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            memory.ConversationCount is < 1 or > AiTeacherConversationStore.MaximumConversationFiles ||
            memory.CompletedExchangeCount < 0 || memory.UnansweredExchangeCount < 0 ||
            memory.Conversations is null || memory.Conversations.Count != memory.ConversationCount ||
            memory.RecentLessons is null || memory.RecentLessons.Count > MaximumDetailedLessons ||
            memory.HasMoreDetailedLessons != (memory.ConversationCount > memory.RecentLessons.Count))
            throw new InvalidDataException("A estrutura da memória por matéria é inválida.");

        AiTeacherSubjectBindingFactory.ValidateText(memory.SubjectName, 160, "nome da matéria");
        if (!AiTeacherSubjectBindingFactory.IsSemanticVersion(memory.LatestPackageVersion))
            throw new InvalidDataException("A versão mais recente da memória por matéria é inválida.");

        var ids = new HashSet<Guid>();
        var previous = DateTimeOffset.MaxValue;
        var completed = 0;
        var unanswered = 0;
        foreach (var item in memory.Conversations)
        {
            ValidateIndex(item);
            if (!ids.Add(item.ConversationId) || item.UpdatedAtUtc > previous || item.UpdatedAtUtc > memory.UpdatedAtUtc)
                throw new InvalidDataException("O índice da memória por matéria está duplicado ou fora de ordem.");
            previous = item.UpdatedAtUtc;
            completed += item.CompletedExchangeCount;
            unanswered += item.UnansweredExchangeCount;
        }
        if (completed != memory.CompletedExchangeCount || unanswered != memory.UnansweredExchangeCount ||
            memory.UpdatedAtUtc != memory.Conversations[0].UpdatedAtUtc ||
            memory.SubjectName != memory.Conversations[0].SubjectName ||
            memory.LatestPackageVersion != memory.Conversations[0].PackageVersion)
            throw new InvalidDataException("Os totais da memória por matéria são inconsistentes.");

        var detailIds = new HashSet<Guid>();
        foreach (var lesson in memory.RecentLessons)
        {
            ValidateLesson(lesson);
            if (!detailIds.Add(lesson.ConversationId) || !ids.Contains(lesson.ConversationId))
                throw new InvalidDataException("A memória detalhada referencia uma conversa inválida.");
            var index = memory.Conversations.Single(item => item.ConversationId == lesson.ConversationId);
            if (!string.Equals(index.ContentId, lesson.ContentId, StringComparison.Ordinal) ||
                !string.Equals(index.ContentTitle, lesson.ContentTitle, StringComparison.Ordinal) ||
                index.CreatedAtUtc != lesson.CreatedAtUtc || index.UpdatedAtUtc != lesson.UpdatedAtUtc)
                throw new InvalidDataException("A memória detalhada não corresponde ao índice da conversa.");
        }
    }

    private static void ValidateBoundConversation(
        AiTeacherConversationSnapshot conversation,
        AiTeacherSubjectBinding binding)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        AiTeacherSubjectBindingFactory.Validate(binding, conversation.LessonContext);
        if (conversation.ConversationId == Guid.Empty || conversation.Exchanges is null ||
            conversation.CreatedAtUtc == default || conversation.CreatedAtUtc.Offset != TimeSpan.Zero ||
            conversation.UpdatedAtUtc == default || conversation.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            conversation.UpdatedAtUtc < conversation.CreatedAtUtc)
            throw new InvalidDataException("A conversa vinculada à matéria é inválida.");
    }

    private static void ValidateIndex(AiTeacherSubjectMemoryConversation item)
    {
        if (item is null || item.ConversationId == Guid.Empty ||
            item.CreatedAtUtc == default || item.CreatedAtUtc.Offset != TimeSpan.Zero ||
            item.UpdatedAtUtc == default || item.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            item.UpdatedAtUtc < item.CreatedAtUtc ||
            item.SourceExchangeCount < 0 || item.SourceExchangeCount > AiTeacherConversationStore.MaximumExchangesPerConversation ||
            item.CompletedExchangeCount < 0 || item.UnansweredExchangeCount < 0 ||
            item.CompletedExchangeCount + item.UnansweredExchangeCount != item.SourceExchangeCount ||
            !AiTeacherSubjectBindingFactory.IsSemanticVersion(item.PackageVersion) ||
            !LearningCatalogIds.IsValid(item.ContentId))
            throw new InvalidDataException("A memória por matéria contém um índice de conversa inválido.");
        AiTeacherSubjectBindingFactory.ValidateText(item.SubjectName, 160, "nome da matéria");
        AiTeacherSubjectBindingFactory.ValidateText(item.ContentTitle, 240, "título do conteúdo");
    }

    private static void ValidateLesson(AiTeacherSubjectMemoryLesson lesson)
    {
        if (lesson is null || lesson.ConversationId == Guid.Empty ||
            lesson.CreatedAtUtc == default || lesson.CreatedAtUtc.Offset != TimeSpan.Zero ||
            lesson.UpdatedAtUtc == default || lesson.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            lesson.UpdatedAtUtc < lesson.CreatedAtUtc ||
            !LearningCatalogIds.IsValid(lesson.ContentId) || lesson.RecentExchanges is null ||
            lesson.RecentExchanges.Count > MaximumRecentExchangesPerLesson)
            throw new InvalidDataException("A memória por matéria contém uma aula detalhada inválida.");
        AiTeacherSubjectBindingFactory.ValidateText(lesson.ContentTitle, 240, "título do conteúdo");
        foreach (var exchange in lesson.RecentExchanges)
        {
            if (exchange is null || exchange.ExchangeId == Guid.Empty ||
                exchange.UpdatedAtUtc == default || exchange.UpdatedAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("A memória por matéria contém uma troca inválida.");
        }
    }
}
