using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

internal static class EnemCatalogTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("ENEM catalog preserves four official areas and separate writing", CatalogStructureIsStable),
        ("ENEM catalog activates only for explicit ENEM requests", ContextRequiresEnem),
        ("ENEM catalog maps user emphasis and stays bounded", ContextMapsEmphasis),
        ("ENEM catalog context rejects tampering", ContextRejectsTampering),
        ("ENEM proposal accepts only catalog subjects and contents", ProposalAcceptsCatalogValues),
        ("ENEM proposal rejects invented subjects", ProposalRejectsInventedSubject),
        ("ENEM proposal rejects invented contents", ProposalRejectsInventedContent),
        ("AI planning sends and records the ENEM catalog context", PlanningUsesCatalog)
    };

    private static void CatalogStructureIsStable()
    {
        var catalog = EnemCatalogService.Default;
        Equal("inep-enem-2026-r1", catalog.CatalogVersion);
        Require(catalog.OfficialSourceUrl.StartsWith("https://www.gov.br/inep/", StringComparison.Ordinal));
        Equal(4, catalog.Areas.Count);
        Equal("Redação", catalog.Writing.Name);
        Require(catalog.Areas.All(area => area.Subjects.Count > 0));
        Require(catalog.Areas.SelectMany(area => area.Subjects).Count() == 14);
        Require(catalog.Areas.SelectMany(area => area.Subjects).SelectMany(subject => subject.Contents).Count() == 50);
        Equal(5, catalog.Writing.Contents.Count);
    }

    private static void ContextRequiresEnem()
    {
        var catalog = EnemCatalogService.Default;
        Require(catalog.CreateContext(new AiAssistantInput { FreeText = "Quero estudar matemática." }) is null);
        Require(catalog.CreateContext(new AiAssistantInput { ObjectiveOrExam = "ENEM 2027" }) is not null);
        Require(catalog.CreateContext(new AiAssistantInput { ObjectiveOrExam = "enem" }) is not null);
        Require(catalog.CreateContext(new AiAssistantInput { ObjectiveOrExam = "cinema" }) is null);
    }

    private static void ContextMapsEmphasis()
    {
        var context = EnemCatalogService.Default.CreateContext(new AiAssistantInput
        {
            ObjectiveOrExam = "ENEM",
            WeakSubjects = new List<string> { "fisica", "redação" },
            StrongSubjects = new List<string> { "História" },
            FreeText = "Também quero melhorar geopolítica."
        })!;
        AiContractValidator.ValidateEnemCatalogContext(context);
        var subjects = context.Areas.SelectMany(area => area.Subjects).Append(context.Writing).ToList();
        Equal("weak", subjects.Single(subject => subject.Name == "Física").UserEmphasis);
        Equal("weak", context.Writing.UserEmphasis);
        Equal("strong", subjects.Single(subject => subject.Name == "História").UserEmphasis);
        Equal("mentioned", subjects.Single(subject => subject.Name == "Geografia").UserEmphasis);
        Require(subjects.Count <= 16);
        Require(subjects.Sum(subject => subject.Contents.Count) <= 60);
        Require(context.Basis.Contains("curadoria do Rota", StringComparison.Ordinal));
        Require(!context.Basis.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    private static void ContextRejectsTampering()
    {
        var context = EnemCatalogService.Default.CreateContext(new AiAssistantInput { ObjectiveOrExam = "ENEM" })!;
        var tampered = context with
        {
            Areas = context.Areas.Take(3).ToList()
        };
        Expect<AiContractValidationException>(() => AiContractValidator.ValidateEnemCatalogContext(tampered));
    }

    private static void ProposalAcceptsCatalogValues() =>
        EnemProposalValidator.Validate(
            Proposal("Matemática", "Álgebra, funções, equações e gráficos"),
            Context());

    private static void ProposalRejectsInventedSubject()
    {
        Expect<AiContractValidationException>(() => EnemProposalValidator.Validate(
            Proposal("Astrologia", "Mapa astral"), Context()));
        Expect<AiContractValidationException>(() => EnemProposalValidator.Validate(new AiProposal
        {
            Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000018"),
            CreatedAtUtc = new DateTimeOffset(2031, 1, 1, 12, 0, 0, TimeSpan.Zero),
            Summary = "Alteração ENEM.",
            Kind = AiProposalKind.PlanChanges,
            Status = AiProposalStatus.Pending,
            Changes = new AiPlanChangeDraft
            {
                Operations = new List<AiPlanOperation>
                {
                    new()
                    {
                        Id = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000018"),
                        Type = AiPlanOperationType.ChangeSubjectPriority,
                        Summary = "Priorizar matéria inventada.",
                        Subject = "Astrologia",
                        Priority = 100
                    }
                }
            }
        }, Context()));
    }

    private static void ProposalRejectsInventedContent() =>
        Expect<AiContractValidationException>(() => EnemProposalValidator.Validate(
            Proposal("Matemática", "Conteúdo imaginário"), Context()));

    private static void PlanningUsesCatalog()
    {
        var backend = new CaptureBackend(Proposal("Matemática", "Álgebra, funções, equações e gráficos"));
        var service = new AiPlanningService(
            backend,
            new StaticConfigurationStore(),
            enemCatalogProvider: EnemCatalogService.Default);
        var generation = service.CreateGenerationAsync(
            new AiAssistantInput { ObjectiveOrExam = "ENEM" },
            AiProposalKind.StudyPlan).GetAwaiter().GetResult();
        Require(backend.Context is not null);
        Require(ReferenceEquals(backend.Context, generation.EnemCatalogContext));
        Equal(EnemCatalogService.CurrentCatalogVersion, generation.EnemCatalogContext!.CatalogVersion);

        backend.Proposal = Proposal("Astrologia", "Mapa astral");
        Expect<AiPlanningException>(() => service.CreateGenerationAsync(
            new AiAssistantInput { ObjectiveOrExam = "ENEM" },
            AiProposalKind.StudyPlan).GetAwaiter().GetResult());
    }

    private static AiEnemCatalogContext Context() =>
        EnemCatalogService.Default.CreateContext(new AiAssistantInput { ObjectiveOrExam = "ENEM" })!;

    private static AiProposal Proposal(string subject, string topic) => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000017"),
        CreatedAtUtc = new DateTimeOffset(2031, 1, 1, 12, 0, 0, TimeSpan.Zero),
        Summary = "Plano ENEM.",
        Kind = AiProposalKind.StudyPlan,
        Status = AiProposalStatus.Pending,
        StudyPlan = new AiStudyPlanDraft
        {
            StudyPlanJson = $$"""
                {
                  "format":"studyplan",
                  "format_version":"0.2",
                  "plan":{"id":"enem-plan","revision":1,"title":"Plano ENEM"},
                  "objective":{"name":"ENEM","date":"2031-11-09"},
                  "sessions":[{
                    "id":"session-1",
                    "date":"2031-02-10",
                    "subject":"{{subject}}",
                    "topic":"{{topic}}",
                    "minutes":60,
                    "target":"Resolver exercícios e revisar erros.",
                    "kind":"study"
                  }]
                }
                """
        }
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class StaticConfigurationStore : IAiConfigurationStore
    {
        public string ConfigurationPath => Path.Combine(Path.GetTempPath(), "RotaEnemCatalog", "config.json");
        public string LastLoadWarning => "";
        public Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ModelId = "qwen3-4b-q4-k-m",
            ModelPath = Path.Combine(Path.GetTempPath(), "RotaEnemCatalog", "model.gguf"),
            RuntimePath = Path.Combine(Path.GetTempPath(), "RotaEnemCatalog", "llama-server.exe"),
            InstallationState = AiInstallationState.Ready
        });
        public Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CaptureBackend : ILocalAiBackend
    {
        public CaptureBackend(AiProposal proposal) => Proposal = proposal;
        public AiProposal Proposal { get; set; }
        public AiEnemCatalogContext? Context { get; private set; }

        public Task<AiProposal> CreateProposalAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            AiConfiguration configuration,
            AiPlanningContext? planningContext = null,
            CancellationToken cancellationToken = default) => Task.FromResult(Proposal);

        public Task<AiProposal> CreateProposalAsync(
            AiAssistantInput input,
            AiProposalKind kind,
            AiConfiguration configuration,
            AiPlanningContext? planningContext,
            AiConversationContext? conversationContext,
            AiEnemCatalogContext? enemCatalogContext,
            CancellationToken cancellationToken)
        {
            Context = enemCatalogContext;
            return Task.FromResult(Proposal);
        }
    }
}
