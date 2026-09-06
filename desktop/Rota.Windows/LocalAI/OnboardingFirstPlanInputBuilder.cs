namespace Rota.Desktop.LocalAI;

public static class OnboardingFirstPlanInputBuilder
{
    public const int MaximumDifficultiesLength = 1_000;
    public const int MaximumNotesLength = 1_700;

    public static AiAssistantInput Build(
        AppSettings settings,
        string? difficulties,
        bool difficultiesUnknown,
        string? notes)
    {
        ArgumentNullException.ThrowIfNull(settings);
        difficulties ??= "";
        notes ??= "";
        if (!difficultiesUnknown && difficulties.Length > MaximumDifficultiesLength)
            throw new AiContractValidationException(
                $"As dificuldades devem ter no máximo {MaximumDifficultiesLength:N0} caracteres.");
        if (notes.Length > MaximumNotesLength)
            throw new AiContractValidationException(
                $"As observações devem ter no máximo {MaximumNotesLength:N0} caracteres.");

        var weakSubjects = difficultiesUnknown
            ? new List<string>()
            : ParseDifficulties(difficulties);
        if (!difficultiesUnknown && weakSubjects.Count == 0)
            throw new AiContractValidationException(
                "Informe ao menos uma dificuldade ou marque que ainda não sabe.");

        var cleanNotes = notes.Trim();
        if (difficultiesUnknown)
        {
            const string unknownNote = "Ainda não identifiquei minhas principais dificuldades.";
            cleanNotes = cleanNotes.Length == 0
                ? unknownNote
                : unknownNote + Environment.NewLine + cleanNotes;
        }

        var input = new AiAssistantInput
        {
            ObjectiveOrExam = settings.ObjectiveName,
            ExamDate = settings.ObjectiveDate,
            AvailableHoursPerDay = settings.DailyHours,
            AvailableDays = settings.AvailableStudyDays.ToList(),
            WeakSubjects = weakSubjects,
            Goal = "Crie meu primeiro plano de estudos até o prazo, distribuindo a carga somente nos dias disponíveis.",
            Notes = cleanNotes
        };
        AiContractValidator.ValidateInput(input);
        return input;
    }

    private static List<string> ParseDifficulties(string value) => value
        .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(part => string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
        .Where(part => part.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
