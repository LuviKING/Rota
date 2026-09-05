namespace Rota.Desktop.LocalAI;

public sealed class LocalAiServices : IAsyncDisposable
{
    private bool _disposed;

    private LocalAiServices(StudyRepository repository, string rootDirectory)
    {
        RootDirectory = rootDirectory;
        HardwareDetector = new WindowsAiHardwareProfileDetector();
        ConfigurationStore = new AiConfigurationStore(Path.Combine(rootDirectory, "config.json"));
        ModelManager = new LocalAiModelManager(rootDirectory, AiModelCatalog.Default, HardwareDetector);
        Installer = new LocalAiInstaller(rootDirectory, ConfigurationStore, ModelManager);
        RuntimeHost = new LocalAiRuntimeHost(ModelManager);
        Backend = new LlamaServerBackend(RuntimeHost);
        ContextProvider = new StudyPlanningContextProvider(repository);
        PlanningService = new AiPlanningService(Backend, ConfigurationStore, ContextProvider);
        PreviewService = new AiProposalPreviewService(repository, ContextProvider);
        ProposalStore = new AiProposalStore(Path.Combine(rootDirectory, "proposals.json"));
        Workflow = new AiProposalWorkflowService(PlanningService, PreviewService, ProposalStore);
        AssistantController = new AiAssistantController(ConfigurationStore, ModelManager, ProposalStore, Workflow);
    }

    public string RootDirectory { get; }
    public WindowsAiHardwareProfileDetector HardwareDetector { get; }
    public AiConfigurationStore ConfigurationStore { get; }
    public LocalAiModelManager ModelManager { get; }
    public LocalAiInstaller Installer { get; }
    public LocalAiRuntimeHost RuntimeHost { get; }
    public LlamaServerBackend Backend { get; }
    public StudyPlanningContextProvider ContextProvider { get; }
    public AiPlanningService PlanningService { get; }
    public AiProposalPreviewService PreviewService { get; }
    public AiProposalStore ProposalStore { get; }
    public AiProposalWorkflowService Workflow { get; }
    public AiAssistantController AssistantController { get; }

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
        await AssistantController.DisposeAsync().ConfigureAwait(false);
        Workflow.Dispose();
        Backend.Dispose();
        try
        {
            await RuntimeHost.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Installer.Dispose();
            ProposalStore.Dispose();
            ConfigurationStore.Dispose();
        }
    }
}
