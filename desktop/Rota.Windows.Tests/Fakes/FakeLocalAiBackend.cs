using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests.Fakes;

internal sealed class FakeLocalAiBackend : ILocalAiBackend
{
    private const string DeterministicStudyPlan = """
        {
          "format": "studyplan",
          "format_version": "0.2",
          "plan": {
            "id": "fake-local-ai-plan",
            "revision": 1,
            "title": "Plano simulado para testes"
          },
          "objective": {
            "name": "Validar a fundação da IA local",
            "date": "2030-12-31"
          },
          "sessions": [
            {
              "id": "fake-session-1",
              "date": "2030-01-02",
              "subject": "Matemática",
              "topic": "Fundamentos",
              "minutes": 60,
              "target": "Resolver 10 questões e revisar os erros",
              "kind": "study"
            }
          ]
        }
        """;

    private static readonly DateTimeOffset FixedTimestamp =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public bool SimulateFailure { get; set; }
    public int CallCount { get; private set; }

    public Task<AiProposal> CreateProposalAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        AiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;

        if (SimulateFailure)
            throw new InvalidOperationException("Falha simulada do backend local.");

        var proposal = kind switch
        {
            AiProposalKind.StudyPlan => new AiProposal
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CreatedAtUtc = FixedTimestamp,
                Summary = "Plano simulado e determinístico.",
                Kind = AiProposalKind.StudyPlan,
                StudyPlan = new AiStudyPlanDraft { StudyPlanJson = DeterministicStudyPlan },
                Warnings = new List<string> { "Resposta simulada: nenhuma inferência foi executada." }
            },
            AiProposalKind.PlanChanges => new AiProposal
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                CreatedAtUtc = FixedTimestamp,
                Summary = "Redistribuição simulada e determinística.",
                Kind = AiProposalKind.PlanChanges,
                Changes = new AiPlanChangeDraft
                {
                    Operations = new List<AiPlanOperation>
                    {
                        new()
                        {
                            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            Type = AiPlanOperationType.RedistributeLoad,
                            Summary = "Redistribuir somente sessões futuras.",
                            MaxHoursPerDay = 3
                        }
                    }
                },
                Warnings = new List<string> { "Resposta simulada: nenhuma alteração foi aplicada." }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        return Task.FromResult(proposal);
    }
}
