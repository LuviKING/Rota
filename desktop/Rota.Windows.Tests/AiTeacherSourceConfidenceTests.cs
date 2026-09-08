using Rota.Desktop.LocalAI;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Rota.Desktop.Tests;

public static class AiTeacherSourceConfidenceTests
{
    private const string ApiKey = "abababababababababababababababababababababababababababababababab";

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher grounding exposes deterministic internal sources", GroundingExposesDeterministicSources),
        ("Teacher grounding confidence is high only for a complete verified slice", CompleteContextHasHighConfidence),
        ("Teacher grounding confidence drops when verified context is truncated", TruncatedContextHasMediumConfidence),
        ("Teacher grounding confidence is limited without verified theory", MissingTheoryHasLimitedConfidence),
        ("Teacher service attaches grounding after inference", ServiceAttachesGroundingAfterInference),
        ("Teacher model cannot inject sources or confidence metadata", BackendRejectsModelControlledGrounding)
    };

    private static void GroundingExposesDeterministicSources()
    {
        var context = Context();
        var grounding = AiTeacherGroundingMetadataFactory.Create(context);

        Require(grounding.Sources.Count == 4);
        Require(grounding.Sources[0] == new AiTeacherSourceReference
        {
            Kind = AiTeacherSourceKind.Content,
            Id = "razao",
            Title = "Razão",
            ParentContentId = ""
        });
        Require(grounding.Sources[1] == new AiTeacherSourceReference
        {
            Kind = AiTeacherSourceKind.TheoryMaterial,
            Id = "teoria-razao",
            Title = "Razão no pacote",
            ParentContentId = "razao"
        });
        Require(grounding.Sources[2].Kind == AiTeacherSourceKind.PrerequisiteReference);
        Require(grounding.Sources[2].Id == "fracoes");
        Require(grounding.Sources[3].Id == "divisao");
        Require(AiTeacherGroundingMetadataFactory.GetSourceKindLabel(grounding.Sources[1].Kind) == "Material teórico");
    }

    private static void CompleteContextHasHighConfidence()
    {
        var grounding = AiTeacherGroundingMetadataFactory.Create(Context());
        Require(grounding.Confidence == AiTeacherGroundingConfidence.High);
        Require(AiTeacherGroundingMetadataFactory.GetConfidenceLabel(grounding.Confidence) == "Alta");
        Require(grounding.ConfidenceReason.Contains("sem truncamento", StringComparison.Ordinal));
    }

    private static void TruncatedContextHasMediumConfidence()
    {
        var baseContext = Context();
        var context = baseContext with
        {
            HasMorePrerequisites = true,
            Theory = baseContext.Theory! with { HasMoreSections = true }
        };
        var grounding = AiTeacherGroundingMetadataFactory.Create(context);

        Require(grounding.Confidence == AiTeacherGroundingConfidence.Medium);
        Require(AiTeacherGroundingMetadataFactory.GetConfidenceLabel(grounding.Confidence) == "Média");
        Require(grounding.ConfidenceReason.Contains("recorte foi limitada", StringComparison.Ordinal));
    }

    private static void MissingTheoryHasLimitedConfidence()
    {
        var grounding = AiTeacherGroundingMetadataFactory.Create(Context() with { Theory = null });
        Require(grounding.Confidence == AiTeacherGroundingConfidence.Limited);
        Require(AiTeacherGroundingMetadataFactory.GetConfidenceLabel(grounding.Confidence) == "Limitada");
        Require(grounding.Sources.All(item => item.Kind != AiTeacherSourceKind.TheoryMaterial));

        var unavailable = AiTeacherGroundingMetadataFactory.Create(null);
        Require(unavailable.Confidence == AiTeacherGroundingConfidence.Unavailable);
        Require(unavailable.Sources.Count == 0);
    }

    private static void ServiceAttachesGroundingAfterInference()
    {
        var descriptor = new AiModelDescriptor(
            "qwen3-4b-q4-k-m", "Qwen3 4B", AiProfile.Balanced, "4B", "Q4_K_M", "qwen3-4b.gguf", 8192, true);
        var configuration = new AiConfiguration
        {
            Profile = AiProfile.Balanced,
            ModelId = descriptor.Id,
            RuntimePath = Path.Combine(Path.GetTempPath(), "teacher-source-tests", "llama-server.exe"),
            ModelPath = Path.Combine(Path.GetTempPath(), "teacher-source-tests", "qwen3-4b.gguf"),
            ContextSize = 8192,
            ComputePreference = AiComputePreference.Cpu,
            InstallationState = AiInstallationState.Ready
        };
        var manager = new FakeModelManager(new AiModelInstallationInfo(
            AiInstallationState.Ready,
            AiProfile.Balanced,
            descriptor,
            configuration.RuntimePath,
            configuration.ModelPath,
            true,
            true,
            Array.Empty<string>()));
        var backend = new CapturingBackend();
        var service = new AiTeacherService(
            backend,
            new FakeConfigurationStore(configuration),
            manager,
            requireVerifiedLessonContext: true);
        var request = new AiTeacherRequest
        {
            Question = "Explique razão usando o material interno.",
            LessonContext = Context()
        };

        var result = service.ExplainWithGroundingAsync(request).GetAwaiter().GetResult();

        Require(backend.CallCount == 1);
        Require(manager.CallCount == 1);
        Require(result.Answer.Title == "Resposta do backend");
        Require(result.Grounding.Confidence == AiTeacherGroundingConfidence.High);
        Require(result.Grounding.Sources.Any(item => item.Id == "teoria-razao"));
        Require(result.Grounding.Sources.Any(item => item.Id == "divisao"));
    }

    private static void BackendRejectsModelControlledGrounding()
    {
        var generated = "{\"title\":\"Razão\",\"introduction\":\"Vamos usar o material.\",\"steps\":[{\"number\":1,\"title\":\"Compare\",\"explanation\":\"Razão compara quantidades.\"}],\"recap\":\"Compare as quantidades.\",\"limitations\":[],\"hidden_doubts\":[],\"grounding_confidence\":\"high\",\"sources\":[\"internet\"]}";
        using var client = new HttpClient(new CompletionHandler(CompletionResponse(generated)))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var backend = new LlamaTeacherBackend(new FakeRuntimeHost(), AiTeacherManual.Current, client);

        Expect<AiInferenceException>(() => backend.ExplainAsync(new AiTeacherRequest
        {
            Question = "Explique razão.",
            LessonContext = Context()
        }, Configuration()).GetAwaiter().GetResult());
    }

    private static AiTeacherLessonContext Context() => new()
    {
        ContentId = "razao",
        ContentTitle = "Razão",
        ContentSummary = "Comparação entre duas quantidades.",
        Prerequisites = new()
        {
            new AiTeacherPrerequisiteContext { ContentId = "fracoes", Title = "Frações" },
            new AiTeacherPrerequisiteContext { ContentId = "divisao", Title = "Divisão" }
        },
        Theory = new AiTeacherTheoryContext
        {
            MaterialId = "teoria-razao",
            Title = "Razão no pacote",
            LearningGoal = "Compreender razão como comparação.",
            Sections = new()
            {
                new AiTeacherTheorySectionContext
                {
                    Kind = LearningTheorySectionKinds.Explanation,
                    Title = "Conceito",
                    Body = "Uma razão compara duas quantidades por meio de uma divisão.",
                    Position = 1
                }
            }
        }
    };

    private static AiConfiguration Configuration() => new()
    {
        Profile = AiProfile.Balanced,
        ModelId = "qwen3-4b-q4-k-m",
        RuntimePath = Path.Combine(Path.GetTempPath(), "teacher-source-tests", "llama-server.exe"),
        ModelPath = Path.Combine(Path.GetTempPath(), "teacher-source-tests", "qwen3-4b.gguf"),
        ContextSize = 8192,
        ComputePreference = AiComputePreference.Cpu,
        InstallationState = AiInstallationState.Ready
    };

    private static string CompletionResponse(string generated) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new { message = new { content = generated }, finish_reason = "stop" }
        }
    });

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher source/confidence assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class FakeConfigurationStore : IAiConfigurationStore
    {
        private AiConfiguration _configuration;
        public FakeConfigurationStore(AiConfiguration configuration) => _configuration = configuration;
        public string ConfigurationPath => Path.Combine(Path.GetTempPath(), "teacher-source-config.json");
        public string LastLoadWarning => "";

        public Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_configuration);
        }

        public Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _configuration = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModelManager : IAiModelManager
    {
        private readonly AiModelInstallationInfo _installation;
        public FakeModelManager(AiModelInstallationInfo installation) => _installation = installation;
        public int CallCount { get; private set; }

        public Task<AiModelInstallationInfo> GetInstallationInfoAsync(
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_installation);
        }
    }

    private sealed class CapturingBackend : IAiTeacherBackend
    {
        public int CallCount { get; private set; }

        public Task<AiTeacherAnswer> ExplainAsync(
            AiTeacherRequest request,
            AiConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new AiTeacherAnswer
            {
                Title = "Resposta do backend",
                Introduction = "Introdução",
                Steps = new[]
                {
                    new AiTeacherStep { Number = 1, Title = "Passo", Explanation = "Explicação." }
                },
                Recap = "Resumo",
                ManualId = AiTeacherManual.Current.ManualId,
                ManualVersion = AiTeacherManual.Current.ManualVersion,
                ManualFingerprintSha256 = AiTeacherManual.Current.FingerprintSha256,
                UsedLessonContext = request.LessonContext is not null,
                ContentId = request.LessonContext?.ContentId ?? ""
            });
        }
    }

    private sealed class FakeRuntimeHost : ILocalAiRuntimeHost
    {
        public AiRuntimeStatus Status { get; private set; } =
            new(AiRuntimeState.Stopped, null, null, null, null, "Parado");
        public AiRuntimeConnection? Connection { get; } =
            new(new Uri("http://127.0.0.1:54328/"), ApiKey);

        public Task<AiRuntimeStatus> StartAsync(AiConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Status = new AiRuntimeStatus(
                AiRuntimeState.Ready,
                Connection?.Endpoint,
                49,
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

    private sealed class CompletionHandler : HttpMessageHandler
    {
        private readonly string _body;
        public CompletionHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}

