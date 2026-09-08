using Rota.Desktop;
using Rota.Desktop.LocalAI;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Rota.Desktop.Tests;

public static class AiTeacherPrerequisiteDetectionTests
{
    private const string ApiKey = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string Question = "Eu entendo a equação, mas não lembro como dividir números inteiros para continuar.";

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher canonical context expands a verified prerequisite chain", CanonicalContextExpandsVerifiedChain),
        ("Teacher prerequisite chain is bounded by depth", PrerequisiteChainIsDepthBounded),
        ("Teacher prerequisite chain is bounded by item count", PrerequisiteChainIsCountBounded),
        ("Teacher can detect an indirect prerequisite only from the verified graph", BackendAcceptsVerifiedIndirectPrerequisite),
        ("Teacher rejects an invented prerequisite outside the verified graph", BackendRejectsInventedPrerequisite),
        ("Teacher prerequisite context still excludes practice questions and answer keys", PrerequisiteContextExcludesQuestions)
    };

    private static void CanonicalContextExpandsVerifiedChain()
    {
        var package = ChainPackage("equacoes", "algebra-basica", "operacoes", "divisao");
        var context = AiTeacherLessonContextFactory.Create(package, "equacoes");

        Require(context.Prerequisites.Select(item => item.ContentId).SequenceEqual(new[]
        {
            "algebra-basica",
            "operacoes",
            "divisao"
        }));
        Require(!context.HasMorePrerequisites);
        Require(context.Prerequisites.Select(item => item.ContentId).Distinct(StringComparer.Ordinal).Count() == 3);
        AiTeacherContractValidator.ValidateLessonContext(context);
    }

    private static void PrerequisiteChainIsDepthBounded()
    {
        var package = ChainPackage("nivel-5", "nivel-4", "nivel-3", "nivel-2", "nivel-1");
        var context = AiTeacherLessonContextFactory.Create(package, "nivel-5");

        Require(context.Prerequisites.Select(item => item.ContentId).SequenceEqual(new[]
        {
            "nivel-4",
            "nivel-3",
            "nivel-2"
        }));
        Require(context.HasMorePrerequisites);
        Require(AiTeacherLessonContextFactory.MaximumPrerequisiteDepth == 3);
    }

    private static void PrerequisiteChainIsCountBounded()
    {
        var contentIds = Enumerable.Range(1, 14).Select(index => $"base-{index:00}").ToArray();
        var package = FlatPackage("avancado", contentIds);
        var context = AiTeacherLessonContextFactory.Create(package, "avancado");

        Require(context.Prerequisites.Count == AiTeacherContractValidator.MaximumPrerequisites);
        Require(context.HasMorePrerequisites);
        Require(context.Prerequisites.Select(item => item.ContentId).SequenceEqual(contentIds.Take(12)));
    }

    private static void BackendAcceptsVerifiedIndirectPrerequisite()
    {
        var package = ChainPackage("equacoes", "algebra-basica", "operacoes", "divisao");
        var context = AiTeacherLessonContextFactory.Create(package, "equacoes");
        var request = new AiTeacherRequest
        {
            Question = Question,
            LessonContext = context
        };
        var result = Run(request, GeneratedAnswer("divisao"));

        Require(result.Answer.HiddenDoubts.Count == 1);
        var doubt = result.Answer.HiddenDoubts.Single();
        Require(doubt.Kind == AiTeacherHiddenDoubtKind.Prerequisite);
        Require(doubt.PrerequisiteContentId == "divisao");
        Require(doubt.QuestionSignal == "não lembro como dividir números inteiros");
        AiTeacherContractValidator.ValidateAnswer(result.Answer, AiTeacherManual.Current, request);

        using var wire = JsonDocument.Parse(result.RequestBody);
        var user = wire.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        using var payload = JsonDocument.Parse(user);
        var ids = payload.RootElement.GetProperty("lesson_context").GetProperty("prerequisites")
            .EnumerateArray().Select(item => item.GetProperty("content_id").GetString()).ToArray();
        Require(ids.SequenceEqual(new[] { "algebra-basica", "operacoes", "divisao" }));
    }

    private static void BackendRejectsInventedPrerequisite()
    {
        var package = ChainPackage("equacoes", "algebra-basica", "operacoes", "divisao");
        var request = new AiTeacherRequest
        {
            Question = Question,
            LessonContext = AiTeacherLessonContextFactory.Create(package, "equacoes")
        };

        Expect<AiInferenceException>(() => Run(request, GeneratedAnswer("geometria")));
    }

    private static void PrerequisiteContextExcludesQuestions()
    {
        var package = ChainPackage("equacoes", "algebra-basica", "operacoes", "divisao");
        package.PracticeQuestions.Add(new LearningPracticeQuestion
        {
            Id = "questao-secreta",
            ContentId = "equacoes",
            Prompt = "Qual é o resultado final da equação?",
            Options = new()
            {
                new() { Id = "opcao-a", Text = "Resposta A" },
                new() { Id = "opcao-b", Text = "Resposta B" }
            },
            CorrectOptionId = "opcao-b",
            Explanation = "Explicação do gabarito.",
            Difficulty = LearningPracticeDifficulties.Basic
        });

        var context = AiTeacherLessonContextFactory.Create(package, "equacoes");
        var result = Run(new AiTeacherRequest
        {
            Question = Question,
            LessonContext = context
        }, GeneratedAnswer("divisao"));

        Require(!result.RequestBody.Contains("questao-secreta", StringComparison.Ordinal));
        Require(!result.RequestBody.Contains("Qual é o resultado final", StringComparison.Ordinal));
        Require(!result.RequestBody.Contains("opcao-b", StringComparison.Ordinal));
        Require(!result.RequestBody.Contains("Explicação do gabarito", StringComparison.Ordinal));
    }

    private static LearningContentPackage ChainPackage(params string[] ids)
    {
        if (ids.Length < 2) throw new ArgumentException("A prerequisite chain needs at least two contents.", nameof(ids));
        var package = BasePackage(ids);
        for (var index = 0; index < ids.Length - 1; index++)
        {
            package.Catalog.Prerequisites.Add(new LearningPrerequisite
            {
                ContentId = ids[index],
                RequiredContentId = ids[index + 1]
            });
        }
        return package;
    }

    private static LearningContentPackage FlatPackage(string targetId, IReadOnlyList<string> prerequisiteIds)
    {
        var all = new[] { targetId }.Concat(prerequisiteIds).ToArray();
        var package = BasePackage(all);
        foreach (var id in prerequisiteIds)
        {
            package.Catalog.Prerequisites.Add(new LearningPrerequisite
            {
                ContentId = targetId,
                RequiredContentId = id
            });
        }
        return package;
    }

    private static LearningContentPackage BasePackage(IReadOnlyList<string> contentIds)
    {
        var contents = contentIds.Select((id, index) => new LearningContent
        {
            Id = id,
            LessonId = "aula-prerequisitos",
            Title = id.Replace('-', ' '),
            Summary = $"Conteúdo verificado {id}.",
            Position = index + 1
        }).ToList();

        return new LearningContentPackage
        {
            Package = new LearningPackageIdentity
            {
                Id = "pacote-prerequisitos",
                Version = "1.0.0",
                Title = "Pacote de pré-requisitos",
                Locale = "pt-BR"
            },
            Catalog = new LearningCatalog
            {
                Subjects = new() { new LearningSubject { Id = "matematica", Name = "Matemática" } },
                Courses = new()
                {
                    new LearningCourse
                    {
                        Id = "curso-matematica",
                        SubjectId = "matematica",
                        Name = "Matemática",
                        Position = 1
                    }
                },
                Modules = new()
                {
                    new LearningModule
                    {
                        Id = "modulo-prerequisitos",
                        CourseId = "curso-matematica",
                        Name = "Pré-requisitos",
                        Position = 1
                    }
                },
                Skills = new()
                {
                    new LearningSkill
                    {
                        Id = "habilidade-prerequisitos",
                        SubjectId = "matematica",
                        Name = "Raciocínio matemático"
                    }
                },
                Lessons = new()
                {
                    new LearningLesson
                    {
                        Id = "aula-prerequisitos",
                        ModuleId = "modulo-prerequisitos",
                        Name = "Pré-requisitos",
                        Position = 1,
                        SkillIds = new() { "habilidade-prerequisitos" }
                    }
                },
                Contents = contents
            }
        };
    }

    private static (AiTeacherAnswer Answer, string RequestBody) Run(AiTeacherRequest request, string generated)
    {
        var handler = new CaptureHandler(CompletionResponse(generated));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var backend = new LlamaTeacherBackend(new FakeRuntimeHost(), AiTeacherManual.Current, client);
        var answer = backend.ExplainAsync(request, Configuration()).GetAwaiter().GetResult();
        return (answer, handler.RequestBody ?? throw new InvalidOperationException("Expected captured request."));
    }

    private static string GeneratedAnswer(string prerequisiteId) =>
        "{\"title\":\"Confira a base necessária\",\"introduction\":\"Antes de continuar a equação, vamos conferir a operação que aparece no próximo passo.\",\"steps\":[" +
        "{\"number\":1,\"title\":\"Retome a divisão\",\"explanation\":\"Dividir desfaz uma multiplicação quando a mesma operação é aplicada de forma compatível à igualdade.\"}," +
        "{\"number\":2,\"title\":\"Volte à equação\",\"explanation\":\"Com a operação inversa identificada, continue o raciocínio preservando os dois lados da igualdade.\"}]," +
        "\"recap\":\"Revise a divisão necessária e então retome a transformação da equação.\",\"limitations\":[]," +
        "\"hidden_doubts\":[{\"kind\":\"prerequisite\",\"summary\":\"Pode ser necessário retomar divisão antes da equação\",\"question_signal\":\"não lembro como dividir números inteiros\",\"reason\":\"A pergunta sinaliza explicitamente uma dificuldade com uma operação anterior cadastrada no pacote.\",\"prerequisite_content_id\":\"" + prerequisiteId + "\",\"addressed_in_step\":1}]}";

    private static string CompletionResponse(string generated) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new { message = new { content = generated }, finish_reason = "stop" }
        }
    });

    private static AiConfiguration Configuration() => new()
    {
        Profile = AiProfile.Balanced,
        ModelId = "qwen3-4b-q4-k-m",
        RuntimePath = Path.Combine(Path.GetTempPath(), "teacher-prerequisite-tests", "llama-server.exe"),
        ModelPath = Path.Combine(Path.GetTempPath(), "teacher-prerequisite-tests", "qwen3-4b.gguf"),
        ContextSize = 8192,
        ComputePreference = AiComputePreference.Cpu,
        InstallationState = AiInstallationState.Ready
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher prerequisite detection assertion failed.");
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
            new(new Uri("http://127.0.0.1:54326/"), ApiKey);

        public Task<AiRuntimeStatus> StartAsync(AiConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Status = new AiRuntimeStatus(
                AiRuntimeState.Ready,
                Connection?.Endpoint,
                47,
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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

