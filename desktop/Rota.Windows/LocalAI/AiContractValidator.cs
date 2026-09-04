using System.Globalization;

namespace Rota.Desktop.LocalAI;

public static class AiContractValidator
{
    private const int MaxSubjectCount = 100;
    private const int MaxOperationCount = 500;
    private const int MaxPlanningSessions = 200;

    public static void ValidateConfiguration(AiConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.SchemaVersion != AiConfiguration.CurrentSchemaVersion)
            throw new AiContractValidationException($"Versão de configuração de IA não suportada: {configuration.SchemaVersion}.");
        ValidateEnum(configuration.Profile, "perfil de IA");
        ValidateEnum(configuration.ComputePreference, "preferência de processamento");
        ValidateEnum(configuration.InstallationState, "estado de instalação");
        ValidateText(configuration.ModelId, "Modelo selecionado", 160, allowEmpty: true);
        ValidatePath(configuration.ModelPath, "Caminho do modelo");
        ValidatePath(configuration.RuntimePath, "Caminho do runtime");

        if (configuration.ContextSize is < 512 or > 131_072)
            throw new AiContractValidationException("O contexto da IA deve ficar entre 512 e 131072 tokens.");

        if (configuration.InstallationState is AiInstallationState.RuntimeInstalled or AiInstallationState.Ready &&
            string.IsNullOrWhiteSpace(configuration.RuntimePath))
        {
            throw new AiContractValidationException("O runtime precisa de um caminho absoluto quando estiver instalado.");
        }

