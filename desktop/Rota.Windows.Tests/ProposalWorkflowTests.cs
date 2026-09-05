using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class ProposalWorkflowTests
{
    private static readonly Guid ProposalId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OperationId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid RequestId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset CreatedAt = new(2031, 2, 3, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI workflow reuses the exact generation context for preview and storage", WorkflowReusesExactContext),
        ("AI workflow stores a blocked preview as failed", WorkflowStoresBlockedPreview),
        ("AI workflow cancellation leaves no partial proposal", WorkflowCancellationLeavesNoPartialRecord),
        ("AI workflow contains failures and leaves no partial proposal", WorkflowContainsFailureWithoutPartialRecord),
        ("AI workflow preserves the bounded conversation link", WorkflowPreservesConversationLink)
    };

    private static void WorkflowReusesExactContext()
    {
        WithStore(store =>
        {
            var context = Context();
            var planning = new StubPlanningService(new AiProposalGeneration
            {
                Proposal = Proposal(),
                PlanningContext = context
            });
            var previewing = new StubPreviewService((proposal, received) => ReadyPreview());
            using var workflow = new AiProposalWorkflowService(planning, previewing, store);

            var stored = workflow.PrepareAsync(Input(), AiProposalKind.PlanChanges).GetAwaiter().GetResult();

            Require(ReferenceEquals(context, previewing.LastContext));
            Require(stored.Proposal.Status == AiProposalStatus.Validated);
            Require(store.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Id == ProposalId);
        });
    }

    private static void WorkflowStoresBlockedPreview()
    {
        WithStore(store =>
        {
            var planning = new StubPlanningService(new AiProposalGeneration
            {
                Proposal = Proposal(),
                PlanningContext = Context()
            });
            var previewing = new StubPreviewService((_, _) => BlockedPreview());
            using var workflow = new AiProposalWorkflowService(planning, previewing, store);

            var stored = workflow.PrepareAsync(Input(), AiProposalKind.PlanChanges).GetAwaiter().GetResult();

            Require(stored.Proposal.Status == AiProposalStatus.Failed);
            Require(!stored.Preview.CanProceed);
        });
    }

    private static void WorkflowCancellationLeavesNoPartialRecord()
    {
        WithStore(store =>
        {
            using var cancellation = new CancellationTokenSource();
            var planning = new StubPlanningService(new AiProposalGeneration
            {
                Proposal = Proposal(),
                PlanningContext = Context()
            });
            var previewing = new StubPreviewService((_, _) =>
            {
                cancellation.Cancel();
                return ReadyPreview();
            });
            using var workflow = new AiProposalWorkflowService(planning, previewing, store);

            Expect<OperationCanceledException>(() => workflow.PrepareAsync(
                Input(), AiProposalKind.PlanChanges, cancellation.Token).GetAwaiter().GetResult());

            Require(store.LoadAsync().GetAwaiter().GetResult().Count == 0);
        });
    }

    private static void WorkflowContainsFailureWithoutPartialRecord()
    {
        WithStore(store =>
        {
            var planning = new StubPlanningService(new AiProposalGeneration
            {
                Proposal = Proposal(),
                PlanningContext = Context()
            });
            var previewing = new StubPreviewService((_, _) => throw new InvalidOperationException("preview failure"));
            using var workflow = new AiProposalWorkflowService(planning, previewing, store);

            try
            {
                workflow.PrepareAsync(Input(), AiProposalKind.PlanChanges).GetAwaiter().GetResult();
            }
            catch (AiProposalWorkflowException ex)
            {
                Require(ex.InnerException is InvalidOperationException);
                Require(store.LoadAsync().GetAwaiter().GetResult().Count == 0);
                return;
            }
            throw new InvalidOperationException("Expected AiProposalWorkflowException.");
        });
    }

    private static void WorkflowPreservesConversationLink()
    {
        WithStore(store =>
        {
            var conversation = new AiConversationContext
            {
                ConversationId = Guid.Parse("cccccccc-0000-0000-0000-000000000001")
            };
            var planning = new StubPlanningService(new AiProposalGeneration
            {
                Proposal = Proposal(),
                PlanningContext = Context()
            });
            using var workflow = new AiProposalWorkflowService(
                planning,
                new StubPreviewService((_, _) => ReadyPreview()),
                store);

            var stored = workflow.PrepareAsync(
                Input(),
                AiProposalKind.PlanChanges,
                RequestId,
                conversation,
                CancellationToken.None).GetAwaiter().GetResult();

            Require(ReferenceEquals(conversation, planning.LastConversationContext));
            Require(stored.RequestTurnId == RequestId);
            Require(store.LoadAsync().GetAwaiter().GetResult().Single().RequestTurnId == RequestId);
        });
    }

    private static AiProposal Proposal() => new()
    {
        Id = ProposalId,
        CreatedAtUtc = CreatedAt,
        Summary = "Mover sessão.",
        Kind = AiProposalKind.PlanChanges,
        Changes = new AiPlanChangeDraft
        {
            Operations = new List<AiPlanOperation>
            {
                new()
                {
                    Id = OperationId,
                    Type = AiPlanOperationType.MoveSession,
                    Summary = "Mover sessão futura.",
                    SessionId = "session-1",
                    DestinationDate = "2031-02-04"
                }
            }
        },
        Warnings = new List<string>()
    };

    private static AiProposalPreview ReadyPreview() => new()
    {
        ProposalId = ProposalId,
        Kind = AiProposalKind.PlanChanges,
        State = AiProposalPreviewState.Ready,
        Message = "Prévia pronta.",
        BeforeSessionCount = 1,
        AfterSessionCount = 1,
        BeforeMinutes = 60,
        AfterMinutes = 60,
        MovedSessionCount = 1,
        Operations = new List<AiPreviewOperation>
        {
            new()
            {
                OperationId = OperationId,
                Type = AiPlanOperationType.MoveSession,
                Summary = "Mover sessão futura.",
                SessionId = "session-1",
                OriginalDate = "2031-02-03",
                ProposedDate = "2031-02-04"
            }
        },
        Warnings = new List<string>()
    };

    private static AiProposalPreview BlockedPreview() => new()
    {
        ProposalId = ProposalId,
        Kind = AiProposalKind.PlanChanges,
        State = AiProposalPreviewState.Blocked,
        Message = "Prévia bloqueada.",
        BeforeSessionCount = 1,
        AfterSessionCount = 1,
        BeforeMinutes = 60,
        AfterMinutes = 60,
        Warnings = new List<string>()
    };

    private static AiPlanningContext Context() => new()
    {
        SnapshotDate = "2031-02-03",
        ObjectiveName = "ENEM",
        ObjectiveDate = "2031-12-31",
        ActivePlanId = "plan-1",
        ActivePlanRevision = 1,
        ActivePlanTitle = "Plano atual",
        DailyMinutesLimit = 180,
        BlockMinutes = 60,
        FutureSessions = new List<AiPlanningSessionContext>
        {
            new()
            {
                SessionId = "session-1",
                PlanId = "plan-1",
                PlanRevision = 1,
                Date = "2031-02-03",
                Subject = "Matemática",
                Topic = "Funções",
                Minutes = 60,
                Kind = "study",
                Origin = "plan"
            }
        }
    };

    private static AiAssistantInput Input() => new() { FreeText = "Mova a sessão." };

    private static void WithStore(Action<AiProposalStore> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaProposalWorkflowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new AiProposalStore(Path.Combine(directory, "proposals.json"), () => CreatedAt);
            store.LoadAsync().GetAwaiter().GetResult();
            action(store);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Proposal workflow assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class StubPlanningService : IAiPlanningService
    {
        private readonly AiProposalGeneration _generation;

        public StubPlanningService(AiProposalGeneration generation) => _generation = generation;

        public AiConversationContext? LastConversationContext { get; private set; }

        public Task<AiProposal> CreateProposalAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            CancellationToken cancellationToken = default) => Task.FromResult(_generation.Proposal);

        public Task<AiProposalGeneration> CreateGenerationAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_generation);
        }

        public Task<AiProposalGeneration> CreateGenerationAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            AiConversationContext? conversationContext,
            CancellationToken cancellationToken)
        {
            LastConversationContext = conversationContext;
            return CreateGenerationAsync(input, kind, cancellationToken);
        }
    }

    private sealed class StubPreviewService : IAiProposalPreviewService
    {
        private readonly Func<AiProposal, AiPlanningContext?, AiProposalPreview> _preview;

        public StubPreviewService(Func<AiProposal, AiPlanningContext?, AiProposalPreview> preview) =>
            _preview = preview;

        public AiPlanningContext? LastContext { get; private set; }

        public AiProposalPreview Preview(AiProposal proposal, AiPlanningContext? planningContext = null)
        {
            LastContext = planningContext;
            return _preview(proposal, planningContext);
        }
    }
}
