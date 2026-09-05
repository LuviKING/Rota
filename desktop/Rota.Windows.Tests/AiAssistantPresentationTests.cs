using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiAssistantPresentationTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI assistant presentation distinguishes validated and blocked previews", PresentationMapsSafetyState),
        ("AI assistant presentation summarizes preview changes", PresentationSummarizesChanges),
        ("AI assistant presentation maps persistent conversation turns", PresentationMapsConversation)
    };

    private static void PresentationMapsSafetyState()
    {
        var ready = new AiProposalCardView(Stored(AiProposalPreviewState.Ready, AiProposalStatus.Validated));
        var blocked = new AiProposalCardView(Stored(AiProposalPreviewState.Blocked, AiProposalStatus.Failed));
        var appliedFromReceipt = new AiProposalCardView(
            Stored(AiProposalPreviewState.Ready, AiProposalStatus.Validated),
            new CalendarApplicationState(true, true, false, true, "Pode desfazer."),
            actionsEnabled: true);
        var undone = new AiProposalCardView(
            Stored(AiProposalPreviewState.Ready, AiProposalStatus.Undone),
            new CalendarApplicationState(true, false, true, false, "Já desfeita."),
            actionsEnabled: true);

        Require(ready.StatusDisplay == "VALIDADA");
        Require(blocked.StatusDisplay == "BLOQUEADA");
        Require(ready.KindDisplay == "AJUSTE DO PLANO");
        Require(ready.ApplyVisibility == System.Windows.Visibility.Visible && ready.CanApply);
        Require(appliedFromReceipt.StatusDisplay == "APLICADA");
        Require(appliedFromReceipt.ApplyVisibility == System.Windows.Visibility.Collapsed && appliedFromReceipt.CanUndo);
        Require(undone.StatusDisplay == "DESFEITA");
    }

    private static void PresentationSummarizesChanges()
    {
        var card = new AiProposalCardView(Stored(AiProposalPreviewState.Ready, AiProposalStatus.Validated));

        Require(card.MetricsDisplay.Contains("4 → 5 sessões", StringComparison.Ordinal));
        Require(card.MetricsDisplay.Contains("+2 sessões", StringComparison.Ordinal));
        Require(card.MetricsDisplay.Contains("1 movidas", StringComparison.Ordinal));
    }

    private static void PresentationMapsConversation()
    {
        var pending = new AiConversationTurnView(new AiConversationTurn
        {
            RequestId = Guid.NewGuid(),
            Role = AiConversationRole.User,
            Status = AiConversationTurnStatus.Pending,
            ProposalKind = AiProposalKind.StudyPlan,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Text = "Monte um plano."
        });
        var response = new AiConversationTurnView(new AiConversationTurn
        {
            RequestId = Guid.NewGuid(),
            Role = AiConversationRole.Assistant,
            Status = AiConversationTurnStatus.Completed,
            ProposalKind = AiProposalKind.PlanChanges,
            ProposalId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Text = "Proposta preparada."
        });

        Require(pending.RoleDisplay == "VOCÊ" && pending.StatusDisplay == "GERANDO…");
        Require(pending.BubbleAlignment == System.Windows.HorizontalAlignment.Right);
        Require(response.RoleDisplay == "ROTA IA");
        Require(response.ProposalVisibility == System.Windows.Visibility.Visible);
        Require(response.ProposalDisplay.Contains("ajuste", StringComparison.OrdinalIgnoreCase));
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
