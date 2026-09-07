using System.Collections.ObjectModel;

namespace Rota.Desktop;

/// <summary>
/// Catálogo local e versionado que descreve o que o Rota pode ensinar.
/// O conteúdo textual detalhado virá em pacotes posteriores; esta camada mantém
/// apenas a estrutura pedagógica e as relações estáveis entre os itens.
/// </summary>
public sealed class LearningCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<LearningSubject> Subjects { get; set; } = new();
    public List<LearningCourse> Courses { get; set; } = new();
    public List<LearningModule> Modules { get; set; } = new();
    public List<LearningLesson> Lessons { get; set; } = new();
    public List<LearningContent> Contents { get; set; } = new();
    public List<LearningSkill> Skills { get; set; } = new();
    public List<LearningPrerequisite> Prerequisites { get; set; } = new();

    public LearningCatalog Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        Subjects = Subjects.Select(item => item.Copy()).ToList(),
        Courses = Courses.Select(item => item.Copy()).ToList(),
        Modules = Modules.Select(item => item.Copy()).ToList(),
        Lessons = Lessons.Select(item => item.Copy()).ToList(),
        Contents = Contents.Select(item => item.Copy()).ToList(),
        Skills = Skills.Select(item => item.Copy()).ToList(),
        Prerequisites = Prerequisites.Select(item => item.Copy()).ToList()
    };
}

public sealed class LearningSubject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public LearningSubject Copy() => new() { Id = Id, Name = Name, Description = Description };
}

public sealed class LearningCourse
{
    public string Id { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public int Position { get; set; }

    public LearningCourse Copy() => new()
    {
        Id = Id, SubjectId = SubjectId, Name = Name, Level = Level, Position = Position
    };
}

public sealed class LearningModule
{
    public string Id { get; set; } = "";
    public string CourseId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Position { get; set; }

    public LearningModule Copy() => new() { Id = Id, CourseId = CourseId, Name = Name, Position = Position };
}

public sealed class LearningLesson
{
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public List<string> SkillIds { get; set; } = new();

    public LearningLesson Copy() => new()
    {
        Id = Id,
        ModuleId = ModuleId,
        Name = Name,
        Position = Position,
        SkillIds = SkillIds.ToList()
    };
}

public sealed class LearningContent
{
    public string Id { get; set; } = "";
    public string LessonId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public int Position { get; set; }

    public LearningContent Copy() => new()
    {
        Id = Id, LessonId = LessonId, Title = Title, Summary = Summary, Position = Position
    };
}

public sealed class LearningSkill
{
    public string Id { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public LearningSkill Copy() => new()
    {
        Id = Id, SubjectId = SubjectId, Name = Name, Description = Description
    };
}

public sealed class LearningPrerequisite
{
    public string ContentId { get; set; } = "";
    public string RequiredContentId { get; set; } = "";

    public LearningPrerequisite Copy() => new() { ContentId = ContentId, RequiredContentId = RequiredContentId };
}

public static class LearningCatalogIds
{
    public static bool IsValid(string? value) => value is { Length: > 0 and <= 80 } &&
        value[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}

public static class LearningCatalogValidator
{
    private const int MaximumEntityCount = 10_000;

    public static void Validate(LearningCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != LearningCatalog.CurrentSchemaVersion)
            throw new InvalidDataException($"Versão de catálogo educacional não suportada: {catalog.SchemaVersion}.");
        if (catalog.Subjects is null || catalog.Courses is null || catalog.Modules is null || catalog.Lessons is null ||
            catalog.Contents is null || catalog.Skills is null || catalog.Prerequisites is null)
            throw new InvalidDataException("O catálogo educacional está incompleto.");

        var total = catalog.Subjects.Count + catalog.Courses.Count + catalog.Modules.Count + catalog.Lessons.Count +
            catalog.Contents.Count + catalog.Skills.Count + catalog.Prerequisites.Count;
        if (total > MaximumEntityCount)
            throw new InvalidDataException("O catálogo educacional excede o limite seguro de itens.");

        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        ValidateSubjects(catalog.Subjects, entityIds);
        var subjects = catalog.Subjects.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        ValidateCourses(catalog.Courses, entityIds, subjects);
        var courses = catalog.Courses.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        ValidateModules(catalog.Modules, entityIds, courses);
        var modules = catalog.Modules.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var skills = ValidateSkills(catalog.Skills, entityIds, subjects);
        ValidateLessons(catalog.Lessons, entityIds, modules, skills);
        var lessons = catalog.Lessons.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var contents = ValidateContents(catalog.Contents, entityIds, lessons);
        ValidatePrerequisites(catalog.Prerequisites, contents);
    }

    private static void ValidateSubjects(IEnumerable<LearningSubject> items, HashSet<string> ids)
    {
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("O catálogo contém uma matéria nula.");
            ValidateEntity(item.Id, item.Name, "matéria", ids);
            ValidateText(item.Description, "descrição da matéria", 500, allowEmpty: true);
        }
    }

