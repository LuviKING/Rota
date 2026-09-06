using System.Globalization;
using System.Windows;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class AiDiagnosticsWindow : Window
{
    private readonly IAiAssistantController _controller;
    private readonly IAiHardwareDiagnosticsService _diagnosticsService;
    private CancellationTokenSource? _refreshCancellation;
    private bool _initializationStarted;
    private bool _refreshing;
    private string _diagnosticWarning = "";

    public AiDiagnosticsWindow(
        IAiAssistantController controller,
        IAiHardwareDiagnosticsService? diagnosticsService = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _diagnosticsService = diagnosticsService ?? new AiHardwareDiagnosticsService();
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

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        _diagnosticWarning = "";
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
                _diagnosticWarning = "O Rota conteve uma falha ao consultar o hardware. Tente verificar novamente.";
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
        RefreshButton.IsEnabled = !_refreshing && !state.IsBusy;

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

        var warning = string.IsNullOrWhiteSpace(_diagnosticWarning)
            ? state.Warnings.LastOrDefault() ?? ""
            : _diagnosticWarning;
        WarningNotice.Visibility = warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = warning;
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

        _diagnosticWarning = report.Warnings.LastOrDefault() ?? "";
        HardwareDetailsPanel.Visibility = Visibility.Visible;
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

    private static string ProfileName(AiProfile profile) => profile switch
    {
        AiProfile.Lightweight => "Leve",
        AiProfile.Balanced => "Equilibrado",
        AiProfile.Performance => "Desempenho",
        _ => "Automático"
    };
}
