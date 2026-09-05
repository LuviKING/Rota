namespace Rota.Desktop.LocalAI;

public sealed class AiPlanningService : IAiPlanningService
{
    private readonly ILocalAiBackend _backend;
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiPlanningContextProvider? _contextProvider;
    private readonly IAiEnemCatalogProvider? _enemCatalogProvider;

    public AiPlanningService(
        ILocalAiBackend backend,
        IAiConfigurationStore configurationStore,
        IAiPlanningContextProvider? contextProvider = null,
        IAiEnemCatalogProvider? enemCatalogProvider = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _contextProvider = contextProvider;
        _enemCatalogProvider = enemCatalogProvider;
    }

    public async Task<AiProposal> CreateProposalAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default) =>
        (await CreateGenerationAsync(input, kind, cancellationToken).ConfigureAwait(false)).Proposal;

    public async Task<AiProposalGeneration> CreateGenerationAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default) =>
        await CreateGenerationAsync(input, kind, null, cancellationToken).ConfigureAwait(false);

    public async Task<AiProposalGeneration> CreateGenerationAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        AiConversationContext? conversationContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiContractValidator.ValidateInput(input);
        if (!Enum.IsDefined(kind))
            throw new AiContractValidationException("O tipo de proposta solicitado é inválido.");
        if (conversationContext is not null)
            AiContractValidator.ValidateConversationContext(conversationContext);

        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        AiContractValidator.ValidateConfiguration(configuration);

        AiProposal proposal;
        AiPlanningContext? planningContext = null;
        AiEnemCatalogContext? enemCatalogContext = null;
        try
        {
            var configuredContext = _contextProvider?.Capture();
            planningContext = kind == AiProposalKind.PlanChanges ? configuredContext : null;
            var effectiveInput = kind == AiProposalKind.StudyPlan
                ? ApplyConfiguredPlanningDefaults(input, configuredContext)
                : input;
            enemCatalogContext = _enemCatalogProvider?.CreateContext(effectiveInput);
            if (enemCatalogContext is not null)
                AiContractValidator.ValidateEnemCatalogContext(enemCatalogContext);
            proposal = await _backend
                .CreateProposalAsync(
                    effectiveInput,
                    kind,
                    configuration,
                    planningContext,
                    conversationContext,
                    enemCatalogContext,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiPlanningException("O backend local não conseguiu preparar a proposta da IA.", ex);
        }

        try
        {
            AiContractValidator.ValidateProposal(proposal, kind);
            if (proposal.Status != AiProposalStatus.Pending)
                throw new AiContractValidationException("Uma nova proposta da IA precisa iniciar no estado Pending.");
            if (enemCatalogContext is not null)
                EnemProposalValidator.Validate(proposal, enemCatalogContext);
        }
        catch (AiContractValidationException ex)
        {
            throw new AiPlanningException("O backend local retornou uma proposta inválida.", ex);
        }

        return new AiProposalGeneration
        {
            Proposal = proposal,
            PlanningContext = planningContext,
            EnemCatalogContext = enemCatalogContext
        };
    }

    private static AiAssistantInput ApplyConfiguredPlanningDefaults(
        AiAssistantInput input,
        AiPlanningContext? context)
    {
        if (context is null) return input;

        var notes = input.Notes.Trim();
        var blockPreference = $"Bloco preferido configurado no Rota: {context.BlockMinutes} minutos.";
        if (notes.Length == 0)
            notes = blockPreference;
        else if (notes.Length + Environment.NewLine.Length + blockPreference.Length <= 2_000)
            notes += Environment.NewLine + blockPreference;

        var effective = input with
        {
            AvailableHoursPerDay = input.AvailableHoursPerDay ?? context.DailyMinutesLimit / 60d,
            AvailableDays = input.AvailableDays.Count == 0
                ? context.AvailableDays.ToList()
                : input.AvailableDays.ToList(),
            Notes = notes
        };
        AiContractValidator.ValidateInput(effective);
        return effective;
    }
}
