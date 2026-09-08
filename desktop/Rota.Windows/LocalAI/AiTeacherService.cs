namespace Rota.Desktop.LocalAI;

/// <summary>
/// Orquestra uma explicação local usando somente a instalação já verificada. Não possui
/// acesso ao repositório de estudos e não participa do fluxo de aplicação de calendário.
/// Em composição de produção, exige contexto derivado de pacote pedagógico verificado
/// antes de sequer resolver ou iniciar o modelo local.
/// </summary>
public sealed class AiTeacherService : IAiTeacherService
{
    private readonly IAiTeacherBackend _backend;
    private readonly IAiConfigurationStore _configurationStore;
    private readonly IAiModelManager _modelManager;
    private readonly AiTeacherManual _manual;

    public AiTeacherService(
        IAiTeacherBackend backend,
        IAiConfigurationStore configurationStore,
        IAiModelManager modelManager,
        bool requireVerifiedLessonContext = false,
        AiTeacherManual? manual = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _manual = manual ?? AiTeacherManual.Current;
        RequireVerifiedLessonContext = requireVerifiedLessonContext;
    }

    /// <summary>
    /// Quando verdadeiro, a inferência é bloqueada se o Rota não fornecer um recorte
    /// de pacote pedagógico com origem e contrato já validados. A composição de produção
    /// mantém esta opção ligada; o valor falso existe apenas para consumidores internos
    /// legados/testes de infraestrutura que exercitam o backend isoladamente.
    /// </summary>
    public bool RequireVerifiedLessonContext { get; }

    public async Task<AiTeacherAnswer> ExplainAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken = default)
    {
        var grounded = await ExplainWithGroundingAsync(request, cancellationToken).ConfigureAwait(false);
        return grounded.Answer;
    }

    /// <summary>
    /// Caminho de apresentação preferido para a Professora Local. A geração continua
    /// idêntica à resposta pedagógica comum, mas as fontes internas e o nível de
    /// confiança do grounding são calculados depois pelo Rota e nunca pelo modelo.
    /// </summary>
    public async Task<AiTeacherGroundedAnswer> ExplainWithGroundingAsync(
        AiTeacherRequest request,
        CancellationToken cancellationToken = default)
    {
        AiTeacherContractValidator.ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (RequireVerifiedLessonContext && request.LessonContext is null)
        {
            throw new AiInferenceException(
                "A Professora Local precisa de um pacote pedagógico verificado para responder nesta experiência.");
        }

        var grounding = AiTeacherGroundingMetadataFactory.Create(request.LessonContext);
        var knowledge = AiTeacherKnowledgeDisclosureFactory.Create(request.LessonContext);
        AiTeacherKnowledgeDisclosureFactory.Validate(knowledge, request.LessonContext);

        // Na experiência de produção, ausência de teoria verificada é uma resposta
        // estruturada, não uma solicitação para que o modelo complete a lacuna. O
        // retorno ocorre antes de configuração, runtime ou inferência.
        if (RequireVerifiedLessonContext && !knowledge.CanAnswerSubstantively)
        {
            var unavailableAnswer = AiTeacherKnowledgeDisclosureFactory
                .CreateInsufficientEvidenceAnswer(request, _manual);
            AiTeacherContractValidator.ValidateAnswer(unavailableAnswer, _manual, request);
            return new AiTeacherGroundedAnswer
            {
                Answer = unavailableAnswer,
                Grounding = grounding,
                Knowledge = knowledge
            };
        }

        // A validação de contrato acima comprova a origem learning_package e todos os
        // limites do contexto antes de qualquer leitura de configuração ou sondagem do modelo.
        var configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        AiContractValidator.ValidateConfiguration(configuration);
        var installation = await _modelManager
            .GetInstallationInfoAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
        if (installation.State != AiInstallationState.Ready)
        {
            throw new AiInferenceException(
                "A professora local precisa de um runtime e de um modelo verificados antes de explicar.");
        }

        // Congela a resolução automática apenas para esta chamada. Assim, runtime/modelo
        // usados na geração são exatamente os que acabaram de ser verificados.
        var resolvedConfiguration = configuration with
        {
            Profile = installation.EffectiveProfile,
            ModelId = installation.Model.Id,
            RuntimePath = installation.RuntimePath,
            ModelPath = installation.ModelPath,
            InstallationState = AiInstallationState.Ready
        };
        AiContractValidator.ValidateConfiguration(resolvedConfiguration);

        var answer = await _backend
            .ExplainAsync(request, resolvedConfiguration, cancellationToken)
            .ConfigureAwait(false);
        return new AiTeacherGroundedAnswer
        {
            Answer = answer,
            Grounding = grounding,
            Knowledge = knowledge
        };
    }
}
