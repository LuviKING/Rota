namespace Rota.Desktop.LocalAI;

public sealed class AiPlanningService : IAiPlanningService
{
    private readonly ILocalAiBackend _backend;
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiPlanningContextProvider? _contextProvider;

    public AiPlanningService(
        ILocalAiBackend backend,
        IAiConfigurationStore configurationStore,
        IAiPlanningContextProvider? contextProvider = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _contextProvider = contextProvider;
    }

    public async Task<AiProposal> CreateProposalAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiContractValidator.ValidateInput(input);
        if (!Enum.IsDefined(kind))
            throw new AiContractValidationException("O tipo de proposta solicitado é inválido.");

        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        AiContractValidator.ValidateConfiguration(configuration);

        AiProposal proposal;
        try
        {
            var planningContext = kind == AiProposalKind.PlanChanges
                ? _contextProvider?.Capture()
                : null;
            proposal = await _backend
                .CreateProposalAsync(input, kind, configuration, planningContext, cancellationToken)
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
        }
        catch (AiContractValidationException ex)
        {
            throw new AiPlanningException("O backend local retornou uma proposta inválida.", ex);
        }

        return proposal;
    }
}
