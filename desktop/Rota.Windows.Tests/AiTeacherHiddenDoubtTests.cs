using Rota.Desktop.LocalAI;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Rota.Desktop.Tests;

public static class AiTeacherHiddenDoubtTests
{
    private const string ApiKey = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string Question = "Eu sei fazer 6:3, mas não entendo o que a razão compara nem por que 6:3 vira 2.";

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher hidden doubt taxonomy is stable and closed", TaxonomyIsStable),
        ("Teacher maps question-anchored hidden doubts and trusted prerequisites", BackendMapsGroundedDoubts),
        ("Teacher rejects a hidden doubt with invented question evidence", BackendRejectsInventedQuestionSignal),
        ("Teacher rejects a hidden doubt with an untrusted prerequisite id", BackendRejectsUntrustedPrerequisite),
        ("Teacher rejects a hidden doubt that points to a missing explanation step", BackendRejectsMissingAddressStep),
        ("Teacher hidden doubt contracts reject unsupported duplicate and unbound diagnostics", ContractsRejectUnsafeDiagnostics),
        ("Teacher hidden doubt contracts cap diagnostics at three", ContractsCapDiagnostics),
        ("Teacher rejects generated answers that omit the hidden doubt field", BackendRequiresHiddenDoubtField),
        ("Teacher hidden doubt policy is trusted bounded and non-profiling", BackendSendsTrustedDiagnosticPolicy)
    };

    private static void TaxonomyIsStable()
    {
        var kinds = AiTeacherHiddenDoubtKinds.All;
        Require(kinds.Count == 4);
        Require(kinds.Select(item => item.Id).SequenceEqual(new[]
        {
            AiTeacherHiddenDoubtKinds.PrerequisiteId,
            AiTeacherHiddenDoubtKinds.ConceptConfusionId,
            AiTeacherHiddenDoubtKinds.NotationOrVocabularyId,
            AiTeacherHiddenDoubtKinds.ProceduralReasoningId
        }));
        Require(kinds.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == kinds.Count);
        Require(kinds.Select(item => item.Kind).Distinct().Count() == kinds.Count);

        foreach (var descriptor in kinds)
        {
            Require(AiTeacherHiddenDoubtKinds.IsSupported(descriptor.Kind));
            Require(ReferenceEquals(AiTeacherHiddenDoubtKinds.Get(descriptor.Kind), descriptor));
            Require(AiTeacherHiddenDoubtKinds.GetId(descriptor.Kind) == descriptor.Id);
            Require(AiTeacherHiddenDoubtKinds.TryParseId(descriptor.Id, out var parsed));
            Require(parsed == descriptor.Kind);
            Require(!string.IsNullOrWhiteSpace(descriptor.DisplayName));
            Require(!string.IsNullOrWhiteSpace(descriptor.Description));
            Require(descriptor.Id.Length <= 32);
            Require(descriptor.Description.Length <= 300);
        }

        Require(!AiTeacherHiddenDoubtKinds.IsSupported((AiTeacherHiddenDoubtKind)99));
        Require(!AiTeacherHiddenDoubtKinds.TryParseId("invented", out _));
        Expect<ArgumentOutOfRangeException>(() => AiTeacherHiddenDoubtKinds.Get((AiTeacherHiddenDoubtKind)99));
    }

    private static void BackendMapsGroundedDoubts()
    {
        var result = Run(ValidGeneratedAnswer());
        Require(result.Answer.HiddenDoubts.Count == 2);

        var concept = result.Answer.HiddenDoubts[0];
        Require(concept.Kind == AiTeacherHiddenDoubtKind.ConceptConfusion);
        Require(concept.QuestionSignal == "não entendo o que a razão compara");
        Require(concept.PrerequisiteContentId.Length == 0);
        Require(concept.AddressedInStep == 1);

        var procedure = result.Answer.HiddenDoubts[1];
        Require(procedure.Kind == AiTeacherHiddenDoubtKind.ProceduralReasoning);
        Require(procedure.QuestionSignal == "por que 6:3 vira 2");
        Require(procedure.PrerequisiteContentId == "divisao");
        Require(procedure.AddressedInStep == 2);

        AiTeacherContractValidator.ValidateAnswer(result.Answer, AiTeacherManual.Current, Request());
    }

    private static void BackendRejectsInventedQuestionSignal()
    {
        var generated = ValidGeneratedAnswer().Replace(
            "não entendo o que a razão compara",
            "eu nunca aprendi razão",
            StringComparison.Ordinal);
        Expect<AiInferenceException>(() => Run(generated));
    }

    private static void BackendRejectsUntrustedPrerequisite()
    {
        var generated = ValidGeneratedAnswer().Replace(
            "\"prerequisite_content_id\":\"divisao\"",
            "\"prerequisite_content_id\":\"equacoes\"",
            StringComparison.Ordinal);
        Expect<AiInferenceException>(() => Run(generated));
    }

    private static void BackendRejectsMissingAddressStep()
    {
        var generated = ValidGeneratedAnswer().Replace(
            "\"addressed_in_step\":2",
            "\"addressed_in_step\":3",
            StringComparison.Ordinal);
        Expect<AiInferenceException>(() => Run(generated));
    }

    private static void ContractsRejectUnsafeDiagnostics()
    {
        var request = Request();
        var valid = new AiTeacherHiddenDoubt
        {
            Kind = AiTeacherHiddenDoubtKind.ConceptConfusion,
            Summary = "Separar razão de uma conta isolada",
            QuestionSignal = "não entendo o que a razão compara",
            Reason = "O trecho pede o significado da comparação antes do cálculo.",
            AddressedInStep = 1
        };

        var unsupported = valid with
        {
            Kind = (AiTeacherHiddenDoubtKind)99,
            Summary = "Categoria inválida"
        };
        Expect<AiContractValidationException>(() => AiTeacherContractValidator.ValidateAnswer(
            Answer(request, unsupported), AiTeacherManual.Current, request));

        var duplicate = valid with { AddressedInStep = 2 };
        Expect<AiContractValidationException>(() => AiTeacherContractValidator.ValidateAnswer(
            Answer(request, valid, duplicate), AiTeacherManual.Current, request));

        Expect<AiContractValidationException>(() => AiTeacherContractValidator.ValidateAnswer(
            Answer(request, valid), AiTeacherManual.Current));
    }

    private static void ContractsCapDiagnostics()
    {
        var request = Request();
        var doubts = Enumerable.Range(1, 4)
            .Select(index => new AiTeacherHiddenDoubt
            {
                Kind = AiTeacherHiddenDoubtKind.ConceptConfusion,
                Summary = $"Hipótese {index}",
                QuestionSignal = "6:3",
                Reason = "Sinal concreto da pergunta atual.",
                AddressedInStep = index % 2 + 1
            })
            .ToArray();

        Expect<AiContractValidationException>(() => AiTeacherContractValidator.ValidateAnswer(
            Answer(request, doubts), AiTeacherManual.Current, request));
    }

    private static void BackendRequiresHiddenDoubtField()
    {
        var generated = ValidGeneratedAnswer().Replace(
            ",\"hidden_doubts\":[{\"kind\":\"concept_confusion\",\"summary\":\"Separar razão de uma conta isolada\",\"question_signal\":\"não entendo o que a razão compara\",\"reason\":\"A pergunta pede o significado da comparação antes do cálculo.\",\"prerequisite_content_id\":\"\",\"addressed_in_step\":1},{\"kind\":\"procedural_reasoning\",\"summary\":\"Entender por que a divisão produz dois\",\"question_signal\":\"por que 6:3 vira 2\",\"reason\":\"A pergunta solicita a justificativa do resultado, não apenas a regra mecânica.\",\"prerequisite_content_id\":\"divisao\",\"addressed_in_step\":2}]",
            "",
            StringComparison.Ordinal);
        Expect<AiInferenceException>(() => Run(generated));
    }

    private static void BackendSendsTrustedDiagnosticPolicy()
    {
        var result = Run(ValidGeneratedAnswer());
        using var document = JsonDocument.Parse(result.RequestBody);
        var root = document.RootElement;
        var schema = root.GetProperty("json_schema");
        var hidden = schema.GetProperty("properties").GetProperty("hidden_doubts");
        Require(hidden.GetProperty("maxItems").GetInt32() == AiTeacherContractValidator.MaximumHiddenDoubts);

        var kindEnum = hidden.GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        Require(kindEnum.SequenceEqual(AiTeacherHiddenDoubtKinds.All.Select(item => item.Id)));

        var messages = root.GetProperty("messages");
        var system = messages[0].GetProperty("content").GetString()!;
        var user = messages[1].GetProperty("content").GetString()!;
        Require(system.Contains("[hidden-doubt-diagnostic-protocol]", StringComparison.Ordinal));
        Require(system.Contains("hipóteses pedagógicas locais à pergunta", StringComparison.Ordinal));
        Require(system.Contains("Não conclua que o aluno é desatento", StringComparison.Ordinal));
        Require(system.Contains("question_signal", StringComparison.Ordinal));
        Require(!system.Contains(Question, StringComparison.Ordinal));

        using var payload = JsonDocument.Parse(user);
        Require(payload.RootElement.GetProperty("user_payload").GetProperty("question").GetString() == Question);
    }

    private static AiTeacherAnswer Answer(AiTeacherRequest request, params AiTeacherHiddenDoubt[] doubts) => new()
    {
        Title = "Razão com significado",
        Introduction = "Vamos separar o significado da razão e o cálculo.",
        Steps = new[]
        {
            new AiTeacherStep
            {
                Number = 1,
                Title = "Entenda a comparação",
                Explanation = "A razão 6:3 compara seis unidades com grupos de três."
            },
            new AiTeacherStep
            {
                Number = 2,
                Title = "Justifique a divisão",
                Explanation = "Dois grupos de três formam seis, então 6 dividido por 3 resulta em 2."
            }
        },
        Recap = "Razão expressa uma comparação e a divisão calcula quantos grupos cabem.",
        Limitations = Array.Empty<string>(),
        HiddenDoubts = doubts,
        ExplanationStyle = request.ExplanationStyle,
        ManualId = AiTeacherManual.Current.ManualId,
        ManualVersion = AiTeacherManual.Current.ManualVersion,
        ManualFingerprintSha256 = AiTeacherManual.Current.FingerprintSha256,
        UsedLessonContext = request.LessonContext is not null,
        ContentId = request.LessonContext?.ContentId ?? ""
    };

    private static (AiTeacherAnswer Answer, string RequestBody) Run(string generated)
    {
        var handler = new CaptureHandler(CompletionResponse(generated));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var backend = new LlamaTeacherBackend(new FakeRuntimeHost(), AiTeacherManual.Current, client);
        var answer = backend.ExplainAsync(Request(), Configuration()).GetAwaiter().GetResult();
        var requestBody = handler.RequestBody ?? throw new InvalidOperationException("Expected a captured teacher request.");
        return (answer, requestBody);
    }

    private static AiTeacherRequest Request() => new()
    {
        Question = Question,
        ExplanationStyle = AiTeacherExplanationStyle.StepByStep,
        LessonContext = new AiTeacherLessonContext
        {
            ContentId = "razao",
            ContentTitle = "Razão",
            ContentSummary = "Comparação entre duas quantidades por meio de uma divisão.",
            Prerequisites = new()
            {
                new AiTeacherPrerequisiteContext
                {
                    ContentId = "divisao",
                    Title = "Divisão"
                }
            }
        }
    };

    private static AiConfiguration Configuration() => new()
    {
        Profile = AiProfile.Balanced,
        ModelId = "qwen3-4b-q4-k-m",
        RuntimePath = Path.Combine(Path.GetTempPath(), "teacher-hidden-doubt-tests", "llama-server.exe"),
        ModelPath = Path.Combine(Path.GetTempPath(), "teacher-hidden-doubt-tests", "qwen3-4b.gguf"),
        ContextSize = 8192,
        ComputePreference = AiComputePreference.Cpu,
        InstallationState = AiInstallationState.Ready
    };

    private static string ValidGeneratedAnswer() =>
        "{\"title\":\"Razão com significado\",\"introduction\":\"Vamos separar o significado da razão e o cálculo.\",\"steps\":[" +
        "{\"number\":1,\"title\":\"Entenda a comparação\",\"explanation\":\"A razão 6:3 compara seis unidades com grupos de três.\"}," +
        "{\"number\":2,\"title\":\"Justifique a divisão\",\"explanation\":\"Dois grupos de três formam seis, então 6 dividido por 3 resulta em 2.\"}]," +
        "\"recap\":\"Razão expressa uma comparação e a divisão calcula quantos grupos cabem.\",\"limitations\":[]," +
        "\"hidden_doubts\":[" +
        "{\"kind\":\"concept_confusion\",\"summary\":\"Separar razão de uma conta isolada\",\"question_signal\":\"não entendo o que a razão compara\",\"reason\":\"A pergunta pede o significado da comparação antes do cálculo.\",\"prerequisite_content_id\":\"\",\"addressed_in_step\":1}," +
        "{\"kind\":\"procedural_reasoning\",\"summary\":\"Entender por que a divisão produz dois\",\"question_signal\":\"por que 6:3 vira 2\",\"reason\":\"A pergunta solicita a justificativa do resultado, não apenas a regra mecânica.\",\"prerequisite_content_id\":\"divisao\",\"addressed_in_step\":2}]}";

    private static string CompletionResponse(string generated) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new
            {
                message = new { content = generated },
                finish_reason = "stop"
            }
        }
    });

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher hidden doubt assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class FakeRuntimeHost : ILocalAiRuntimeHost
    {
        public AiRuntimeStatus Status { get; private set; } =
            new(AiRuntimeState.Stopped, null, null, null, null, "Parado");

        public AiRuntimeConnection? Connection { get; } =
            new(new Uri("http://127.0.0.1:54323/"), ApiKey);

        public Task<AiRuntimeStatus> StartAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoint = Connection?.Endpoint ?? throw new InvalidOperationException("Expected a local test connection.");
            Status = new AiRuntimeStatus(
                AiRuntimeState.Ready,
                endpoint,
                44,
                configuration.Profile,
                configuration.ComputePreference,
                "Pronto");
            return Task.FromResult(Status);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Status = new AiRuntimeStatus(AiRuntimeState.Stopped, null, null, null, null, "Parado");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _body;

        public CaptureHandler(string body) => _body = body;

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}

