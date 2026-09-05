using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiConversationTests
{
    private static readonly DateTimeOffset Timestamp = new(2031, 2, 3, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI conversation persists a linked request and response", ConversationPersistsLinkedExchange),
        ("AI conversation context is bounded and excludes unfinished turns", ContextIsBoundedAndCompletedOnly),
        ("AI conversation records cancellation without a partial response", ConversationRecordsCancellation),
        ("AI conversation reconciles a proposal saved before its response", ConversationReconcilesSavedProposal),
        ("AI conversation recovers its last valid atomic backup", ConversationRecoversAtomicBackup),
        ("AI proposal history persists the conversation request link", ProposalHistoryPersistsRequestLink)
    };

    private static void ConversationPersistsLinkedExchange()
    {
        WithStore((store, path, requestId) =>
        {
            var initial = store.LoadAsync().GetAwaiter().GetResult();
            var started = store.BeginRequestAsync("Reorganize minha semana.", AiProposalKind.PlanChanges)
                .GetAwaiter().GetResult();
            var completed = store.CompleteRequestAsync(requestId, StoredProposal(requestId))
                .GetAwaiter().GetResult();

            Require(initial.ConversationId == completed.ConversationId);
            Require(started.Turns.Single().Status == AiConversationTurnStatus.Pending);
            Require(completed.Turns.Count == 2);
            Require(completed.Turns[0].Status == AiConversationTurnStatus.Completed);
            Require(completed.Turns[1].ProposalId == StoredProposal(requestId).Proposal.Id);

            using var reloaded = new AiConversationStore(path);
            var persisted = reloaded.LoadAsync().GetAwaiter().GetResult();
            Require(persisted.ConversationId == completed.ConversationId);
            Require(persisted.Turns.SequenceEqual(completed.Turns));
        });
    }

    private static void ContextIsBoundedAndCompletedOnly()
    {
        var turns = new List<AiConversationTurn>();
        for (var index = 0; index < 8; index++)
        {
            var requestId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}");
            turns.Add(Turn(requestId, AiConversationRole.User, AiConversationTurnStatus.Completed,
                new string((char)('A' + index), 700), Timestamp.AddMinutes(index * 2)));
            turns.Add(Turn(requestId, AiConversationRole.Assistant, AiConversationTurnStatus.Completed,
                "Resposta " + index, Timestamp.AddMinutes(index * 2 + 1), Guid.NewGuid()));
        }
        turns.Add(Turn(Guid.NewGuid(), AiConversationRole.User, AiConversationTurnStatus.Cancelled,
            "Pedido cancelado", Timestamp.AddHours(1)));
        var context = AiConversationContextFactory.Create(new AiConversationSnapshot
        {
            ConversationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            UpdatedAtUtc = Timestamp.AddHours(1),
            Turns = turns
        });

        Require(context.Turns.Count == 12);
        Require(context.Turns[0].Text.StartsWith("C", StringComparison.Ordinal));
        Require(context.Turns.All(turn => turn.Text.Length <= 500));
        Require(context.Turns.Sum(turn => turn.Text.Length) <= 6_000);
        Require(context.Turns.Select(turn => turn.Role)
            .SequenceEqual(Enumerable.Range(0, 12).Select(index =>
                index % 2 == 0 ? AiConversationRole.User : AiConversationRole.Assistant)));
    }

    private static void ConversationRecordsCancellation()
    {
        WithStore((store, _, requestId) =>
        {
            store.LoadAsync().GetAwaiter().GetResult();
            store.BeginRequestAsync("Cancele se eu pedir.", AiProposalKind.StudyPlan).GetAwaiter().GetResult();
            var cancelled = store.MarkRequestAsync(requestId, AiConversationTurnStatus.Cancelled)
                .GetAwaiter().GetResult();

            Require(cancelled.Turns.Count == 1);
            Require(cancelled.Turns[0].Status == AiConversationTurnStatus.Cancelled);
            Require(AiConversationContextFactory.Create(cancelled).Turns.Count == 0);
        });
    }

    private static void ConversationReconcilesSavedProposal()
    {
        WithStore((store, path, requestId) =>
        {
            store.LoadAsync().GetAwaiter().GetResult();
            store.BeginRequestAsync("Gere uma proposta.", AiProposalKind.PlanChanges).GetAwaiter().GetResult();

            using var restarted = new AiConversationStore(path, () => Timestamp.AddMinutes(2));
            var reconciled = restarted.ReconcileAsync(new[] { StoredProposal(requestId) })
                .GetAwaiter().GetResult();

            Require(reconciled.Turns.Count == 2);
            Require(reconciled.Turns[0].Status == AiConversationTurnStatus.Completed);
            Require(reconciled.Turns[1].ProposalId == StoredProposal(requestId).Proposal.Id);
        });
    }

    private static void ConversationRecoversAtomicBackup()
    {
        WithStore((store, path, _) =>
        {
            store.LoadAsync().GetAwaiter().GetResult();
            store.BeginRequestAsync("Pedido preservado.", AiProposalKind.StudyPlan).GetAwaiter().GetResult();
            File.WriteAllText(path, "{broken");

            using var recoveredStore = new AiConversationStore(path, () => Timestamp.AddMinutes(3));
            var recovered = recoveredStore.LoadAsync().GetAwaiter().GetResult();

            Require(recovered.Turns.Count == 0);
            Require(recoveredStore.LastLoadWarning.Contains("recuperou", StringComparison.OrdinalIgnoreCase));
            Require(Directory.GetFiles(Path.GetDirectoryName(path)!, "conversation.corrupt-*.json").Length == 1);
        });
    }

    private static void ProposalHistoryPersistsRequestLink()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaConversationProposalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var requestId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
            var stored = StoredProposal(requestId);
            using (var proposalStore = new AiProposalStore(Path.Combine(directory, "proposals.json"), () => Timestamp))
            {
                var saved = proposalStore.SavePreviewAsync(
                    stored.Proposal with { Status = AiProposalStatus.Pending },
                    stored.Preview,
                    requestId,
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(saved.RequestTurnId == requestId);
            }
            using var reloaded = new AiProposalStore(Path.Combine(directory, "proposals.json"));
            Require(reloaded.LoadAsync().GetAwaiter().GetResult().Single().RequestTurnId == requestId);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static AiConversationTurn Turn(
        Guid requestId,
        AiConversationRole role,
        AiConversationTurnStatus status,
        string text,
        DateTimeOffset createdAt,
        Guid proposalId = default) => new()
    {
        RequestId = requestId,
        Role = role,
        Status = status,
        ProposalKind = AiProposalKind.PlanChanges,
        ProposalId = proposalId,
        CreatedAtUtc = createdAt,
        Text = text
    };

    private static AiStoredProposal StoredProposal(Guid requestId)
    {
        var proposalId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        var operationId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
        return new AiStoredProposal
        {
            RequestTurnId = requestId,
            UpdatedAtUtc = Timestamp.AddMinutes(1),
            Proposal = new AiProposal
            {
                Id = proposalId,
                CreatedAtUtc = Timestamp,
                Summary = "Semana reorganizada.",
                Kind = AiProposalKind.PlanChanges,
                Status = AiProposalStatus.Validated,
                Changes = new AiPlanChangeDraft
                {
                    Operations = new List<AiPlanOperation>
                    {
                        new()
                        {
                            Id = operationId,
                            Type = AiPlanOperationType.MoveSession,
                            Summary = "Mover sessão.",
                            SessionId = "session-1",
                            DestinationDate = "2031-02-04"
                        }
                    }
                }
            },
            Preview = new AiProposalPreview
            {
                ProposalId = proposalId,
                Kind = AiProposalKind.PlanChanges,
                State = AiProposalPreviewState.Ready,
                Message = "Prévia pronta para revisão.",
                BeforeSessionCount = 1,
                AfterSessionCount = 1,
                BeforeMinutes = 60,
                AfterMinutes = 60,
                MovedSessionCount = 1
            }
        };
    }

    private static void WithStore(Action<AiConversationStore, string, Guid> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaConversationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "conversation.json");
        var conversationId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
        var requestId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
        var ids = new Queue<Guid>(new[] { conversationId, requestId });
        try
        {
            using var store = new AiConversationStore(path, () => Timestamp, () => ids.Dequeue());
            action(store, path, requestId);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("AI conversation assertion failed.");
    }
}
