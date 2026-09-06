using System.Windows;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class AiDiagnosticsWindow : Window
{
    private readonly IAiAssistantController _controller;
    private bool _initializationStarted;
    private bool _refreshing;

    public AiDiagnosticsWindow(IAiAssistantController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
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
        RefreshState();
        try
        {
            await _controller.InitializeAsync();
        }
        finally
        {
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

        var warning = state.Warnings.LastOrDefault() ?? "";
        WarningNotice.Visibility = warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = warning;
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

    private static string ProfileName(AiProfile profile) => profile switch
    {
        AiProfile.Lightweight => "Leve",
        AiProfile.Balanced => "Equilibrado",
        AiProfile.Performance => "Desempenho",
        _ => "Automático"
    };
}
