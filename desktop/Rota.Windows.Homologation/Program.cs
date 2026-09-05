using Rota.Desktop;
using Rota.Desktop.LocalAI;
using System.Diagnostics;
using System.Text.Json;

var options = Options.Parse(args);
Directory.CreateDirectory(options.RootDirectory);
var detector = new WindowsAiHardwareProfileDetector();
var hardware = await detector.DetectAsync();
var report = new HomologationReport
{
    StartedAtUtc = DateTimeOffset.UtcNow,
    Machine = new MachineResult
    {
        Cpu = hardware.CpuName,
        LogicalProcessors = hardware.LogicalProcessorCount,
        SystemMemoryBytes = hardware.SystemMemoryBytes,
        Gpu = hardware.GpuName,
        DedicatedGpuMemoryBytes = hardware.DedicatedGpuMemoryBytes,
        AutomaticProfile = hardware.RecommendedProfile.ToString(),
        Warnings = hardware.Warnings.ToList()
    }
};

foreach (var profile in options.Profiles)
{
    Console.WriteLine($"\n=== {profile} ===");
    var result = await RunProfileAsync(profile, options, detector);
    report.Profiles.Add(result);
    Console.WriteLine(result.Passed
        ? $"PASS {profile}: startup {result.StartupMilliseconds} ms, generation {result.GenerationMilliseconds} ms"
        : $"FAIL {profile}: {result.Error}");
}

report = report with
{
    FinishedAtUtc = DateTimeOffset.UtcNow,
    Passed = report.Profiles.Count > 0 && report.Profiles.All(result => result.Passed)
};
var reportDirectory = Path.GetDirectoryName(options.OutputPath);
if (!string.IsNullOrWhiteSpace(reportDirectory)) Directory.CreateDirectory(reportDirectory);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true
}));
Console.WriteLine($"\nReport: {options.OutputPath}");
return report.Passed ? 0 : 1;

