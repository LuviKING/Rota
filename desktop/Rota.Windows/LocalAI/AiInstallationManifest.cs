namespace Rota.Desktop.LocalAI;

public sealed class AiInstallationManifest
{
    public const int CurrentManifestVersion = 1;
    private const long MaximumArtifactBytes = 16L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> TrustedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "huggingface.co"
    };
    private static readonly HashSet<string> ReservedWindowsFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly IReadOnlyList<AiRuntimePackage> _runtimePackages;
    private readonly IReadOnlyList<AiModelPackage> _modelPackages;

    public static AiInstallationManifest Default { get; } = CreateDefault();

    public int Version { get; }
    public IReadOnlyList<AiRuntimePackage> RuntimePackages => _runtimePackages;
    public IReadOnlyList<AiModelPackage> ModelPackages => _modelPackages;

    public AiInstallationManifest(
        int version,
        IEnumerable<AiRuntimePackage> runtimePackages,
        IEnumerable<AiModelPackage> modelPackages,
        IAiModelCatalog? modelCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(runtimePackages);
        ArgumentNullException.ThrowIfNull(modelPackages);

        Version = version;
        var runtimes = runtimePackages.ToArray();
        var models = modelPackages.ToArray();
        Validate(version, runtimes, models, modelCatalog ?? AiModelCatalog.Default);
        _runtimePackages = Array.AsReadOnly(runtimes);
        _modelPackages = Array.AsReadOnly(models);
    }

    public AiRuntimePackage GetRuntimePackage(AiComputePreference preference)
    {
        if (preference is not (AiComputePreference.Cpu or AiComputePreference.Gpu))
            throw new AiContractValidationException("O manifesto exige uma preferência concreta de runtime.");
        return _runtimePackages.Single(package => package.ComputePreference == preference);
    }

    public AiModelPackage GetModelPackage(string modelId)
    {
        var package = _modelPackages.SingleOrDefault(
            item => string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        return package ?? throw new AiContractValidationException(
            "O modelo selecionado não possui um artefato verificável no manifesto local.");
    }

    private static AiInstallationManifest CreateDefault()
    {
        var runtimes = new[]
        {
            new AiRuntimePackage(
                AiComputePreference.Cpu,
                new AiDownloadArtifact(
                    "llama.cpp-b10795-win-cpu-x64",
                    new Uri("https://github.com/ggml-org/llama.cpp/releases/download/b10795/llama-b10795-bin-win-cpu-x64.zip"),
                    "llama-b10795-bin-win-cpu-x64.zip",
                    18_389_848,
                    "24b865773e7ef99996197a85a3b9bec88a9dedd75e490010d426e5a6a9353eab"),
                LocalAiModelManager.RuntimeFileName),
            new AiRuntimePackage(
                AiComputePreference.Gpu,
                new AiDownloadArtifact(
                    "llama.cpp-b10795-win-vulkan-x64",
                    new Uri("https://github.com/ggml-org/llama.cpp/releases/download/b10795/llama-b10795-bin-win-vulkan-x64.zip"),
                    "llama-b10795-bin-win-vulkan-x64.zip",
                    35_208_196,
                    "d6f81f2cbbaf457aa4392080a6b8d7675e0cb4ef0163ee26076439dfc956aab4"),
                LocalAiModelManager.RuntimeFileName)
        };

        var models = new[]
        {
            new AiModelPackage(
                "qwen3-1.7b-q8-0",
                new AiDownloadArtifact(
                    "qwen3-1.7b-q8-0-90862c4b",
                    new Uri("https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/resolve/90862c4b9d2787eaed51d12237eafdfe7c5f6077/Qwen3-1.7B-Q8_0.gguf?download=true"),
                    "Qwen3-1.7B-Q8_0.gguf",
                    1_834_426_016,
                    "061b54daade076b5d3362dac252678d17da8c68f07560be70818cace6590cb1a")),
            new AiModelPackage(
                "qwen3-4b-q4-k-m",
                new AiDownloadArtifact(
                    "qwen3-4b-q4-k-m-bc640142",
                    new Uri("https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/bc640142c66e1fdd12af0bd68f40445458f3869b/Qwen3-4B-Q4_K_M.gguf?download=true"),
                    "Qwen3-4B-Q4_K_M.gguf",
                    2_497_280_256,
                    "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5")),
            new AiModelPackage(
                "qwen3-8b-q4-k-m",
                new AiDownloadArtifact(
                    "qwen3-8b-q4-k-m-7c41481f",
                    new Uri("https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/7c41481f57cb95916b40956ab2f0b139b296d974/Qwen3-8B-Q4_K_M.gguf?download=true"),
                    "Qwen3-8B-Q4_K_M.gguf",
                    5_027_783_488,
                    "d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785"))
        };

        return new AiInstallationManifest(CurrentManifestVersion, runtimes, models);
    }

    private static void Validate(
        int version,
        IReadOnlyList<AiRuntimePackage> runtimes,
        IReadOnlyList<AiModelPackage> models,
        IAiModelCatalog catalog)
    {
        if (version != CurrentManifestVersion)
            throw new AiContractValidationException($"Versão de manifesto de instalação não suportada: {version}.");
        if (runtimes.Count != 2 || runtimes.Any(package => package is null) ||
            runtimes.Count(package => package.ComputePreference == AiComputePreference.Cpu) != 1 ||
            runtimes.Count(package => package.ComputePreference == AiComputePreference.Gpu) != 1 ||
            runtimes.Any(package => package.ComputePreference == AiComputePreference.Automatic))
        {
            throw new AiContractValidationException("O manifesto precisa de exatamente um runtime CPU e um runtime GPU.");
        }

        var artifactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var artifactFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtime in runtimes)
        {
            if (runtime is null)
                throw new AiContractValidationException("O manifesto contém um pacote de runtime nulo.");
            ValidateArtifact(runtime.Archive, ".zip", artifactIds, artifactFileNames);
            ValidateRelativePath(runtime.ServerRelativePath, "executável do runtime");
            if (!string.Equals(Path.GetFileName(runtime.ServerRelativePath), LocalAiModelManager.RuntimeFileName, StringComparison.OrdinalIgnoreCase))
                throw new AiContractValidationException("O pacote de runtime não identifica llama-server.exe.");
        }

        var modelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (model is null)
                throw new AiContractValidationException("O manifesto contém um pacote de modelo nulo.");
            var descriptor = catalog.GetById(model.ModelId);
            if (!modelIds.Add(model.ModelId))
                throw new AiContractValidationException("O manifesto contém pacotes duplicados para um modelo.");
            ValidateArtifact(model.Artifact, ".gguf", artifactIds, artifactFileNames);
            if (!string.Equals(model.Artifact.FileName, descriptor.FileName, StringComparison.Ordinal))
                throw new AiContractValidationException("O nome do artefato não corresponde ao catálogo local de modelos.");
        }

        if (catalog.Models.Any(model => !modelIds.Contains(model.Id)) || modelIds.Count != catalog.Models.Count)
            throw new AiContractValidationException("O manifesto precisa conter exatamente um artefato para cada modelo do catálogo.");
    }

    private static void ValidateArtifact(
        AiDownloadArtifact artifact,
        string expectedExtension,
        HashSet<string> ids,
        HashSet<string> fileNames)
    {
        if (artifact is null)
            throw new AiContractValidationException("O manifesto contém um artefato nulo.");
        if (string.IsNullOrWhiteSpace(artifact.Id) ||
            artifact.Id.Length > 160 ||
            artifact.Id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')) ||
            !ids.Add(artifact.Id))
        {
            throw new AiContractValidationException("O manifesto contém um ID de artefato inválido ou duplicado.");
        }
        if (artifact.Source is null || !artifact.Source.IsAbsoluteUri ||
            !string.Equals(artifact.Source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !artifact.Source.IsDefaultPort ||
            artifact.Source.UserInfo.Length > 0 ||
            artifact.Source.Fragment.Length > 0 ||
            !TrustedHosts.Contains(artifact.Source.Host) ||
            artifact.Source.AbsolutePath.Contains("/main/", StringComparison.OrdinalIgnoreCase) ||
            artifact.Source.AbsolutePath.Contains("/latest/", StringComparison.OrdinalIgnoreCase) ||
            !IsPinnedSource(artifact.Source))
        {
            throw new AiContractValidationException("O manifesto contém uma origem não confiável ou não versionada.");
        }
        if (string.IsNullOrWhiteSpace(artifact.FileName) ||
            artifact.FileName.Length > 240 ||
            !string.Equals(Path.GetFileName(artifact.FileName), artifact.FileName, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(artifact.FileName), expectedExtension, StringComparison.OrdinalIgnoreCase) ||
            !fileNames.Add(artifact.FileName))
        {
            throw new AiContractValidationException("O manifesto contém um nome de artefato inseguro ou duplicado.");
        }
        ValidateRelativePath(artifact.FileName, "arquivo do manifesto");
        if (artifact.ExpectedSizeBytes is <= 0 or > MaximumArtifactBytes)
            throw new AiContractValidationException("O manifesto contém um tamanho de artefato inválido.");
        if (artifact.Sha256 is null || artifact.Sha256.Length != 64 || artifact.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new AiContractValidationException("O manifesto contém um SHA-256 inválido.");
    }

    private static bool IsPinnedSource(Uri source)
    {
        var parts = source.AbsolutePath.Trim('/').Split('/');
        if (source.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))
            return parts.Length == 5 && parts[2] == "resolve" &&
                parts[3].Length == 40 && parts[3].All(Uri.IsHexDigit);
        return parts.Length == 6 && parts[2] == "releases" && parts[3] == "download" &&
            parts[4] is not ("main" or "master" or "latest") && parts[4].Length > 0;
    }

    internal static void ValidateRelativePath(string path, string field)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path))
        {
            throw new AiContractValidationException($"O caminho relativo do {field} é inseguro.");
        }

        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var baseName = part.Split('.')[0];
            if (part is "" or "." or ".." ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                part.EndsWith(' ') ||
                part.EndsWith('.') ||
                ReservedWindowsFileNames.Contains(baseName))
            {
                throw new AiContractValidationException($"O caminho relativo do {field} é inseguro.");
            }
        }
    }
}