        if (configuration.InstallationState == AiInstallationState.Ready &&
            (string.IsNullOrWhiteSpace(configuration.ModelId) || string.IsNullOrWhiteSpace(configuration.ModelPath)))
        {
            throw new AiContractValidationException("Uma configuração pronta precisa identificar o modelo e seu caminho absoluto.");
        }
    }

    public static void ValidateInput(AiAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ValidateText(input.ObjectiveOrExam, "Objetivo ou prova", 160, allowEmpty: true);
        ValidateText(input.ExamDate, "Data da prova", 10, allowEmpty: true);
        ValidateText(input.Goal, "Meta", 500, allowEmpty: true, allowLineBreaks: true);
        ValidateText(input.Notes, "Observações", 2_000, allowEmpty: true, allowLineBreaks: true);
        ValidateText(input.FreeText, "Texto livre", 8_000, allowEmpty: true, allowLineBreaks: true);
        ValidateSubjects(input.StrongSubjects, "Matérias fortes");
        ValidateSubjects(input.WeakSubjects, "Matérias fracas");
        ValidateDays(input.AvailableDays, "Dias disponíveis");

        if (input.ExamDate.Length > 0 &&
            !DateOnly.TryParseExact(input.ExamDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new AiContractValidationException("A data da prova deve ser uma data válida no formato AAAA-MM-DD.");
        }

        if (input.AvailableHoursPerDay is { } hours && (!double.IsFinite(hours) || hours <= 0 || hours > 24))
            throw new AiContractValidationException("As horas disponíveis por dia devem ser maiores que zero e no máximo 24.");

        var hasContent = !string.IsNullOrWhiteSpace(input.ObjectiveOrExam) ||
            !string.IsNullOrWhiteSpace(input.ExamDate) ||
            input.AvailableHoursPerDay.HasValue ||
            input.AvailableDays.Count > 0 ||
            input.StrongSubjects.Count > 0 ||
            input.WeakSubjects.Count > 0 ||
            !string.IsNullOrWhiteSpace(input.Goal) ||
            !string.IsNullOrWhiteSpace(input.Notes) ||
            !string.IsNullOrWhiteSpace(input.FreeText);

        if (!hasContent)
            throw new AiContractValidationException("Informe ao menos um objetivo, disponibilidade ou texto para o assistente.");
    }

    public static void ValidateProposal(AiProposal proposal, AiProposalKind? expectedKind = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        if (proposal.Id == Guid.Empty)
            throw new AiContractValidationException("A proposta da IA precisa de um ID válido.");
        if (proposal.CreatedAtUtc == default || proposal.CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new AiContractValidationException("A proposta da IA precisa de um timestamp UTC válido.");
        ValidateText(proposal.Summary, "Resumo da proposta", 1_000, allowEmpty: false, allowLineBreaks: true);
        ValidateEnum(proposal.Kind, "tipo de proposta");
        ValidateEnum(proposal.Status, "estado da proposta");

        if (expectedKind.HasValue && proposal.Kind != expectedKind.Value)
            throw new AiContractValidationException("O backend retornou um tipo de proposta diferente do solicitado.");

        if (proposal.Warnings is null || proposal.Warnings.Count > 50)
            throw new AiContractValidationException("A lista de avisos da proposta é inválida.");
        foreach (var warning in proposal.Warnings)
            ValidateText(warning, "Aviso da proposta", 1_000, allowEmpty: false, allowLineBreaks: true);

        switch (proposal.Kind)
        {
            case AiProposalKind.StudyPlan:
                if (proposal.StudyPlan is null || proposal.Changes is not null)
                    throw new AiContractValidationException("Uma proposta de criação precisa conter somente um rascunho de StudyPlan.");
                ValidateStudyPlanDraft(proposal.StudyPlan);
                break;
            case AiProposalKind.PlanChanges:
                if (proposal.Changes is null || proposal.StudyPlan is not null)
                    throw new AiContractValidationException("Uma proposta de alteração precisa conter somente operações estruturadas.");
                ValidateChangeDraft(proposal.Changes);
                break;
            default:
                throw new AiContractValidationException("O tipo de proposta da IA é inválido.");
        }
    }

    public static void ValidatePlanningContext(AiPlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SchemaVersion != AiPlanningContext.CurrentSchemaVersion)
            throw new AiContractValidationException($"Versão de contexto da IA não suportada: {context.SchemaVersion}.");
        ValidateText(context.SnapshotDate, "Data do contexto", 10, allowEmpty: false);
        if (!DateOnly.TryParseExact(context.SnapshotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new AiContractValidationException("A data do contexto da IA é inválida.");
        ValidateText(context.ObjectiveName, "Objetivo do contexto", 120, allowEmpty: false);
        ValidateText(context.ObjectiveDate, "Data do objetivo do contexto", 10, allowEmpty: true);
        if (context.ObjectiveDate.Length > 0 &&
            !DateOnly.TryParseExact(context.ObjectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new AiContractValidationException("A data do objetivo no contexto da IA é inválida.");
        }
        ValidateText(context.ActivePlanId, "ID do plano ativo", 80, allowEmpty: true);
        ValidateText(context.ActivePlanTitle, "Título do plano ativo", 120, allowEmpty: true);
        if (context.ActivePlanRevision < 0 ||
            (context.ActivePlanId.Length == 0 && context.ActivePlanRevision != 0) ||
            (context.ActivePlanId.Length > 0 && context.ActivePlanRevision < 1))
        {
            throw new AiContractValidationException("A revisão do plano ativo no contexto da IA é inválida.");
        }
        if (context.DailyMinutesLimit is < 60 or > 720 || context.BlockMinutes is < 30 or > 180)
            throw new AiContractValidationException("Os limites de tempo do contexto da IA são inválidos.");
        if (context.FutureSessions is null || context.FutureSessions.Count > MaxPlanningSessions)
            throw new AiContractValidationException("O contexto da IA contém sessões futuras demais ou ausentes.");

        var identities = new HashSet<(string PlanId, string SessionId)>();
        foreach (var session in context.FutureSessions)
        {
            if (session is null)
                throw new AiContractValidationException("O contexto da IA contém uma sessão nula.");
            ValidateText(session.SessionId, "ID da sessão do contexto", 100, allowEmpty: false);
            ValidateText(session.PlanId, "ID do plano da sessão do contexto", 80, allowEmpty: false);
            if (!identities.Add((session.PlanId, session.SessionId)))
                throw new AiContractValidationException("O contexto da IA contém identidades de sessão duplicadas.");
            if (session.PlanRevision < 1)
                throw new AiContractValidationException("O contexto da IA contém uma revisão de sessão inválida.");
            ValidateText(session.Date, "Data da sessão do contexto", 10, allowEmpty: false);
            if (!DateOnly.TryParseExact(session.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                string.CompareOrdinal(session.Date, context.SnapshotDate) < 0)
            {
                throw new AiContractValidationException("O contexto da IA contém uma sessão que não é futura.");
            }
            ValidateText(session.Subject, "Matéria da sessão do contexto", 80, allowEmpty: false);
            ValidateText(session.Topic, "Tópico da sessão do contexto", 160, allowEmpty: false);
            if (session.Minutes is < 10 or > 360)
                throw new AiContractValidationException("O contexto da IA contém uma duração inválida.");
            if (session.Kind is not "study" and not "review" and not "assessment")
                throw new AiContractValidationException("O contexto da IA contém um tipo de sessão inválido.");
            if (session.Origin is not "plan" and not "runtime" ||
                session.ProtectedFromDirectRemoval != (session.Origin == "runtime"))
            {
                throw new AiContractValidationException("O contexto da IA contém uma origem de sessão inválida.");
            }
        }
    }

    private static void ValidateStudyPlanDraft(AiStudyPlanDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.StudyPlanJson))
            throw new AiContractValidationException("O rascunho de StudyPlan está vazio.");
        if (draft.StudyPlanJson.Length > 2_000_000)
            throw new AiContractValidationException("O rascunho de StudyPlan excede o limite seguro.");

        try
        {
            StudyPlanImporter.Parse(draft.StudyPlanJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            throw new AiContractValidationException("O rascunho gerado não é um StudyPlan Code 0.2 válido.", ex);
        }
    }

    private static void ValidateChangeDraft(AiPlanChangeDraft draft)
    {
        if (draft.Operations is null || draft.Operations.Count is < 1 or > MaxOperationCount)
            throw new AiContractValidationException("A proposta de alteração precisa conter entre 1 e 500 operações.");

        var ids = new HashSet<Guid>();
        foreach (var operation in draft.Operations)
        {
            if (operation is null)
                throw new AiContractValidationException("A proposta contém uma operação nula.");
            if (operation.Id == Guid.Empty || !ids.Add(operation.Id))
                throw new AiContractValidationException("Cada operação proposta precisa de um ID único e válido.");
            ValidateEnum(operation.Type, "tipo de operação");
            ValidateText(operation.Summary, "Resumo da operação", 500, allowEmpty: false, allowLineBreaks: true);
            ValidateText(operation.SessionId, "ID da sessão", 120, allowEmpty: true);
            ValidateText(operation.Subject, "Matéria", 120, allowEmpty: true);
            ValidateText(operation.DestinationDate, "Data de destino", 10, allowEmpty: true);
            ValidateDays(operation.AvailableDays, "Dias da operação");

            if (operation.DestinationDate.Length > 0 &&
                !DateOnly.TryParseExact(operation.DestinationDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new AiContractValidationException("Uma operação contém uma data de destino inválida.");
            }
            if (operation.Minutes is <= 0 or > 1_440)
                throw new AiContractValidationException("Uma operação contém duração inválida.");
            if (operation.Priority is < 1 or > 100)
                throw new AiContractValidationException("Uma operação contém prioridade inválida.");
            if (operation.MaxHoursPerDay is { } hours && (!double.IsFinite(hours) || hours <= 0 || hours > 24))
                throw new AiContractValidationException("Uma operação contém limite diário inválido.");
        }
    }

    private static void ValidateSubjects(List<string>? subjects, string field)
    {
        if (subjects is null || subjects.Count > MaxSubjectCount)
            throw new AiContractValidationException($"{field} contém itens demais ou está ausente.");
        foreach (var subject in subjects)
            ValidateText(subject, field, 120, allowEmpty: false);
    }

    private static void ValidateDays(List<DayOfWeek>? days, string field)
    {
        if (days is null || days.Count > 7 || days.Distinct().Count() != days.Count)
            throw new AiContractValidationException($"{field} contém valores duplicados ou inválidos.");
        foreach (var day in days)
            ValidateEnum(day, field);
    }

    private static void ValidatePath(string? path, string field)
    {
        ValidateText(path, field, 1_024, allowEmpty: true);
        if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path))
            throw new AiContractValidationException($"{field} precisa ser absoluto.");
    }

    private static void ValidateText(string? value, string field, int maxLength, bool allowEmpty, bool allowLineBreaks = false)
    {
        if (value is null || value.Length > maxLength || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
            throw new AiContractValidationException($"{field} contém texto inválido.");

        foreach (var character in value)
        {
            if (!char.IsControl(character)) continue;
            if (allowLineBreaks && character is '\r' or '\n' or '\t') continue;
            throw new AiContractValidationException($"{field} contém caractere de controle não permitido.");
        }
    }

    private static void ValidateEnum<T>(T value, string field) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new AiContractValidationException($"O {field} é inválido.");
    }
}
