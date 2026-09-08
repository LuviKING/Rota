namespace Rota.Desktop.LocalAI;

/// <summary>
/// Converte material pedagógico verificado em um recorte mínimo para a professora
/// local. A seleção é determinística e nunca transporta questões ou gabaritos.
/// </summary>
public static class AiTeacherLessonContextFactory
{
    private const int MaximumTheoryCharacters = 12_000;
    public const int MaximumPrerequisiteDepth = 3;

    /// <summary>
    /// Converte um guia já composto. Neste caminho só existem os pré-requisitos que
    /// o próprio guia recebeu; ele é mantido para consumidores que já fizeram a
    /// seleção pedagógica antes de chamar a professora.
    /// </summary>
    public static AiTeacherLessonContext Create(LearningStudyGuide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(guide.Content);
        ArgumentNullException.ThrowIfNull(guide.Prerequisites);
        ArgumentNullException.ThrowIfNull(guide.PracticeQuestions);

        var prerequisites = guide.Prerequisites
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(AiTeacherContractValidator.MaximumPrerequisites)
            .Select(ToPrerequisiteContext)
            .ToList();

        return CreateCore(
            guide,
            prerequisites,
            prerequisites.Count < guide.Prerequisites.Count);
    }

    /// <summary>
    /// Caminho canônico quando o pacote verificado está disponível. Além dos
    /// pré-requisitos diretos, inclui uma cadeia transitiva curta e limitada para
    /// que a professora possa reconhecer uma base anterior realmente cadastrada no
    /// pacote. Conteúdo fora desse grafo nunca vira candidato para diagnóstico.
    /// </summary>
    public static AiTeacherLessonContext Create(LearningContentPackage package, string contentId)
    {
        ArgumentNullException.ThrowIfNull(package);
        LearningContentPackageFormat.Validate(package);
        if (!LearningCatalogIds.IsValid(contentId))
            throw new AiContractValidationException("O conteúdo solicitado para a professora local é inválido.");

        var guide = LearningStudyGuideService.Create(package, contentId);
        var resolution = ResolvePrerequisites(package.Catalog, contentId);
        return CreateCore(guide, resolution.Items, resolution.HasMore);
    }

    private static AiTeacherLessonContext CreateCore(
        LearningStudyGuide guide,
        List<AiTeacherPrerequisiteContext> prerequisites,
        bool hasMorePrerequisites)
    {
        AiTeacherTheoryContext? theory = null;
        if (guide.TheoryMaterial is { } material)
        {
            var selected = new List<AiTeacherTheorySectionContext>();
            var usedCharacters = 0;
            var orderedSections = material.Sections
                .OrderBy(section => section.Position)
                .ThenBy(section => section.Title, StringComparer.Ordinal)
                .ToArray();

            foreach (var section in orderedSections)
            {
                if (selected.Count >= AiTeacherContractValidator.MaximumTheorySections)
                    break;
                var cost = section.Kind.Length + section.Title.Length + section.Body.Length;
                if (usedCharacters + cost > MaximumTheoryCharacters)
                    break;
                selected.Add(new AiTeacherTheorySectionContext
                {
                    Kind = section.Kind,
                    Title = section.Title,
                    Body = section.Body,
                    Position = section.Position
                });
                usedCharacters += cost;
            }

            if (selected.Count == 0)
                throw new AiContractValidationException("O material teórico não cabe no contexto seguro da professora local.");

            theory = new AiTeacherTheoryContext
            {
                MaterialId = material.Id,
                Title = material.Title,
                LearningGoal = material.LearningGoal,
                Sections = selected,
                HasMoreSections = selected.Count < orderedSections.Length
            };
        }

        var context = new AiTeacherLessonContext
        {
            ContentId = guide.Content.Id,
            ContentTitle = guide.Content.Title,
            ContentSummary = guide.Content.Summary,
            Prerequisites = prerequisites,
            HasMorePrerequisites = hasMorePrerequisites,
            Theory = theory
        };
        AiTeacherContractValidator.ValidateLessonContext(context);
        return context;
    }

    private static PrerequisiteResolution ResolvePrerequisites(LearningCatalog catalog, string contentId)
    {
        var contents = catalog.Contents.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (!contents.ContainsKey(contentId))
            throw new AiContractValidationException("O conteúdo solicitado não pertence ao pacote pedagógico verificado.");

        var edges = catalog.Prerequisites
            .GroupBy(item => item.ContentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.RequiredContentId)
                    .OrderBy(id => contents[id].Position)
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var queue = new Queue<(string ContentId, int Depth)>();
        foreach (var requiredId in edges.GetValueOrDefault(contentId) ?? Array.Empty<string>())
            queue.Enqueue((requiredId, 1));

        var seen = new HashSet<string>(StringComparer.Ordinal) { contentId };
        var selected = new List<AiTeacherPrerequisiteContext>();
        var hasMore = false;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current.ContentId))
                continue;

            if (current.Depth > MaximumPrerequisiteDepth)
            {
                hasMore = true;
                continue;
            }

            if (selected.Count >= AiTeacherContractValidator.MaximumPrerequisites)
            {
                hasMore = true;
                continue;
            }

            selected.Add(ToPrerequisiteContext(contents[current.ContentId]));

            var children = edges.GetValueOrDefault(current.ContentId) ?? Array.Empty<string>();
            foreach (var child in children)
            {
                if (current.Depth >= MaximumPrerequisiteDepth)
                {
                    if (!seen.Contains(child))
                        hasMore = true;
                    continue;
                }
                queue.Enqueue((child, current.Depth + 1));
            }
        }

        if (queue.Count > 0)
            hasMore = true;

        return new PrerequisiteResolution(selected, hasMore);
    }

    private static AiTeacherPrerequisiteContext ToPrerequisiteContext(LearningContent item) => new()
    {
        ContentId = item.Id,
        Title = item.Title
    };

    private sealed record PrerequisiteResolution(
        List<AiTeacherPrerequisiteContext> Items,
        bool HasMore);
}

