namespace Rota.Desktop.LocalAI;

/// <summary>
/// Matéria resolvida exclusivamente da hierarquia de um pacote pedagógico já
/// validado. O vínculo é metadado local e nunca entra automaticamente no prompt.
/// PackageId + SubjectId formam a identidade da memória entre versões do pacote.
/// </summary>
public sealed record AiTeacherSubjectBinding
{
    public string PackageId { get; init; } = "";
    public string PackageVersion { get; init; } = "";
    public string SubjectId { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string ContentId { get; init; } = "";
    public string ContentTitle { get; init; } = "";
}

public static class AiTeacherSubjectBindingFactory
{
    public static AiTeacherSubjectBinding Create(LearningContentPackage package, string contentId)
    {
        ArgumentNullException.ThrowIfNull(package);
        LearningContentPackageFormat.Validate(package);
        if (!LearningCatalogIds.IsValid(contentId))
            throw new AiContractValidationException("O conteúdo da memória por matéria é inválido.");

        var content = package.Catalog.Contents.SingleOrDefault(item => item.Id == contentId)
            ?? throw new AiContractValidationException("O conteúdo não pertence ao pacote pedagógico verificado.");
        var lesson = package.Catalog.Lessons.Single(item => item.Id == content.LessonId);
        var module = package.Catalog.Modules.Single(item => item.Id == lesson.ModuleId);
        var course = package.Catalog.Courses.Single(item => item.Id == module.CourseId);
        var subject = package.Catalog.Subjects.Single(item => item.Id == course.SubjectId);

        var result = new AiTeacherSubjectBinding
        {
            PackageId = package.Package.Id,
            PackageVersion = package.Package.Version,
            SubjectId = subject.Id,
            SubjectName = subject.Name,
            ContentId = content.Id,
            ContentTitle = content.Title
        };
        Validate(result);
        return result;
    }

    public static void Validate(AiTeacherSubjectBinding binding, AiTeacherLessonContext? lessonContext = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!LearningCatalogIds.IsValid(binding.PackageId) ||
            !LearningCatalogIds.IsValid(binding.SubjectId) ||
            !LearningCatalogIds.IsValid(binding.ContentId) ||
            !IsSemanticVersion(binding.PackageVersion))
            throw new AiContractValidationException("A origem da memória por matéria é inválida.");

        ValidateText(binding.SubjectName, 160, "nome da matéria");
        ValidateText(binding.ContentTitle, 240, "título do conteúdo");

        if (lessonContext is null) return;
        AiTeacherContractValidator.ValidateLessonContext(lessonContext);
        if (!string.Equals(binding.ContentId, lessonContext.ContentId, StringComparison.Ordinal) ||
            !string.Equals(binding.ContentTitle, lessonContext.ContentTitle, StringComparison.Ordinal))
        {
            throw new AiContractValidationException(
                "A matéria da memória não corresponde à aula pedagógica congelada.");
        }
    }

    public static bool SameSubject(AiTeacherSubjectBinding left, AiTeacherSubjectBinding right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(left.PackageId, right.PackageId, StringComparison.Ordinal) &&
               string.Equals(left.SubjectId, right.SubjectId, StringComparison.Ordinal);
    }

    public static bool Equivalent(AiTeacherSubjectBinding left, AiTeacherSubjectBinding right) => left == right;

    public static AiTeacherSubjectBinding Copy(AiTeacherSubjectBinding source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source with { };
    }

    internal static bool IsSemanticVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 3) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || (part.Length > 1 && part[0] == '0') ||
                !part.All(char.IsDigit) || !int.TryParse(part, out _))
                return false;
        }
        return true;
    }

    internal static void ValidateText(string? value, int maximum, string field)
    {
        if (value is null || value.Length is 0 || value.Length > maximum ||
            string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new AiContractValidationException($"O campo {field} da memória por matéria é inválido.");
    }
}

public sealed record AiTeacherSubjectMemoryConversation
{
    public Guid ConversationId { get; init; }
    public string PackageVersion { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string ContentId { get; init; } = "";
    public string ContentTitle { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public int SourceExchangeCount { get; init; }
    public int CompletedExchangeCount { get; init; }
    public int UnansweredExchangeCount { get; init; }
}

public sealed record AiTeacherSubjectMemoryLesson
{
    public Guid ConversationId { get; init; }
    public string ContentId { get; init; } = "";
    public string ContentTitle { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public bool HasEarlierCompletedExchanges { get; init; }
    public IReadOnlyList<AiTeacherLessonSummaryExchange> RecentExchanges { get; init; } =
        Array.Empty<AiTeacherLessonSummaryExchange>();
}

public sealed record AiTeacherSubjectMemorySnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string PackageId { get; init; } = "";
    public string SubjectId { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string LatestPackageVersion { get; init; } = "";
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public int ConversationCount { get; init; }
    public int CompletedExchangeCount { get; init; }
    public int UnansweredExchangeCount { get; init; }
    public bool HasMoreDetailedLessons { get; init; }
    public IReadOnlyList<AiTeacherSubjectMemoryConversation> Conversations { get; init; } =
        Array.Empty<AiTeacherSubjectMemoryConversation>();
    public IReadOnlyList<AiTeacherSubjectMemoryLesson> RecentLessons { get; init; } =
        Array.Empty<AiTeacherSubjectMemoryLesson>();
}
