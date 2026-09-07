namespace Rota.Desktop;

/// <summary>
/// Roteiro declarativo de um simulado. A nota é calculada localmente pelo Rota
/// a partir das questões do próprio pacote, sem delegar o gabarito à IA.
/// </summary>
public sealed class LearningAssessment
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public List<string> QuestionIds { get; set; } = new();
    public int TimeLimitMinutes { get; set; }
    public int PassingScorePercentage { get; set; }

    public LearningAssessment Copy() => new()
    {
        Id = Id,
        Title = Title,
        Instructions = Instructions,
        QuestionIds = QuestionIds.ToList(),
        TimeLimitMinutes = TimeLimitMinutes,
        PassingScorePercentage = PassingScorePercentage
    };
}

public sealed class LearningAssessmentResult
{
    public string AssessmentId { get; init; } = "";
    public int CorrectAnswers { get; init; }
    public int TotalQuestions { get; init; }
    public int ScorePercentage { get; init; }
    public bool Passed { get; init; }
    public IReadOnlyList<string> IncorrectQuestionIds { get; init; } = Array.Empty<string>();
}

public static class LearningAssessmentValidator
{
    public const int MaximumAssessments = 2_000;

    public static void Validate(IReadOnlyList<LearningAssessment>? assessments, IReadOnlyList<LearningPracticeQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(questions);
        if (assessments.Count > MaximumAssessments)
            throw new InvalidDataException("O pacote possui simulados demais.");

        var knownQuestionIds = questions.Select(question => question.Id).ToHashSet(StringComparer.Ordinal);
        var assessmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assessment in assessments)
        {
            if (assessment is null || assessment.QuestionIds is null)
                throw new InvalidDataException("O pacote contém um simulado incompleto.");
            if (!LearningCatalogIds.IsValid(assessment.Id) || !assessmentIds.Add(assessment.Id))
                throw new InvalidDataException("O identificador do simulado é inválido ou duplicado.");
            ValidateText(assessment.Title, "título do simulado", 160, allowEmpty: false);
            ValidateText(assessment.Instructions, "instruções do simulado", 2_000, allowEmpty: false);
            if (assessment.TimeLimitMinutes is < 5 or > 480)
                throw new InvalidDataException("O tempo do simulado precisa estar entre 5 e 480 minutos.");
            if (assessment.PassingScorePercentage is < 0 or > 100)
                throw new InvalidDataException("A nota mínima do simulado é inválida.");
            if (assessment.QuestionIds.Count is < 1 or > 200 || assessment.QuestionIds.Distinct(StringComparer.Ordinal).Count() != assessment.QuestionIds.Count)
                throw new InvalidDataException("O simulado precisa ter questões únicas dentro do limite seguro.");
            foreach (var questionId in assessment.QuestionIds)
                if (!LearningCatalogIds.IsValid(questionId) || !knownQuestionIds.Contains(questionId))
                    throw new InvalidDataException("O simulado referencia uma questão inexistente.");
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

public static class LearningAssessmentGrader
{
    public static LearningAssessmentResult Grade(
        LearningAssessment assessment,
        IReadOnlyList<LearningPracticeQuestion> questions,
        IReadOnlyDictionary<string, string>? answers)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(questions);
        LearningAssessmentValidator.Validate(new[] { assessment }, questions);
        answers ??= new Dictionary<string, string>(StringComparer.Ordinal);

        var questionById = questions.ToDictionary(question => question.Id, StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (!assessment.QuestionIds.Contains(answer.Key, StringComparer.Ordinal))
                throw new InvalidDataException("A resposta enviada não pertence a este simulado.");
            var question = questionById[answer.Key];
            if (!LearningCatalogIds.IsValid(answer.Value) || !question.Options.Any(option => option.Id == answer.Value))
                throw new InvalidDataException("A resposta enviada não corresponde a uma alternativa da questão.");
        }

        var incorrect = new List<string>();
        var correct = 0;
        foreach (var questionId in assessment.QuestionIds)
        {
            var question = questionById[questionId];
            if (answers.TryGetValue(questionId, out var optionId) && optionId == question.CorrectOptionId)
                correct++;
            else
                incorrect.Add(questionId);
        }
        var score = (int)Math.Round(correct * 100d / assessment.QuestionIds.Count, MidpointRounding.AwayFromZero);
        return new LearningAssessmentResult
        {
            AssessmentId = assessment.Id,
            CorrectAnswers = correct,
            TotalQuestions = assessment.QuestionIds.Count,
            ScorePercentage = score,
            Passed = score >= assessment.PassingScorePercentage,
            IncorrectQuestionIds = incorrect
        };
    }
}
