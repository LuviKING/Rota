namespace Rota.Desktop;

/// <summary>
/// Questão objetiva local. A resposta e a explicação ficam no pacote para que
/// o Rota possa corrigir e ensinar sem pedir à IA para inventar um gabarito.
/// </summary>
public sealed class LearningPracticeQuestion
{
    public string Id { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public List<LearningPracticeOption> Options { get; set; } = new();
    public string CorrectOptionId { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string Difficulty { get; set; } = "";

    public LearningPracticeQuestion Copy() => new()
    {
        Id = Id,
        ContentId = ContentId,
        Prompt = Prompt,
        Options = Options.Select(option => option.Copy()).ToList(),
        CorrectOptionId = CorrectOptionId,
        Explanation = Explanation,
        Difficulty = Difficulty
    };
}

public sealed class LearningPracticeOption
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";

    public LearningPracticeOption Copy() => new() { Id = Id, Text = Text };
}

public static class LearningPracticeDifficulties
{
    public const string Introductory = "introductory";
    public const string Basic = "basic";
    public const string Intermediate = "intermediate";
    public const string Advanced = "advanced";

    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        Introductory, Basic, Intermediate, Advanced
    };

    public static bool IsValid(string? value) => value is not null && Values.Contains(value);
}

public static class LearningPracticeQuestionValidator
{
    public const int MaximumQuestions = 20_000;

    public static void Validate(IReadOnlyList<LearningPracticeQuestion>? questions, LearningCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(catalog);
        if (questions.Count > MaximumQuestions)
            throw new InvalidDataException("O pacote possui questões práticas demais.");

        var knownContents = catalog.Contents.Select(content => content.Id).ToHashSet(StringComparer.Ordinal);
        var questionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            if (question is null || question.Options is null)
                throw new InvalidDataException("O pacote contém uma questão prática incompleta.");
            if (!LearningCatalogIds.IsValid(question.Id) || !questionIds.Add(question.Id))
                throw new InvalidDataException("O identificador da questão prática é inválido ou duplicado.");
            if (!LearningCatalogIds.IsValid(question.ContentId) || !knownContents.Contains(question.ContentId))
                throw new InvalidDataException("A questão prática precisa referenciar um conteúdo existente.");
            ValidateText(question.Prompt, "enunciado da questão prática", 4_000, allowEmpty: false);
            ValidateText(question.Explanation, "explicação da questão prática", 6_000, allowEmpty: false);
            if (!LearningPracticeDifficulties.IsValid(question.Difficulty))
                throw new InvalidDataException("A dificuldade da questão prática é inválida.");
            if (question.Options.Count is < 2 or > 5)
                throw new InvalidDataException("A questão prática precisa ter entre duas e cinco alternativas.");

            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in question.Options)
            {
                if (option is null || !LearningCatalogIds.IsValid(option.Id) || !optionIds.Add(option.Id))
                    throw new InvalidDataException("O identificador de alternativa é inválido ou duplicado.");
                ValidateText(option.Text, "texto da alternativa", 1_000, allowEmpty: false);
            }
            if (!LearningCatalogIds.IsValid(question.CorrectOptionId) || !optionIds.Contains(question.CorrectOptionId))
                throw new InvalidDataException("O gabarito da questão prática não aponta para uma alternativa.");
        }
    }

    private static void ValidateText(string? value, string field, int maximum, bool allowEmpty)
    {
        if (value is null || value.Length > maximum || value.Any(char.IsControl) ||
            (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidDataException($"O campo {field} é inválido.");
        }
    }
}