    private static void ValidateCourses(IEnumerable<LearningCourse> items, HashSet<string> ids, HashSet<string> subjects)
    {
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("O catálogo contém um curso nulo.");
            ValidateEntity(item.Id, item.Name, "curso", ids);
            RequireReference(item.SubjectId, subjects, "matéria do curso");
            ValidateText(item.Level, "nível do curso", 80, allowEmpty: true);
            ValidatePosition(item.Position, "curso");
        }
    }

    private static void ValidateModules(IEnumerable<LearningModule> items, HashSet<string> ids, HashSet<string> courses)
    {
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("O catálogo contém um módulo nulo.");
            ValidateEntity(item.Id, item.Name, "módulo", ids);
            RequireReference(item.CourseId, courses, "curso do módulo");
            ValidatePosition(item.Position, "módulo");
        }
    }

    private static HashSet<string> ValidateSkills(IEnumerable<LearningSkill> items, HashSet<string> ids, HashSet<string> subjects)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("O catálogo contém uma habilidade nula.");
            ValidateEntity(item.Id, item.Name, "habilidade", ids);
            RequireReference(item.SubjectId, subjects, "matéria da habilidade");
            ValidateText(item.Description, "descrição da habilidade", 500, allowEmpty: true);
            result.Add(item.Id);
        }
        return result;
    }

    private static void ValidateLessons(IEnumerable<LearningLesson> items, HashSet<string> ids, HashSet<string> modules, HashSet<string> skills)
    {
        foreach (var item in items)
        {
            if (item is null || item.SkillIds is null) throw new InvalidDataException("O catálogo contém uma aula incompleta.");
            ValidateEntity(item.Id, item.Name, "aula", ids);
            RequireReference(item.ModuleId, modules, "módulo da aula");
            ValidatePosition(item.Position, "aula");
            if (item.SkillIds.Count > 30 || item.SkillIds.Distinct(StringComparer.Ordinal).Count() != item.SkillIds.Count)
                throw new InvalidDataException("A aula possui habilidades duplicadas ou demais.");
            foreach (var skillId in item.SkillIds)
                RequireReference(skillId, skills, "habilidade da aula");
        }
    }

    private static HashSet<string> ValidateContents(IEnumerable<LearningContent> items, HashSet<string> ids, HashSet<string> lessons)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("O catálogo contém um conteúdo nulo.");
            ValidateEntity(item.Id, item.Title, "conteúdo", ids);
            RequireReference(item.LessonId, lessons, "aula do conteúdo");
            ValidateText(item.Summary, "resumo do conteúdo", 2_000, allowEmpty: true);
            ValidatePosition(item.Position, "conteúdo");
            result.Add(item.Id);
        }
        return result;
    }

    private static void ValidatePrerequisites(IEnumerable<LearningPrerequisite> items, HashSet<string> contents)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pairs = new HashSet<(string Content, string Required)>();
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("O catálogo contém um pré-requisito nulo.");
            RequireReference(item.ContentId, contents, "conteúdo do pré-requisito");
            RequireReference(item.RequiredContentId, contents, "conteúdo obrigatório");
            if (item.ContentId == item.RequiredContentId || !pairs.Add((item.ContentId, item.RequiredContentId)))
                throw new InvalidDataException("O catálogo contém um pré-requisito duplicado ou circular.");
            if (!edges.TryGetValue(item.ContentId, out var required)) edges[item.ContentId] = required = new();
            required.Add(item.RequiredContentId);
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool HasCycle(string id)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;
            foreach (var required in edges.GetValueOrDefault(id) ?? new List<string>())
                if (HasCycle(required)) return true;
            visiting.Remove(id);
            visited.Add(id);
            return false;
        }

        if (contents.Any(HasCycle))
            throw new InvalidDataException("O catálogo contém um ciclo de pré-requisitos.");
    }

    private static void ValidateEntity(string? id, string? name, string kind, HashSet<string> ids)
    {
        if (!LearningCatalogIds.IsValid(id) || !ids.Add(id!))
            throw new InvalidDataException($"O identificador da {kind} é inválido ou duplicado.");
        ValidateText(name, $"nome da {kind}", 160, allowEmpty: false);
    }

    private static void RequireReference(string? id, HashSet<string> known, string field)
    {
        if (!LearningCatalogIds.IsValid(id) || !known.Contains(id!))
            throw new InvalidDataException($"A referência para {field} é inválida.");
    }

    private static void ValidatePosition(int position, string kind)
    {
        if (position is < 1 or > 10_000)
            throw new InvalidDataException($"A posição do {kind} é inválida.");
    }

    private static void ValidateText(string? value, string field, int maximum, bool allowEmpty)
    {
        if (value is null || value.Length > maximum || value.Any(char.IsControl) ||
            (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException($"O campo {field} é inválido.");
    }
}
