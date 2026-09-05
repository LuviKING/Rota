using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiAssistantPresentationTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI assistant presentation distinguishes validated and blocked previews", PresentationMapsSafetyState),
        ("AI assistant presentation summarizes preview changes", PresentationSummarizesChanges)
    };

    private static void PresentationMapsSafetyState()
    {
        var ready = new AiProposalCardView(Stored(AiProposalPreviewState.Ready, AiProposalStatus.Validated));
        var blocked = new AiProposalCardView(Stored(AiProposalPreviewState.Blocked, AiProposalStatus.Failed));

        Require(ready.StatusDisplay == "VALIDADA");
        Require(blocked.StatusDisplay == "BLOQUEADA");
        Require(ready.KindDisplay == "AJUSTE DO PLANO");
    }

    private static void PresentationSummarizesChanges()
    {
        var card = new AiProposalCardView(Stored(AiProposalPreviewState.Ready, AiProposalStatus.Validated));

        Require(card.MetricsDisplay.Contains("4 → 5 sessões", StringComparison.Ordinal));
        Require(card.MetricsDisplay.Contains("+2 sessões", StringComparison.Ordinal));
        Require(card.MetricsDisplay.Contains("1 movidas", StringComparison.Ordinal));
    }

    private static AiStoredProposal Stored(AiProposalPreviewState previewState, AiProposalStatus status)
    {
        var id = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
        return new AiStoredProposal
        {
            Proposal = new AiProposal
            {
                Id = id,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Summary = "Ajuste seguro da semana.",
                Kind = AiProposalKind.PlanChanges,
                Status = status,
                Changes = new AiPlanChangeDraft()
            },
            Preview = new AiProposalPreview
            {
                ProposalId = id,
                Kind = AiProposalKind.PlanChanges,
                State = previewState,
                Message = "Prévia concluída.",
                BeforeSessionCount = 4,
                AfterSessionCount = 5,
                BeforeMinutes = 240,
                AfterMinutes = 300,
                AddedSessionCount = 2,
                RemovedSessionCount = 1,
                MovedSessionCount = 1
            },
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("AI assistant presentation assertion failed.");
    }
}
