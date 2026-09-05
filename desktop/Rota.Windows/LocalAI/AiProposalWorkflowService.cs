namespace Rota.Desktop.LocalAI;

public sealed class AiProposalWorkflowService : IAiProposalWorkflowService, IDisposable
{
    private readonly IAiPlanningService _planningService;
    private readonly IAiProposalPreviewService _previewService;
    private readonly IAiProposalStore _proposalStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AiProposalWorkflowService(
        IAiPlanningService planningService,
        IAiProposalPreviewService previewService,
        IAiProposalStore proposalStore)
    {
        _planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
    }

    public async Task<AiStoredProposal> PrepareAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var generation = await _planningService
                .CreateGenerationAsync(input, kind, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation.Proposal.Kind != kind)
                throw new AiContractValidationException("A geração retornou um tipo de proposta inesperado.");
            if (kind == AiProposalKind.PlanChanges && generation.PlanningContext is null)
                throw new AiContractValidationException("A proposta de alteração não possui a fotografia usada na geração.");
            if (kind == AiProposalKind.StudyPlan && generation.PlanningContext is not null)
                throw new AiContractValidationException("Uma proposta de plano novo recebeu contexto indevido.");

            var preview = _previewService.Preview(generation.Proposal, generation.PlanningContext);
            AiContractValidator.ValidatePreview(preview);
            cancellationToken.ThrowIfCancellationRequested();
            return await _proposalStore
                .SavePreviewAsync(generation.Proposal, preview, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiProposalWorkflowException(
                "O fluxo local da IA não conseguiu preparar e registrar a proposta.",
                ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
