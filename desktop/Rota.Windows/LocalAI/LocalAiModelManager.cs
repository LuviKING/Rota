namespace Rota.Desktop.LocalAI;

public sealed class LocalAiModelManager : IAiModelManager
{
    public const string RuntimeFileName = "llama-server.exe";

    private readonly IAiModelCatalog _catalog;
    private readonly IAiHardwareProfileDetector _hardwareDetector;

    public string RootDirectory { get; }
    public string RuntimeDirectory => Path.Combine(RootDirectory, "runtime");
    public string ModelsDirectory => Path.Combine(RootDirectory, "models");

    public LocalAiModelManager(
        string? rootDirectory = null,
        IAiModelCatalog? catalog = null,
        IAiHardwareProfileDetector? hardwareDetector = null)
    {
        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "AI");
        var selectedRoot = rootDirectory ?? defaultRoot;
        if (string.IsNullOrWhiteSpace(selectedRoot) || !Path.IsPathFullyQualified(selectedRoot))
            throw new AiContractValidationException("O diretório local da IA precisa ser um caminho absoluto.");

        RootDirectory = Path.GetFullPath(selectedRoot);
        _catalog = catalog ?? AiModelCatalog.Default;
        _hardwareDetector = hardwareDetector ?? new WindowsAiHardwareProfileDetector();
    }

    public async Task<AiModelInstallationInfo> GetInstallationInfoAsync(
        AiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        AiContractValidator.ValidateConfiguration(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        var effectiveProfile = configuration.Profile;
        if (effectiveProfile == AiProfile.Automatic)
        {
            var hardware = await _hardwareDetector.DetectAsync(cancellationToken).ConfigureAwait(false);
            effectiveProfile = AiProfileRecommendationPolicy.Resolve(effectiveProfile, hardware);
            warnings.AddRange(hardware.Warnings);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var model = string.IsNullOrWhiteSpace(configuration.ModelId)
            ? _catalog.GetRecommendedModel(effectiveProfile)
            : _catalog.GetById(configuration.ModelId);
        if (model.Profile != effectiveProfile)
        {
            throw new AiContractValidationException(
                "O modelo selecionado não é compatível com o perfil de IA efetivo.");
        }

        var runtimePath = string.IsNullOrWhiteSpace(configuration.RuntimePath)
            ? Path.Combine(RuntimeDirectory, RuntimeFileName)
            : Path.GetFullPath(configuration.RuntimePath);
        var modelPath = string.IsNullOrWhiteSpace(configuration.ModelPath)
            ? Path.Combine(ModelsDirectory, model.FileName)
            : Path.GetFullPath(configuration.ModelPath);
        ValidateArtifactExtension(runtimePath, ".exe", "runtime");
        ValidateArtifactExtension(modelPath, ".gguf", "modelo");

        var runtimeAvailable = IsUsableFile(runtimePath, "runtime", warnings);
        var modelAvailable = IsUsableFile(modelPath, "modelo", warnings);
        var state = runtimeAvailable
            ? modelAvailable ? AiInstallationState.Ready : AiInstallationState.RuntimeInstalled
            : AiInstallationState.NotInstalled;

        if (modelAvailable && !runtimeAvailable)
            warnings.Add("O arquivo do modelo existe, mas o runtime local ainda não está disponível.");
        if (configuration.InstallationState != state)
            warnings.Add("O estado salvo da instalação está desatualizado em relação aos arquivos locais.");

        return new AiModelInstallationInfo(
            state,
            effectiveProfile,
            model,
            runtimePath,
            modelPath,
            runtimeAvailable,
            modelAvailable,
            warnings.ToArray());
    }

    private static bool IsUsableFile(string path, string artifactName, List<string> warnings)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return false;
            if (file.Length > 0) return true;
            warnings.Add($"O arquivo do {artifactName} está vazio e será tratado como não instalado.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warnings.Add($"O arquivo do {artifactName} não pôde ser verificado e será tratado como não instalado.");
            return false;
        }
    }

    private static void ValidateArtifactExtension(string path, string extension, string artifactName)
    {
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new AiContractValidationException($"O caminho do {artifactName} possui um formato incompatível.");
    }
}
