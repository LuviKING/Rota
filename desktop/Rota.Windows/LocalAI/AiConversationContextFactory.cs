namespace Rota.Desktop.LocalAI;

public static class AiConversationContextFactory
{
    private const int MaximumExchanges = 6;
    private const int MaximumContextTextLength = 500;

    public static AiConversationContext Create(AiConversationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ConversationId == Guid.Empty)
            throw new AiContractValidationException("A conversa local não possui um ID válido.");

        var completed = snapshot.Turns
            .GroupBy(turn => turn.RequestId)
            .Select(group => new
            {
                User = group.SingleOrDefault(turn => turn.Role == AiConversationRole.User &&
                    turn.Status == AiConversationTurnStatus.Completed),
                Assistant = group.SingleOrDefault(turn => turn.Role == AiConversationRole.Assistant &&
                    turn.Status == AiConversationTurnStatus.Completed)
            })
            .Where(exchange => exchange.User is not null && exchange.Assistant is not null)
            .OrderBy(exchange => exchange.User!.CreatedAtUtc)
            .TakeLast(MaximumExchanges)
            .ToList();

        var context = new AiConversationContext
        {
            ConversationId = snapshot.ConversationId,
            Turns = completed.SelectMany(exchange => new[]
            {
                new AiConversationContextTurn
                {
                    Role = AiConversationRole.User,
                    Text = Limit(exchange.User!.Text)
                },
                new AiConversationContextTurn
                {
                    Role = AiConversationRole.Assistant,
                    Text = Limit(exchange.Assistant!.Text)
                }
            }).ToList()
        };
        AiContractValidator.ValidateConversationContext(context);
        return context;
    }

    public static string UserText(AiAssistantInput input)
    {
        AiContractValidator.ValidateInput(input);
        if (!string.IsNullOrWhiteSpace(input.FreeText)) return input.FreeText.Trim();

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.ObjectiveOrExam)) parts.Add("Objetivo: " + input.ObjectiveOrExam.Trim());
        if (!string.IsNullOrWhiteSpace(input.ExamDate)) parts.Add("Data: " + input.ExamDate);
        if (input.AvailableHoursPerDay is { } hours) parts.Add($"Disponibilidade diária: {hours:0.##} h");
        if (input.AvailableDays.Count > 0) parts.Add("Dias: " + string.Join(", ", input.AvailableDays));
        if (input.StrongSubjects.Count > 0) parts.Add("Pontos fortes: " + string.Join(", ", input.StrongSubjects));
        if (input.WeakSubjects.Count > 0) parts.Add("Dificuldades: " + string.Join(", ", input.WeakSubjects));
        if (!string.IsNullOrWhiteSpace(input.Goal)) parts.Add("Meta: " + input.Goal.Trim());
        if (!string.IsNullOrWhiteSpace(input.Notes)) parts.Add("Observações: " + input.Notes.Trim());
        return string.Join(Environment.NewLine, parts);
    }

    private static string Limit(string value)
    {
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaximumContextTextLength
            ? normalized
            : normalized[..(MaximumContextTextLength - 1)].TrimEnd() + "…";
    }
}
