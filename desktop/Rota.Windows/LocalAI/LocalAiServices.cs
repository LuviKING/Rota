namespace Rota.Desktop.LocalAI;

public sealed class LocalAiServices : IAsyncDisposable
{
    private bool _disposed;

    private LocalAiServices(StudyRepository repository, string rootDirectory)
    {
        RootDirectory = rootDirectory;
        TeacherManual = AiTeacherManual.Current;
        HardwareDetector = new WindowsAiHardwareProfileDetector();
        HardwareDiagnostics = new AiHardwareDiagnosticsService(
            HardwareDetector,
            new WindowsAiStorageProbe(rootDirectory));
        ConfigurationStore = new AiConfigurationStore(Path.Combine(rootDirectory, "config.json"));
        ModelManager = new LocalAiModelManager(rootDirectory, AiModelCatalog.Default, HardwareDetector);
        Installer = new LocalAiInstaller(rootDirectory, ConfigurationStore, ModelManager);
        RuntimeHost = new LocalAiRuntimeHost(ModelManager);
        PerformanceDiagnostics = new AiPerformanceDiagnosticsService(
            ConfigurationStore,
            ModelManager,
            RuntimeHost);

        Backend = new LlamaServerBackend(RuntimeHost);
        TeacherBackend = new LlamaTeacherBackend(RuntimeHost, TeacherManual);
        TeacherService = new AiTeacherService(
            TeacherBackend,
            ConfigurationStore,
            ModelManager,
            requireVerifiedLessonContext: true,
            manual: TeacherManual);
        TeacherConversationStore = new AiTeacherConversationStore(
            Path.Combine(rootDirectory, "teacher", "conversations"));
        TeacherLessonSummaryStore = new AiTeacherLessonSummaryStore(
            Path.Combine(rootDirectory, "teacher", "summaries"));
        TeacherLessonSummaryService = new AiTeacherLessonSummaryService(
            TeacherConversationStore,
            TeacherLessonSummaryStore);
        TeacherSubjectBindingStore = new AiTeacherSubjectBindingStore(
            Path.Combine(rootDirectory, "teacher", "memory", "subject-bindings"));
        TeacherSubjectMemoryStore = new AiTeacherSubjectMemoryStore(
            Path.Combine(rootDirectory, "teacher", "memory", "subjects"));
        TeacherSubjectMemoryService = new AiTeacherSubjectMemoryService(
            TeacherConversationStore,
            TeacherSubjectBindingStore,
            TeacherSubjectMemoryStore);
        TeacherRecurringLearningSignalService = new AiTeacherRecurringLearningSignalService(
            TeacherConversationStore,
            TeacherSubjectBindingStore);

        ContextProvider = new StudyPlanningContextProvider(repository);
        EnemCatalog = EnemCatalogService.Default;
        PlanningService = new AiPlanningService(Backend, ConfigurationStore, ContextProvider, EnemCatalog);
        PreviewService = new AiProposalPreviewService(repository, ContextProvider);
        ProposalStore = new AiProposalStore(Path.Combine(rootDirectory, "proposals.json"));
        ConversationStore = new AiConversationStore(Path.Combine(rootDirectory, "conversation.json"));
        Workflow = new AiProposalWorkflowService(PlanningService, PreviewService, ProposalStore);
        ApplicationService = new AiProposalApplicationService(repository, PreviewService, ProposalStore);
        AssistantController = new AiAssistantController(
            ConfigurationStore,
            ModelManager,
            ProposalStore,
            Workflow,
            ConversationStore);
        InstallationController = new AiInstallationController(
            rootDirectory,
            ConfigurationStore,
            ModelManager,
            Installer);
    }

    public string RootDirectory { get; }
    public AiTeacherManual TeacherManual { get; }

    public WindowsAiHardwareProfileDetector HardwareDetector { get; }
    public AiHardwareDiagnosticsService HardwareDiagnostics { get; }
    public AiConfigurationStore ConfigurationStore { get; }
    public LocalAiModelManager ModelManager { get; }
    public LocalAiInstaller Installer { get; }
    public LocalAiRuntimeHost RuntimeHost { get; }
    public AiPerformanceDiagnosticsService PerformanceDiagnostics { get; }
    public LlamaServerBackend Backend { get; }
    public LlamaTeacherBackend TeacherBackend { get; }
    public AiTeacherService TeacherService { get; }

    public AiTeacherConversationStore TeacherConversationStore { get; }
    public AiTeacherLessonSummaryStore TeacherLessonSummaryStore { get; }
    public AiTeacherLessonSummaryService TeacherLessonSummaryService { get; }

    /// <summary>
    /// Associação conversa→matéria derivada de pacote verificado. Fica separada do
    /// prompt e do calendário; não é um diagnóstico do aluno.
    /// </summary>
    public AiTeacherSubjectBindingStore TeacherSubjectBindingStore { get; }

    /// <summary>
    /// Memória factual por matéria, derivada do histórico completo. Continua passiva
    /// até um bloco posterior definir recuperação explícita e limitada para o prompt.
    /// </summary>
    public AiTeacherSubjectMemoryStore TeacherSubjectMemoryStore { get; }
    public AiTeacherSubjectMemoryService TeacherSubjectMemoryService { get; }

    /// <summary>
    /// Leitura passiva de categorias recorrentes já registradas no histórico. Ela
    /// não é diagnóstico, não escreve estado e não entra no prompt da professora.
    /// </summary>
    public AiTeacherRecurringLearningSignalService TeacherRecurringLearningSignalService { get; }

    public StudyPlanningContextProvider ContextProvider { get; }
    public EnemCatalogService EnemCatalog { get; }
    public AiPlanningService PlanningService { get; }
    public AiProposalPreviewService PreviewService { get; }
    public AiProposalStore ProposalStore { get; }
    public AiConversationStore ConversationStore { get; }
    public AiProposalWorkflowService Workflow { get; }
    public AiProposalApplicationService ApplicationService { get; }
    public AiAssistantController AssistantController { get; }
    public AiInstallationController InstallationController { get; }

    public static LocalAiServices Create(StudyRepository repository, string? rootDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var selectedRoot = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "AI");
        if (string.IsNullOrWhiteSpace(selectedRoot) || !Path.IsPathFullyQualified(selectedRoot))
            throw new AiContractValidationException("O diretório dos serviços locais da IA precisa ser absoluto.");
        return new LocalAiServices(repository, Path.GetFullPath(selectedRoot));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await InstallationController.DisposeAsync().ConfigureAwait(false);
        await AssistantController.DisposeAsync().ConfigureAwait(false);
        ApplicationService.Dispose();
        Workflow.Dispose();
        TeacherBackend.Dispose();
        Backend.Dispose();
        PerformanceDiagnostics.Dispose();
        try
        {
            await RuntimeHost.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Installer.Dispose();
            TeacherSubjectMemoryStore.Dispose();
            TeacherSubjectBindingStore.Dispose();
            TeacherLessonSummaryStore.Dispose();
            TeacherConversationStore.Dispose();
            ConversationStore.Dispose();
            ProposalStore.Dispose();
            ConfigurationStore.Dispose();
        }
    }
}
