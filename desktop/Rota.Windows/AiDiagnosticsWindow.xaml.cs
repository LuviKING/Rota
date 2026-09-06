using System.Globalization;
using System.Windows;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class AiDiagnosticsWindow : Window
{
    private readonly IAiAssistantController _controller;
    private readonly IAiHardwareDiagnosticsService _diagnosticsService;
    private readonly IAiPerformanceDiagnosticsService? _performanceService;
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _performanceCancellation;
    private bool _initializationStarted;
    private bool _refreshing;
    private bool _performanceRunning;
    private string _hardwareWarning = "";
    private string _performanceWarning = "";
    private AiPerformanceDiagnosticReport? _performanceReport;
    private (string Status, string Background, string Foreground)? _performanceStageOverride;

    public AiDiagnosticsWindow(
        IAiAssistantController controller,
        IAiHardwareDiagnosticsService? diagnosticsService = null,
        IAiPerformanceDiagnosticsService? performanceService = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _diagnosticsService = diagnosticsService ?? new AiHardwareDiagnosticsService();
        _performanceService = performanceService;
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        _controller.StateChanged += Controller_StateChanged;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        RefreshState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted) return;
        _initializationStarted = true;
        await RefreshAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _refreshCancellation?.Cancel();
        _performanceCancellation?.Cancel();
        _controller.CancelCurrentOperation();
        _controller.StateChanged -= Controller_StateChanged;
        Loaded -= Window_Loaded;
        Closed -= Window_Closed;
    }

    private void Controller_StateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RefreshState();
        else _ = Dispatcher.BeginInvoke(RefreshState);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void RunPerformanceTest_Click(object sender, RoutedEventArgs e) =>
        await RunPerformanceTestAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing || _performanceRunning) return;
        _refreshing = true;
        _hardwareWarning = "";
        _performanceWarning = "";
        _performanceReport = null;
        _performanceStageOverride = null;
        PerformanceDetailsPanel.Visibility = Visibility.Collapsed;
        HardwareStatusText.Text = "Analisando CPU, memória e placa de vídeo…";
        HardwareDetailsPanel.Visibility = Visibility.Collapsed;
        HardwareStageIcon.Background = ThemeManager.ResourceBrush("SubtleBrush");
        HardwareStageGlyph.Foreground = ThemeManager.ResourceBrush("MutedBrush");
        _refreshCancellation?.Dispose();
        using var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        RefreshState();
        try
        {
            var assistantTask = _controller.InitializeAsync(cancellation.Token);
            var diagnosticsTask = _diagnosticsService.AnalyzeAsync(cancellation.Token);
            await Task.WhenAll(assistantTask, diagnosticsTask);
            cancellation.Token.ThrowIfCancellationRequested();
            ApplyHardwareReport(await diagnosticsTask);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsLoaded) HardwareStatusText.Text = "Análise cancelada.";
        }
        catch
        {
            if (IsLoaded)
            {
                HardwareStatusText.Text = "Não foi possível analisar este computador.";
                HardwareStageIcon.Background = ThemeManager.ResourceBrush("ErrorSoftBrush");
                HardwareStageGlyph.Foreground = ThemeManager.ResourceBrush("ErrorBrush");
                _hardwareWarning = "O Rota conteve uma falha ao consultar o hardware. Tente verificar novamente.";
            }
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
            _refreshing = false;
            if (IsLoaded) RefreshState();
        }
    }

    private void RefreshState()
    {
        var state = _controller.State;
        ProfileText.Text = ProfileName(state.EffectiveProfile);
        RefreshButton.IsEnabled = !_refreshing && !_performanceRunning && !state.IsBusy;

        if (!state.IsInitialized || state.Activity == AiAssistantActivity.Loading)
        {
            SetOverall("CARREGANDO", "Lendo estado da IA…",
                "Aguarde enquanto o Rota verifica a instalação local.",
                "Verificando runtime e modelo…", "SubtleBrush", "MutedBrush");
        }
        else if (state.InstallationState == AiInstallationState.Ready)
        {
            SetOverall("PRONTA", "A IA local está instalada.",
                state.StatusMessage, "Runtime e modelo disponíveis.",
                "SuccessSoftBrush", "SuccessBrush");
        }
        else if (state.InstallationState == AiInstallationState.RuntimeInstalled)
        {
            SetOverall("INCOMPLETA", "O modelo local ainda está ausente.",
                state.StatusMessage, "Runtime disponível; modelo ausente.",
                "AssessmentSoftBrush", "AssessmentBrush");
        }
        else
        {
            SetOverall("NÃO INSTALADA", "A IA local precisa ser configurada.",
                state.StatusMessage, "Runtime e modelo ainda não instalados.",
                "AssessmentSoftBrush", "AssessmentBrush");
        }

        var warning = !string.IsNullOrWhiteSpace(_performanceWarning)
            ? _performanceWarning
            : !string.IsNullOrWhiteSpace(_hardwareWarning)
                ? _hardwareWarning
                : state.Warnings.LastOrDefault() ?? "";
        WarningNotice.Visibility = warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = warning;
        RefreshPerformanceState(state);
    }

    private async Task RunPerformanceTestAsync()
    {
        var state = _controller.State;
        if (_performanceService is null || _performanceRunning || _refreshing || state.IsBusy ||
            !state.IsInitialized || state.InstallationState != AiInstallationState.Ready)
        {
            return;
        }

        _performanceRunning = true;
        _performanceReport = null;
        _performanceStageOverride = null;
        _performanceWarning = "";
        PerformanceDetailsPanel.Visibility = Visibility.Collapsed;
        _performanceCancellation?.Dispose();
        using var cancellation = new CancellationTokenSource();
        _performanceCancellation = cancellation;
        RefreshState();
        try
        {
            var report = await _performanceService.MeasureAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsLoaded)
            {
                _performanceReport = report;
                ApplyPerformanceReport(report);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsLoaded)
            {
                _performanceStageOverride =
                    ("Teste cancelado.", "SubtleBrush", "MutedBrush");
            }
        }
        catch (AiPerformanceDiagnosticsException ex)
        {
            if (IsLoaded)
            {
                _performanceStageOverride =
                    ("Não foi possível medir.", "ErrorSoftBrush", "ErrorBrush");
                _performanceWarning = ex.Message;
            }
        }
        catch
        {
            if (IsLoaded)
            {
                _performanceStageOverride =
                    ("Não foi possível medir.", "ErrorSoftBrush", "ErrorBrush");
                _performanceWarning = "O Rota conteve uma falha durante o teste local. Tente medir novamente.";
            }
        }
        finally
        {
            if (ReferenceEquals(_performanceCancellation, cancellation)) _performanceCancellation = null;
            _performanceRunning = false;
            if (IsLoaded) RefreshState();
        }
    }

    private void RefreshPerformanceState(AiAssistantState state)
    {
        var available = _performanceService is not null && state.IsInitialized &&
                        state.InstallationState == AiInstallationState.Ready;
        RunPerformanceTestButton.IsEnabled = available && !_refreshing &&
                                             !_performanceRunning && !state.IsBusy;
        PerformanceButtonText.Text = _performanceRunning ? "Medindo…" : "Medir agora";

        if (_performanceRunning)
        {
            SetPerformanceStage(
                "Carregando e gerando uma resposta curta…",
                "PrimarySoftBrush",
                "PrimaryTextBrush");
        }
        else if (_performanceReport is not null)
        {
            SetPerformanceStage(
                $"{FormatTokenRate(_performanceReport.TokensPerSecond)} · resposta em {FormatDuration(_performanceReport.ResponseDuration)}.",
                "SuccessSoftBrush",
                "SuccessBrush");
        }
        else if (!state.IsInitialized || state.Activity == AiAssistantActivity.Loading)
        {
            SetPerformanceStage("Aguardando a instalação…", "SubtleBrush", "MutedBrush");
        }
        else if (state.InstallationState != AiInstallationState.Ready)
        {
            SetPerformanceStage("Instale a IA para medir.", "SubtleBrush", "MutedBrush");
        }
        else if (_performanceStageOverride is { } stageOverride)
        {
            SetPerformanceStage(
                stageOverride.Status,
                stageOverride.Background,
                stageOverride.Foreground);
        }
        else if (_performanceService is null)
        {
            SetPerformanceStage("Teste indisponível nesta sessão.", "SubtleBrush", "MutedBrush");
        }
        else
        {
            SetPerformanceStage("Pronto para medir.", "SubtleBrush", "MutedBrush");
        }
    }

    private void ApplyHardwareReport(AiHardwareDiagnosticReport report)
    {
        var hardware = report.Hardware;
        var recommended = ProfileName(hardware.RecommendedProfile);
        HardwareStatusText.Text = $"Analisado · perfil {recommended}.";
        HardwareStageIcon.Background = ThemeManager.ResourceBrush("PrimarySoftBrush");
        HardwareStageGlyph.Foreground = ThemeManager.ResourceBrush("PrimaryTextBrush");
        RecommendedProfileText.Text = $"{recommended.ToUpper(CultureInfo.CurrentCulture)} · RECOMENDADO";

        CpuValueText.Text = string.IsNullOrWhiteSpace(hardware.CpuName)
            ? "Processador detectado"
            : hardware.CpuName;
        CpuDetailText.Text = hardware.LogicalProcessorCount == 1
            ? "1 processador lógico"
            : $"{hardware.LogicalProcessorCount} processadores lógicos";
        MemoryValueText.Text = FormatGibibytes(hardware.SystemMemoryBytes) + " de RAM";

        GpuValueText.Text = string.IsNullOrWhiteSpace(hardware.GpuName)
            ? "Sem GPU dedicada identificada"
            : hardware.GpuName;
        GpuDetailText.Text = hardware.DedicatedGpuMemoryBytes.HasValue
            ? FormatGibibytes(hardware.DedicatedGpuMemoryBytes.Value) + " de memória dedicada"
            : "O perfil também pode usar o processador";

        if (report.Storage.AvailableBytes.HasValue)
        {
            StorageValueText.Text = FormatGibibytes(report.Storage.AvailableBytes.Value) + " livres";
            StorageDetailText.Text = report.Storage.TotalBytes.HasValue
                ? $"de {FormatGibibytes(report.Storage.TotalBytes.Value)} na unidade da IA"
                : "na unidade da IA local";
        }
        else
        {
            StorageValueText.Text = "Não foi possível consultar";
            StorageDetailText.Text = "A instalação continuará protegida pela checagem de espaço";
        }

        _hardwareWarning = report.Warnings.LastOrDefault() ?? "";
        HardwareDetailsPanel.Visibility = Visibility.Visible;
    }

    private void ApplyPerformanceReport(AiPerformanceDiagnosticReport report)
    {
        StartupValueText.Text = report.RuntimeWasAlreadyReady
            ? "Já estava pronta"
            : FormatDuration(report.StartupDuration);
        ResponseValueText.Text = FormatDuration(report.ResponseDuration);
        TokenRateValueText.Text = FormatTokenRate(report.TokensPerSecond);
        GeneratedTokensText.Text = report.GeneratedTokens == 1
            ? "1 token gerado no teste"
            : $"{report.GeneratedTokens} tokens gerados no teste";
        ComputeValueText.Text = report.ComputePreference switch
        {
            AiComputePreference.Gpu => "Placa de vídeo",
            AiComputePreference.Cpu => "Processador",
            _ => "Automático"
        };
        PerformanceProfileText.Text = $"Perfil {ProfileName(report.EffectiveProfile)}";

        var (label, background, foreground) = report.Rating switch
        {
            AiPerformanceRating.Excellent => ("EXCELENTE", "SuccessSoftBrush", "SuccessBrush"),
            AiPerformanceRating.Good => ("BOM", "PrimarySoftBrush", "PrimaryTextBrush"),
            AiPerformanceRating.Functional => ("FUNCIONAL", "AssessmentSoftBrush", "AssessmentBrush"),
            _ => ("LENTO", "ErrorSoftBrush", "ErrorBrush")
        };
        PerformanceRatingText.Text = label;
        PerformanceRatingBadge.Background = ThemeManager.ResourceBrush(background);
        PerformanceRatingText.Foreground = ThemeManager.ResourceBrush(foreground);
        PerformanceDetailsPanel.Visibility = Visibility.Visible;
    }

    private void SetPerformanceStage(string status, string background, string foreground)
    {
        PerformanceStatusText.Text = status;
        PerformanceStageIcon.Background = ThemeManager.ResourceBrush(background);
        PerformanceStageGlyph.Foreground = ThemeManager.ResourceBrush(foreground);
    }

    private void SetOverall(
        string badge,
        string title,
        string description,
        string installation,
        string badgeBackground,
        string badgeForeground)
    {
        OverallBadgeText.Text = badge;
        OverallTitle.Text = title;
        OverallDescription.Text = description;
        InstallationStatusText.Text = installation;
        OverallBadge.Background = ThemeManager.ResourceBrush(badgeBackground);
        OverallBadgeText.Foreground = ThemeManager.ResourceBrush(badgeForeground);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatGibibytes(long bytes)
    {
        var value = bytes / (double)AiProfileRecommendationPolicy.Gibibyte;
        var format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, CultureInfo.CurrentCulture) + " GB";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMilliseconds(100)) return "menos de 0,1 s";
        var format = duration.TotalSeconds < 10 ? "0.0" : "0";
        return duration.TotalSeconds.ToString(format, CultureInfo.CurrentCulture) + " s";
    }

    private static string FormatTokenRate(double tokensPerSecond) =>
        tokensPerSecond.ToString(tokensPerSecond >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) +
        " tokens/s";

    private static string ProfileName(AiProfile profile) => profile switch
    {
        AiProfile.Lightweight => "Leve",
        AiProfile.Balanced => "Equilibrado",
        AiProfile.Performance => "Desempenho",
        _ => "Automático"
    };
}