static async Task<ProfileResult> RunProfileAsync(
    AiProfile profile,
    Options options,
    IAiHardwareProfileDetector detector)
{
    var catalog = AiModelCatalog.Default;
    var model = catalog.GetRecommendedModel(profile);
    var budget = AiProfileResourceBudgets.Get(profile);
    var profileRoot = Path.Combine(options.RootDirectory, profile.ToString().ToLowerInvariant());
    Directory.CreateDirectory(profileRoot);
    var store = new AiConfigurationStore(Path.Combine(profileRoot, "config.json"));
    var manager = new LocalAiModelManager(profileRoot, catalog, detector);
    using var installer = new LocalAiInstaller(
        profileRoot,
        store,
        manager,
        new CachedArtifactDownloader(options.CacheDirectory));

    var initial = new AiConfiguration
    {
        Profile = profile,
        ModelId = model.Id,
        ContextSize = model.DefaultContextSize,
        ComputePreference = budget.RecommendedCompute,
        InstallationState = AiInstallationState.NotInstalled
    };

    try
    {
        var nvidiaMemoryBefore = TryReadNvidiaTotalMemory();
        var peakNvidiaMemory = nvidiaMemoryBefore;
        var installationWatch = Stopwatch.StartNew();
        var existing = await store.LoadAsync();
        AiConfiguration configuration;
        var existingInfo = await manager.GetInstallationInfoAsync(existing);
        if (existing.Profile == profile && existing.ModelId == model.Id && existingInfo.State == AiInstallationState.Ready)
        {
            configuration = existing;
            installationWatch.Stop();
        }
        else
        {
            var progress = new ConsoleInstallationProgress();
            var installed = await installer.InstallAsync(initial, progress);
            configuration = installed.Configuration;
            installationWatch.Stop();
            Console.WriteLine();
        }

        await using var runtime = new LocalAiRuntimeHost(manager);
        var startupWatch = Stopwatch.StartNew();
        var status = await runtime.StartAsync(configuration);
        startupWatch.Stop();
        if (status.State != AiRuntimeState.Ready || status.ProcessId is null)
            throw new InvalidOperationException("O runtime real não ficou pronto.");

        using var process = Process.GetProcessById(status.ProcessId.Value);
        process.Refresh();
        var peakWorkingSet = process.WorkingSet64;
        var peakPrivate = process.PrivateMemorySize64;
        peakNvidiaMemory = Maximum(peakNvidiaMemory, TryReadNvidiaTotalMemory());
        using var samplerCancellation = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            var samples = 0;
            while (!samplerCancellation.IsCancellationRequested)
            {
                try
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                    if (samples++ % 10 == 0)
                        peakNvidiaMemory = Maximum(peakNvidiaMemory, TryReadNvidiaTotalMemory());
                    await Task.Delay(100, samplerCancellation.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (InvalidOperationException) { break; }
            }
        });

        using var backend = new LlamaServerBackend(runtime, generationTimeout: TimeSpan.FromMinutes(10));
        var planning = new AiPlanningService(
            backend,
            store,
            enemCatalogProvider: EnemCatalogService.Default);
        var input = new AiAssistantInput
        {
            FreeText = "Crie uma prévia mínima para o ENEM, com prova em 2031-11-09. Gere exatamente uma única sessão de 30 minutos em 2031-02-10, com a matéria Matemática e o conteúdo Álgebra, funções, equações e gráficos. Use somente nomes do catálogo."
        };
        var generationWatch = Stopwatch.StartNew();
        var generation = await planning.CreateGenerationAsync(input, AiProposalKind.StudyPlan);
        generationWatch.Stop();
        samplerCancellation.Cancel();
        await sampler;
        process.Refresh();
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
        peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
        peakNvidiaMemory = Maximum(peakNvidiaMemory, TryReadNvidiaTotalMemory());
        var package = StudyPlanImporter.Parse(generation.Proposal.StudyPlan!.StudyPlanJson);
        if (!string.Equals(package.ObjectiveName, "ENEM", StringComparison.Ordinal) ||
            !string.Equals(package.ObjectiveDate, "2031-11-09", StringComparison.Ordinal) ||
            package.Sessions.Count != 1 || package.Sessions[0].Minutes != 30 ||
            !package.Sessions.Any(session =>
                string.Equals(session.Date, "2031-02-10", StringComparison.Ordinal) &&
                string.Equals(session.Subject, "Matemática", StringComparison.Ordinal) &&
                string.Equals(session.Topic, "Álgebra, funções, equações e gráficos", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("A proposta não preservou o objetivo e a sessão mínima pedidos pela interface.");
        }
        var gpuMemory = TryReadNvidiaMemory(status.ProcessId.Value);
        await runtime.StopAsync();

        return new ProfileResult
        {
            Profile = profile.ToString(),
            ModelId = model.Id,
            ModelArtifactBytes = AiInstallationManifest.Default.GetModelPackage(model.Id).Artifact.ExpectedSizeBytes,
            Compute = status.ComputePreference?.ToString() ?? configuration.ComputePreference.ToString(),
            ContextSize = configuration.ContextSize,
            InstallationMilliseconds = installationWatch.ElapsedMilliseconds,
            StartupMilliseconds = startupWatch.ElapsedMilliseconds,
            GenerationMilliseconds = generationWatch.ElapsedMilliseconds,
            PeakWorkingSetBytes = peakWorkingSet,
            PeakPrivateBytes = peakPrivate,
            NvidiaProcessMemoryMiB = gpuMemory,
            NvidiaTotalMemoryBeforeMiB = nvidiaMemoryBefore,
            NvidiaTotalMemoryPeakMiB = peakNvidiaMemory,
            SessionCount = package.Sessions.Count,
            PlannedMinutes = package.Sessions.Sum(session => session.Minutes),
            ProposalSummary = generation.Proposal.Summary,
            Passed = true
        };
    }
    catch (Exception ex)
    {
        return new ProfileResult
        {
            Profile = profile.ToString(),
            ModelId = model.Id,
            ModelArtifactBytes = AiInstallationManifest.Default.GetModelPackage(model.Id).Artifact.ExpectedSizeBytes,
            Compute = budget.RecommendedCompute.ToString(),
            ContextSize = model.DefaultContextSize,
            Error = Flatten(ex),
            Passed = false
        };
    }
    finally
    {
        store.Dispose();
    }
}

static long? Maximum(long? left, long? right) => left switch
{
    null => right,
    _ when right is null => left,
    _ => Math.Max(left.Value, right.Value)
};

static long? TryReadNvidiaTotalMemory()
{
    try
    {
        using var command = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-gpu=memory.used --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        command.Start();
        var output = command.StandardOutput.ReadToEnd();
        command.WaitForExit(5_000);
        var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return long.TryParse(first, out var memory) ? memory : null;
    }
    catch { return null; }
}

static long? TryReadNvidiaMemory(int processId)
{
    try
    {
        using var command = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-compute-apps=pid,used_memory --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        command.Start();
        var output = command.StandardOutput.ReadToEnd();
        command.WaitForExit(5_000);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length == 2 && int.TryParse(fields[0], out var pid) && pid == processId &&
                long.TryParse(fields[1], out var memory)) return memory;
        }
    }
    catch { }
    return null;
}

static string Flatten(Exception exception)
{
    var messages = new List<string>();
    for (var current = exception; current is not null; current = current.InnerException!)
    {
        if (!messages.Contains(current.Message, StringComparer.Ordinal)) messages.Add(current.Message);
        if (current.InnerException is null) break;
    }
    return string.Join(" -> ", messages);
}

