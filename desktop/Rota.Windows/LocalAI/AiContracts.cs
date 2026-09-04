namespace Rota.Desktop.LocalAI;

public enum AiProfile
{
    Automatic,
    Lightweight,
    Balanced,
    Performance
}

public enum AiComputePreference
{
    Automatic,
    Cpu,
    Gpu
}

public enum AiInstallationState
{
    NotInstalled,
    RuntimeInstalled,
    Ready
}

public enum AiProposalKind
{
    StudyPlan,
    PlanChanges
}

public enum AiProposalStatus
{
    Pending,
    Validated,
    Applied,
    Rejected,
    Failed
}

public enum AiPlanOperationType
{
    MoveSession,
    AddSession,
    RemoveFutureSession,
    ChangeSubjectPriority,
    SetAvailability,
    RedistributeLoad,
    RebuildFuturePlan
}

public sealed record AiConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AiProfile Profile { get; init; } = AiProfile.Automatic;
    public string ModelId { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string RuntimePath { get; init; } = "";
    public int ContextSize { get; init; } = 4096;
    public AiComputePreference ComputePreference { get; init; } = AiComputePreference.Automatic;
    public AiInstallationState InstallationState { get; init; } = AiInstallationState.NotInstalled;
}

public sealed record AiAssistantInput
{
    public string ObjectiveOrExam { get; init; } = "";
    public string ExamDate { get; init; } = "";
    public double? AvailableHoursPerDay { get; init; }
    public List<DayOfWeek> AvailableDays { get; init; } = new();
    public List<string> StrongSubjects { get; init; } = new();
    public List<string> WeakSubjects { get; init; } = new();
    public string Goal { get; init; } = "";
    public string Notes { get; init; } = "";
    public string FreeText { get; init; } = "";
}

public enum AiProposalPreviewState
{
    Ready,
    Blocked
}

public sealed record AiPlanningContext
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string SnapshotDate { get; init; } = "";
    public string ObjectiveName { get; init; } = "";
    public string ObjectiveDate { get; init; } = "";
    public string ActivePlanId { get; init; } = "";
    public int ActivePlanRevision { get; init; }
    public string ActivePlanTitle { get; init; } = "";
    public int DailyMinutesLimit { get; init; }
    public int BlockMinutes { get; init; }
    public List<AiPlanningSessionContext> FutureSessions { get; init; } = new();
    public bool HasMoreFutureSessions { get; init; }
}

public sealed record AiPlanningSessionContext
{
    public string SessionId { get; init; } = "";
    public string PlanId { get; init; } = "";
    public int PlanRevision { get; init; }
    public string Date { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Topic { get; init; } = "";
    public int Minutes { get; init; }
    public string Kind { get; init; } = "";
    public string Origin { get; init; } = "";
    public bool ProtectedFromDirectRemoval { get; init; }
}

public sealed record AiStudyPlanDraft
{
    public string StudyPlanJson { get; init; } = "";
}

public sealed record AiPlanOperation
{
    public Guid Id { get; init; }
    public AiPlanOperationType Type { get; init; }
    public string Summary { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Subject { get; init; } = "";
    public string DestinationDate { get; init; } = "";
    public int? Minutes { get; init; }
    public int? Priority { get; init; }
    public double? MaxHoursPerDay { get; init; }
    public List<DayOfWeek> AvailableDays { get; init; } = new();
}

public sealed record AiPlanChangeDraft
{
    public List<AiPlanOperation> Operations { get; init; } = new();
}

public sealed record AiProposal
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Summary { get; init; } = "";
    public AiProposalKind Kind { get; init; }
    public AiStudyPlanDraft? StudyPlan { get; init; }
    public AiPlanChangeDraft? Changes { get; init; }
    public List<string> Warnings { get; init; } = new();
    public AiProposalStatus Status { get; init; } = AiProposalStatus.Pending;
}

public sealed record AiProposalPreview
{
    public Guid ProposalId { get; init; }
    public AiProposalKind Kind { get; init; }
    public AiProposalPreviewState State { get; init; }
    public string Message { get; init; } = "";
    public int BeforeSessionCount { get; init; }
    public int AfterSessionCount { get; init; }
    public int BeforeMinutes { get; init; }
    public int AfterMinutes { get; init; }
    public int AddedSessionCount { get; init; }
    public int RemovedSessionCount { get; init; }
    public int MovedSessionCount { get; init; }
    public List<AiPreviewOperation> Operations { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public bool CanProceed => State == AiProposalPreviewState.Ready;
}

public sealed record AiPreviewOperation
{
    public Guid OperationId { get; init; }
    public AiPlanOperationType Type { get; init; }
    public string Summary { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string OriginalDate { get; init; } = "";
    public string ProposedDate { get; init; } = "";
}

public sealed record AiModelDescriptor(
    string Id,
    string DisplayName,
    AiProfile Profile,
    string ParameterClass,
    string Quantization,
    string FileName,
    int DefaultContextSize,
    bool IsRecommended);

public sealed record AiModelInstallationInfo(
    AiInstallationState State,
    AiProfile EffectiveProfile,
    AiModelDescriptor Model,
    string RuntimePath,
    string ModelPath,
    bool RuntimeAvailable,
    bool ModelAvailable,
    IReadOnlyList<string> Warnings);

public sealed record AiHardwareProfile(
    string CpuName,
    int LogicalProcessorCount,
    long SystemMemoryBytes,
    string GpuName,
    long? DedicatedGpuMemoryBytes,
    AiProfile RecommendedProfile,
    IReadOnlyList<string> Warnings);

public interface ILocalAiBackend
{
    Task<AiProposal> CreateProposalAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        AiConfiguration configuration,
        AiPlanningContext? planningContext = null,
        CancellationToken cancellationToken = default);
}

public interface IAiPlanningContextProvider
{
    AiPlanningContext Capture();
}

public interface IAiConfigurationStore
{
    string ConfigurationPath { get; }
    string LastLoadWarning { get; }

    Task<AiConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AiConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IAiModelManager
{
    Task<AiModelInstallationInfo> GetInstallationInfoAsync(
        AiConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public interface IAiModelCatalog
{
    IReadOnlyList<AiModelDescriptor> Models { get; }

    IReadOnlyList<AiModelDescriptor> GetCompatibleModels(AiProfile profile);
    AiModelDescriptor GetRecommendedModel(AiProfile profile);
    AiModelDescriptor GetById(string modelId);
}

public interface IAiHardwareProfileDetector
{
    Task<AiHardwareProfile> DetectAsync(CancellationToken cancellationToken = default);
}

public interface IAiPlanningService
{
    Task<AiProposal> CreateProposalAsync(
        AiAssistantInput input,
        AiProposalKind kind,
        CancellationToken cancellationToken = default);
}

public interface IAiProposalPreviewService
{
    AiProposalPreview Preview(AiProposal proposal, AiPlanningContext? planningContext = null);
}

public sealed class AiContractValidationException : ArgumentException
{
    public AiContractValidationException(string message) : base(message)
    {
    }

    public AiContractValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class AiPlanningException : Exception
{
    public AiPlanningException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
