using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class ProposalStoreTests
{
    private static readonly DateTimeOffset Time1 = new(2031, 2, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Time2 = Time1.AddMinutes(1);
    private static readonly DateTimeOffset Time3 = Time2.AddMinutes(1);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("AI proposal store persists validated previews outside study state", StorePersistsSeparately),
        ("AI proposal store records monotonic acceptance and rejection", StoreTransitionsMonotonically),
        ("AI proposal store keeps blocked previews failed", StoreKeepsBlockedPreviewFailed),
        ("AI proposal store rejects mismatched or duplicate records", StoreRejectsMismatchedAndDuplicateRecords),
        ("AI proposal store recovers its last valid atomic backup", StoreRecoversAtomicBackup),
        ("AI proposal store rejects duplicate JSON properties", StoreRejectsDuplicateJsonProperties)
    };

    private static void StorePersistsSeparately()
    {
        WithStore((store, directory) =>
        {
            var studyPath = Path.Combine(directory, "desktop-state.json");
            var repository = new StudyRepository(studyPath, () => Time1.LocalDateTime);
            var studyBefore = File.ReadAllBytes(studyPath);

            var saved = store.SavePreviewAsync(Proposal(1), ReadyPreview(1)).GetAwaiter().GetResult();
            var loaded = store.LoadAsync().GetAwaiter().GetResult().Single();

            Require(saved.Proposal.Status == AiProposalStatus.Validated);
            Require(loaded.Proposal.Id == ProposalId(1));
            Require(loaded.Preview.CanProceed);
            Require(Path.GetFullPath(store.StorePath) != Path.GetFullPath(repository.DataPath));
            Require(File.ReadAllBytes(studyPath).SequenceEqual(studyBefore));
        });
    }

    private static void StoreTransitionsMonotonically()
    {
        WithStore((_, directory) =>
        {
            var times = new Queue<DateTimeOffset>(new[] { Time1, Time2, Time3 });
            using var store = new AiProposalStore(
                Path.Combine(directory, "transitions.json"),
                () => times.Dequeue());
            store.SavePreviewAsync(Proposal(2), ReadyPreview(2)).GetAwaiter().GetResult();
            var accepted = store.AcceptAsync(ProposalId(2)).GetAwaiter().GetResult();
            Require(accepted.Proposal.Status == AiProposalStatus.Accepted);
            Require(accepted.UpdatedAtUtc == Time2);
            var rejected = store.RejectAsync(ProposalId(2)).GetAwaiter().GetResult();
            Require(rejected.Proposal.Status == AiProposalStatus.Rejected);
            Require(rejected.UpdatedAtUtc == Time3);
            Expect<AiContractValidationException>(() =>
                store.AcceptAsync(ProposalId(2)).GetAwaiter().GetResult());

            using var reopened = new AiProposalStore(store.StorePath);
            Require(reopened.LoadAsync().GetAwaiter().GetResult().Single().Proposal.Status == AiProposalStatus.Rejected);
        });
    }

    private static void StoreKeepsBlockedPreviewFailed()
    {
        WithStore((store, _) =>
        {
            var saved = store.SavePreviewAsync(Proposal(3), BlockedPreview(3)).GetAwaiter().GetResult();
            Require(saved.Proposal.Status == AiProposalStatus.Failed);
            Expect<AiContractValidationException>(() =>
                store.AcceptAsync(ProposalId(3)).GetAwaiter().GetResult());
        });
    }

    private static void StoreRejectsMismatchedAndDuplicateRecords()
    {
        WithStore((store, _) =>
        {
            var before = File.ReadAllBytes(store.StorePath);
            Expect<AiContractValidationException>(() =>
                store.SavePreviewAsync(Proposal(4), ReadyPreview(5)).GetAwaiter().GetResult());
            Require(File.ReadAllBytes(store.StorePath).SequenceEqual(before));

            store.SavePreviewAsync(Proposal(4), ReadyPreview(4)).GetAwaiter().GetResult();
            var once = File.ReadAllBytes(store.StorePath);
            Expect<AiContractValidationException>(() =>
                store.SavePreviewAsync(Proposal(4), ReadyPreview(4)).GetAwaiter().GetResult());
            Require(File.ReadAllBytes(store.StorePath).SequenceEqual(once));
        });
    }

    private static void StoreRecoversAtomicBackup()
    {
        WithStore((store, _) =>
        {
            store.SavePreviewAsync(Proposal(6), ReadyPreview(6)).GetAwaiter().GetResult();
            store.SavePreviewAsync(Proposal(7), ReadyPreview(7)).GetAwaiter().GetResult();
            File.WriteAllText(store.StorePath, "{invalid");

            var recovered = store.LoadAsync().GetAwaiter().GetResult();

            Require(recovered.Count == 1 && recovered[0].Proposal.Id == ProposalId(6));
            Require(store.LastLoadWarning.Contains("recuperou", StringComparison.OrdinalIgnoreCase));
            Require(Directory.GetFiles(store.StoreDirectory, "proposals.corrupt-*.json").Length == 1);
        });
    }

    private static void StoreRejectsDuplicateJsonProperties()
    {
        WithStore((store, _) =>
        {
            store.SavePreviewAsync(Proposal(8), ReadyPreview(8)).GetAwaiter().GetResult();
            var json = File.ReadAllText(store.StorePath);
            File.WriteAllText(store.StorePath, json.Replace(
                "\"SchemaVersion\": 1,",
                "\"SchemaVersion\": 1,\n  \"SchemaVersion\": 1,",
                StringComparison.Ordinal));

            var recovered = store.LoadAsync().GetAwaiter().GetResult();

            Require(recovered.Count == 0);
            Require(store.LastLoadWarning.Contains("recuperou", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static AiProposal Proposal(int suffix) => new()
    {
        Id = ProposalId(suffix),
        CreatedAtUtc = Time1,
        Summary = $"Proposta {suffix}.",
        Kind = AiProposalKind.PlanChanges,
        Changes = new AiPlanChangeDraft
        {
            Operations = new List<AiPlanOperation>
            {
                new()
                {
                    Id = OperationId(suffix),
                    Type = AiPlanOperationType.MoveSession,
                    Summary = "Mover uma sessão futura.",
                    SessionId = "session-1",
                    DestinationDate = "2031-02-04"
                }
            }
        },
        Warnings = new List<string>()
    };

    private static AiProposalPreview ReadyPreview(int suffix) => new()
    {
        ProposalId = ProposalId(suffix),
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
                OperationId = OperationId(suffix),
                Type = AiPlanOperationType.MoveSession,
                Summary = "Mover uma sessão futura.",
                SessionId = "session-1",
                OriginalDate = "2031-02-03",
                ProposedDate = "2031-02-04"
            }
        },
        Warnings = new List<string>()
    };

    private static AiProposalPreview BlockedPreview(int suffix) => new()
    {
        ProposalId = ProposalId(suffix),
        Kind = AiProposalKind.PlanChanges,
        State = AiProposalPreviewState.Blocked,
        Message = "A proposta foi bloqueada.",
        BeforeSessionCount = 1,
        AfterSessionCount = 1,
        BeforeMinutes = 60,
        AfterMinutes = 60,
        Warnings = new List<string>()
    };

    private static Guid ProposalId(int suffix) =>
        Guid.Parse($"00000000-0000-0000-0000-{suffix:000000000000}");

    private static Guid OperationId(int suffix) =>
        Guid.Parse($"10000000-0000-0000-0000-{suffix:000000000000}");

    private static void WithStore(Action<AiProposalStore, string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaProposalStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new AiProposalStore(Path.Combine(directory, "AI", "proposals.json"), () => Time1);
            store.LoadAsync().GetAwaiter().GetResult();
            action(store, directory);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Proposal store assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