internal sealed class CachedArtifactDownloader : IAiArtifactDownloader
{
    private readonly string _cacheDirectory;

    public CachedArtifactDownloader(string cacheDirectory)
    {
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        if (!Directory.Exists(_cacheDirectory))
            throw new DirectoryNotFoundException("O cache de homologação não existe: " + _cacheDirectory);
    }

    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        long expectedSizeBytes,
        IProgress<AiDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(Uri.UnescapeDataString(source.AbsolutePath));
        var cachedPath = Path.Combine(_cacheDirectory, fileName);
        var cached = new FileInfo(cachedPath);
        if (!cached.Exists || cached.Length != expectedSizeBytes)
            throw new InvalidDataException($"O artefato {fileName} não está completo no cache de homologação.");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var input = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            progress?.Report(new AiDownloadProgress(copied, expectedSizeBytes));
        }
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }
}

internal sealed class ConsoleInstallationProgress : IProgress<AiInstallationProgress>
{
    private AiInstallationStage? _lastStage;
    private int _lastBucket = -1;

    public void Report(AiInstallationProgress value)
    {
        var isDownload = value.Stage is AiInstallationStage.DownloadingRuntime or AiInstallationStage.DownloadingModel;
        var bucket = isDownload && value.TotalBytes is > 0
            ? (int)Math.Floor(10d * value.BytesReceived / value.TotalBytes.Value)
            : -1;
        if (_lastStage == value.Stage && (!isDownload || bucket == _lastBucket)) return;
        _lastStage = value.Stage;
        _lastBucket = bucket;
        if (isDownload && value.TotalBytes is > 0)
            Console.WriteLine($"{value.Stage}: {Math.Min(100, bucket * 10)}%");
        else
            Console.WriteLine(value.Stage);
    }
}

internal sealed record Options
{
    public required string RootDirectory { get; init; }
    public required string CacheDirectory { get; init; }
    public required string OutputPath { get; init; }
    public required IReadOnlyList<AiProfile> Profiles { get; init; }

    public static Options Parse(string[] args)
    {
        string? root = null;
        string? cache = null;
        string? output = null;
        string? profileText = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Argumento sem valor: " + args[index]);
            var value = args[++index];
            switch (args[index - 1])
            {
                case "--root": root = value; break;
                case "--cache": cache = value; break;
                case "--output": output = value; break;
                case "--profiles": profileText = value; break;
                default: throw new ArgumentException("Argumento desconhecido: " + args[index - 1]);
            }
        }
        if (root is null || cache is null || output is null)
            throw new ArgumentException("Use --root, --cache e --output.");
        var profiles = string.IsNullOrWhiteSpace(profileText)
            ? new[] { AiProfile.Lightweight, AiProfile.Balanced, AiProfile.Performance }
            : profileText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.Parse<AiProfile>(value, ignoreCase: true)).ToArray();
        if (profiles.Length == 0 || profiles.Distinct().Count() != profiles.Length ||
            profiles.Any(profile => profile == AiProfile.Automatic || !Enum.IsDefined(profile)))
            throw new ArgumentException("A lista de perfis de homologação é inválida.");
        return new Options
        {
            RootDirectory = Path.GetFullPath(root),
            CacheDirectory = Path.GetFullPath(cache),
            OutputPath = Path.GetFullPath(output),
            Profiles = profiles
        };
    }
}

internal sealed record HomologationReport
{
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
    public bool Passed { get; init; }
    public MachineResult Machine { get; init; } = new();
    public List<ProfileResult> Profiles { get; init; } = new();
}

internal sealed record MachineResult
{
    public string Cpu { get; init; } = "";
    public int LogicalProcessors { get; init; }
    public long SystemMemoryBytes { get; init; }
    public string Gpu { get; init; } = "";
    public long? DedicatedGpuMemoryBytes { get; init; }
    public string AutomaticProfile { get; init; } = "";
    public List<string> Warnings { get; init; } = new();
}

internal sealed record ProfileResult
{
    public string Profile { get; init; } = "";
    public string ModelId { get; init; } = "";
    public long ModelArtifactBytes { get; init; }
    public string Compute { get; init; } = "";
    public int ContextSize { get; init; }
    public long InstallationMilliseconds { get; init; }
    public long StartupMilliseconds { get; init; }
    public long GenerationMilliseconds { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long PeakPrivateBytes { get; init; }
    public long? NvidiaProcessMemoryMiB { get; init; }
    public long? NvidiaTotalMemoryBeforeMiB { get; init; }
    public long? NvidiaTotalMemoryPeakMiB { get; init; }
    public int SessionCount { get; init; }
    public int PlannedMinutes { get; init; }
    public string ProposalSummary { get; init; } = "";
    public string Error { get; init; } = "";
    public bool Passed { get; init; }
}
