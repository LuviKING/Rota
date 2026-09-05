using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiAssistantControllerTests
{
    private static readonly Guid ProposalId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ConversationId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid RequestId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI assistant controller loads profile installation and history", ControllerInitializes),
        ("AI assistant controller maps all quick actions", ControllerMapsQuickActions),
        ("AI assistant controller sends and surfaces a safe preview", ControllerSendsProposal),
        ("AI assistant controller blocks generation before local installation", ControllerBlocksUnavailableRuntime),
        ("AI assistant controller cancels an active generation", ControllerCancelsGeneration),
        ("AI assistant controller contains workflow failures", ControllerContainsFailure)
    };

    private static void ControllerInitializes()
    {
        var stored = StoredProposal();
        var store = new StubProposalStore(new[] { stored });
        var workflow = new StubWorkflow((_, _, _) => Task.FromResult(stored));
        using var fixture = Controller(AiInstallationState.Ready, store, workflow);
        var events = 0;
        fixture.Controller.StateChanged += (_, _) => events++;

        fixture.Controller.InitializeAsync().GetAwaiter().GetResult();

        var state = fixture.Controller.State;
        Require(state.IsInitialized && !state.IsBusy && state.IsOfflineReady);
        Require(state.EffectiveProfile == AiProfile.Performance);
        Require(state.History.Single().Proposal.Id == ProposalId);
        Require(state.StatusMessage.Contains("offline", StringComparison.OrdinalIgnoreCase));
        Require(events >= 2);
    }

    private static void ControllerMapsQuickActions()
    {
        using var fixture = Controller();
        var original = new AiAssistantInput { ObjectiveOrExam = "ENEM", FreeText = "Tenho 3 horas." };
        foreach (var action in Enum.GetValues<AiAssistantQuickAction>())
        {
            var input = fixture.Controller.ApplyQuickAction(original, action);
            Require(input.ObjectiveOrExam == "ENEM");
            Require(input.FreeText.StartsWith("Tenho 3 horas.", StringComparison.Ordinal));
        }
        Require(fixture.Controller.SuggestedKind(AiAssistantQuickAction.BuildPlan) == AiProposalKind.StudyPlan);
        Require(fixture.Controller.SuggestedKind(AiAssistantQuickAction.PrepareForEnem) == AiProposalKind.StudyPlan);
        Require(fixture.Controller.SuggestedKind(AiAssistantQuickAction.AdjustLoad) == AiProposalKind.PlanChanges);
        Expect<AiContractValidationException>(() =>
            fixture.Controller.SuggestedKind((AiAssistantQuickAction)999));
    }

    private static void ControllerSendsProposal()
    {
        var stored = StoredProposal();
        var store = new StubProposalStore(Array.Empty<AiStoredProposal>());
        var workflow = new StubWorkflow((_, _, _) => Task.FromResult(stored));
        using var fixture = Controller(AiInstallationState.Ready, store, workflow);
        fixture.Controller.InitializeAsync().GetAwaiter().GetResult();

        var result = fixture.Controller.SendAsync(
            new AiAssistantInput { FreeText = "Reorganize amanhã." },
            AiProposalKind.PlanChanges).GetAwaiter().GetResult();

        Require(result?.Proposal.Id == ProposalId);
        Require(workflow.CallCount == 1);
        Require(fixture.Controller.State.History.Single().Proposal.Id == ProposalId);
        Require(fixture.Controller.State.Conversation.Count == 2);
        Require(fixture.Controller.State.Conversation.Last().ProposalId == ProposalId);
        Require(workflow.LastConversationContext?.Turns.Count == 0);
        Require(fixture.Controller.State.StatusMessage.Contains("Nada foi aplicado", StringComparison.Ordinal));
    }

    private static void ControllerBlocksUnavailableRuntime()
    {
        var workflow = new StubWorkflow((_, _, _) => Task.FromResult(StoredProposal()));
        using var fixture = Controller(AiInstallationState.NotInstalled, workflow: workflow);
        fixture.Controller.InitializeAsync().GetAwaiter().GetResult();

        var result = fixture.Controller.SendAsync(
            new AiAssistantInput { FreeText = "Monte um plano." },
            AiProposalKind.StudyPlan).GetAwaiter().GetResult();

        Require(result is null && workflow.CallCount == 0);
        Require(fixture.Controller.State.Activity == AiAssistantActivity.Error);
        Require(fixture.Controller.State.StatusMessage.Contains("Instale", StringComparison.Ordinal));
    }

    private static void ControllerCancelsGeneration()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new StubWorkflow(async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return StoredProposal();
        });
        using var fixture = Controller(AiInstallationState.Ready, workflow: workflow);
        fixture.Controller.InitializeAsync().GetAwaiter().GetResult();

        var pending = fixture.Controller.SendAsync(
            new AiAssistantInput { FreeText = "Reorganize." },
            AiProposalKind.PlanChanges);
        Require(started.Task.Wait(TimeSpan.FromSeconds(2)));
        fixture.Controller.CancelCurrentOperation();
        var result = pending.GetAwaiter().GetResult();

        Require(result is null);
        Require(fixture.Controller.State.Activity == AiAssistantActivity.Idle);
        Require(fixture.Controller.State.StatusMessage.Contains("cancelada", StringComparison.OrdinalIgnoreCase));
        Require(fixture.Controller.State.Conversation.Single().Status == AiConversationTurnStatus.Cancelled);
    }

    private static void ControllerContainsFailure()
    {
        var workflow = new StubWorkflow((_, _, _) => throw new InvalidOperationException("secret failure detail"));
        using var fixture = Controller(AiInstallationState.Ready, workflow: workflow);
        fixture.Controller.InitializeAsync().GetAwaiter().GetResult();

        var result = fixture.Controller.SendAsync(
            new AiAssistantInput { FreeText = "Reorganize." },
            AiProposalKind.PlanChanges).GetAwaiter().GetResult();

        Require(result is null);
        Require(fixture.Controller.State.Activity == AiAssistantActivity.Error);
        Require(fixture.Controller.State.Warnings.Last().Contains("conteve", StringComparison.OrdinalIgnoreCase));
        Require(!fixture.Controller.State.Warnings.Any(item => item.Contains("secret", StringComparison.Ordinal)));
        Require(fixture.Controller.State.Conversation.Single().Status == AiConversationTurnStatus.Failed);
    }

    private static ControllerFixture Controller(
        AiInstallationState installationState = AiInstallationState.Ready,
        StubProposalStore? store = null,
        StubWorkflow? workflow = null)
    {
        store ??= new StubProposalStore(Array.Empty<AiStoredProposal>());
        workflow ??= new StubWorkflow((_, _, _) => Task.FromResult(StoredProposal()));
        var configuration = new StubConfigurationStore();
        var modelManager = new StubModelManager(installationState);
        var conversation = new StubConversationStore();
        return new ControllerFixture(
            new AiAssistantController(configuration, modelManager, store, workflow, conversation));
    }

    private static AiStoredProposal StoredProposal() => new()
    {
        Proposal = new AiProposal
        {
            Id = ProposalId,
            CreatedAtUtc = new DateTimeOffset(2031, 2, 3, 12, 0, 0, TimeSpan.Zero),
            Summary = "Proposta pronta.",
            Kind = AiProposalKind.PlanChanges,
            Status = AiProposalStatus.Validated,
            Changes = new AiPlanChangeDraft
            {
                Operations = new List<AiPlanOperation>
                {
                    new()
                    {
                        Id = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
                        Type = AiPlanOperationType.MoveSession,
                        Summary = "Mover sessão.",
                        SessionId = "session-1",
                        DestinationDate = "2031-02-04"
                    }
                }
            },
            Warnings = new List<string>()
        },
        Preview = new AiProposalPreview
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
            Warnings = new List<string>()
        },
        UpdatedAtUtc = new DateTimeOffset(2031, 2, 3, 12, 1, 0, TimeSpan.Zero)
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("AI assistant controller assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class ControllerFixture : IDisposable
    {
        public ControllerFixture(AiAssistantController controller) => Controller = controller;
        public AiAssistantController Controller { get; }
        public void Dispose() => Controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class StubConfigurationStore : IAiConfigurationStore
    {
        public string ConfigurationPath => Path.Combine(Path.GetTempPath(), "stub-ai-config.json");
        public string LastLoadWarning => "";
        public Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiConfiguration { Profile = AiProfile.Performance });
        public Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubModelManager : IAiModelManager
    {
        private readonly AiInstallationState _state;
        public StubModelManager(AiInstallationState state) => _state = state;
        public Task<AiModelInstallationInfo> GetInstallationInfoAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default) => Task.FromResult(new AiModelInstallationInfo(
                _state,
                AiProfile.Performance,
                AiModelCatalog.Default.GetRecommendedModel(AiProfile.Performance),
                "runtime",
                "model",
                _state is AiInstallationState.RuntimeInstalled or AiInstallationState.Ready,
                _state == AiInstallationState.Ready,
                Array.Empty<string>()));
    }

    private sealed class StubProposalStore : IAiProposalStore
    {
        private readonly IReadOnlyList<AiStoredProposal> _history;
        public StubProposalStore(IReadOnlyList<AiStoredProposal> history) => _history = history;
        public string StorePath => Path.Combine(Path.GetTempPath(), "stub-proposals.json");
        public string LastLoadWarning => "";
        public Task<IReadOnlyList<AiStoredProposal>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_history);
        public Task<AiStoredProposal> SavePreviewAsync(
            AiProposal proposal,
            AiProposalPreview preview,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiStoredProposal> AcceptAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AiStoredProposal> MarkAppliedAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AiStoredProposal> MarkUndoneAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AiStoredProposal> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubWorkflow : IAiProposalWorkflowService
    {
        private readonly Func<AiAssistantInput, AiProposalKind, CancellationToken, Task<AiStoredProposal>> _handler;
        public StubWorkflow(Func<AiAssistantInput, AiProposalKind, CancellationToken, Task<AiStoredProposal>> handler) =>
            _handler = handler;
        public int CallCount { get; private set; }
        public AiConversationContext? LastConversationContext { get; private set; }
        public Task<AiStoredProposal> PrepareAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _handler(input, kind, cancellationToken);
        }

        public async Task<AiStoredProposal> PrepareAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            Guid requestTurnId,
            AiConversationContext? conversationContext,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastConversationContext = conversationContext;
            var stored = await _handler(input, kind, cancellationToken).ConfigureAwait(false);
            return stored with { RequestTurnId = requestTurnId };
        }
    }

    private sealed class StubConversationStore : IAiConversationStore
    {
        private List<AiConversationTurn> _turns = new();
        public string StorePath => Path.Combine(Path.GetTempPath(), "stub-conversation.json");
        public string LastLoadWarning => "";

        public Task<AiConversationSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot());

        public Task<AiConversationSnapshot> ReconcileAsync(
            IReadOnlyList<AiStoredProposal> proposals,
            CancellationToken cancellationToken = default) => Task.FromResult(Snapshot());

        public Task<AiConversationSnapshot> BeginRequestAsync(
            string text,
            AiProposalKind kind,
            CancellationToken cancellationToken = default)
        {
            _turns.Add(new AiConversationTurn
            {
                RequestId = RequestId,
                Role = AiConversationRole.User,
                Status = AiConversationTurnStatus.Pending,
                ProposalKind = kind,
                CreatedAtUtc = new DateTimeOffset(2031, 2, 3, 11, 59, 0, TimeSpan.Zero),
                Text = text
            });
            return Task.FromResult(Snapshot());
        }

        public Task<AiConversationSnapshot> CompleteRequestAsync(
            Guid requestId,
            AiStoredProposal proposal,
            CancellationToken cancellationToken = default)
        {
            var user = _turns.Single(turn => turn.RequestId == requestId);
            _turns[_turns.IndexOf(user)] = user with { Status = AiConversationTurnStatus.Completed };
            _turns.Add(new AiConversationTurn
            {
                RequestId = requestId,
                Role = AiConversationRole.Assistant,
                Status = AiConversationTurnStatus.Completed,
                ProposalKind = proposal.Proposal.Kind,
                ProposalId = proposal.Proposal.Id,
                CreatedAtUtc = proposal.UpdatedAtUtc,
                Text = proposal.Proposal.Summary
            });
            return Task.FromResult(Snapshot());
        }

        public Task<AiConversationSnapshot> MarkRequestAsync(
            Guid requestId,
            AiConversationTurnStatus status,
            CancellationToken cancellationToken = default)
        {
            var user = _turns.Single(turn => turn.RequestId == requestId);
            _turns[_turns.IndexOf(user)] = user with { Status = status };
            return Task.FromResult(Snapshot());
        }

        private AiConversationSnapshot Snapshot() => new()
        {
            ConversationId = ConversationId,
            UpdatedAtUtc = new DateTimeOffset(2031, 2, 3, 12, 1, 0, TimeSpan.Zero),
            Turns = _turns.ToList()
        };
    }
}
