using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class AiInstallationWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly IAiInstallationController _controller;
    private bool _loaded;
    private bool _allowClose;

    public AiInstallationWindow(IAiInstallationController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        _controller.StateChanged += Controller_StateChanged;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        Closing += Window_Closing;
        RefreshState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await AnalyzeSelectedProfileAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _controller.StateChanged -= Controller_StateChanged;
        Loaded -= Window_Loaded;
        Closed -= Window_Closed;
        Closing -= Window_Closing;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_controller.State.IsBusy) return;
        e.Cancel = true;
        _controller.CancelCurrentOperation();
        StatusText.Text = "Cancelando com segurança…";
    }

    private void Controller_StateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RefreshState();
        else _ = Dispatcher.BeginInvoke(RefreshState);
    }

    private void RefreshState()
    {
        var state = _controller.State;
        var plan = state.Plan;
        StatusText.Text = state.StatusMessage;
        PlanPanel.Visibility = plan is null ? Visibility.Collapsed : Visibility.Visible;
        if (plan is not null)
        {
            EffectiveProfileText.Text = ProfileName(plan.EffectiveProfile) +
                (plan.RequestedProfile == AiProfile.Automatic ? " · recomendado" : " · escolha manual");
            ComputeText.Text = plan.ComputePreference == AiComputePreference.Gpu
                ? "Aceleração pela placa de vídeo"
                : "Processamento pelo computador";
            ModelText.Text = plan.ModelName;
            DownloadSizeText.Text = FormatBytes(plan.DownloadBytes);
            StorageSizeText.Text = FormatBytes(plan.RecommendedFreeBytes);
            InstallPathText.Text = plan.InstallationDirectory;
        }

        var progress = state.ProgressFraction * 100;
        InstallationProgressBar.Value = progress;
        ProgressPercentText.Text = state.Activity is AiInstallationActivity.Installing or AiInstallationActivity.Completed
            ? $"{progress:0}%"
            : "";
        ProgressBytesText.Text = state.TotalDownloadBytes > 0 && state.Activity == AiInstallationActivity.Installing
            ? $"{FormatBytes(state.BytesReceived)} de {FormatBytes(state.TotalDownloadBytes)}"
            : "";
        WarningPanel.Visibility = state.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = state.Warnings.Count == 0 ? "" : state.Warnings[^1];

        SetProfileChoicesEnabled(!state.IsBusy && state.Activity != AiInstallationActivity.Completed);
        AnalyzeButton.IsEnabled = !state.IsBusy && state.Activity != AiInstallationActivity.Completed;
        InstallButton.IsEnabled = state.CanInstall;
        CancelButton.Content = state.IsBusy ? "Cancelar" : state.Activity == AiInstallationActivity.Completed ? "Concluir" : "Fechar";
        InstallButtonText.Text = state.Activity == AiInstallationActivity.Completed ? "Instalação concluída" : "Baixar e instalar";
    }

    private void SetProfileChoicesEnabled(bool enabled)
    {
        AutomaticChoice.IsEnabled = enabled;
        LightweightChoice.IsEnabled = enabled;
        BalancedChoice.IsEnabled = enabled;
        PerformanceChoice.IsEnabled = enabled;
    }

    private async void ProfileChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _controller.State.IsBusy) return;
        await AnalyzeSelectedProfileAsync();
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeSelectedProfileAsync();

    private async Task AnalyzeSelectedProfileAsync()
    {
        var profile = SelectedProfile();
        await _controller.PrepareAsync(profile);
        if (IsLoaded) RefreshState();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var plan = _controller.State.Plan;
        if (plan is null || !_controller.State.CanInstall) return;

        var answer = MessageBox.Show(
            $"Baixar e instalar a IA local?\n\n" +
            $"Perfil: {ProfileName(plan.EffectiveProfile)}\n" +
            $"Modelo: {plan.ModelName}\n" +
            $"Download: {FormatBytes(plan.DownloadBytes)}\n" +
            $"Pasta: {plan.InstallationDirectory}\n\n" +
            "O download pode levar alguns minutos. Você poderá cancelar durante o processo.",
            "Confirmar instalação da IA local",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var result = await _controller.InstallAsync(plan.ConfirmationId);
        if (result is not null && IsLoaded) RefreshState();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.State.IsBusy)
        {
            _controller.CancelCurrentOperation();
            return;
        }
        _allowClose = true;
        if (_controller.State.Activity == AiInstallationActivity.Completed) DialogResult = true;
        else Close();
    }

    private AiProfile SelectedProfile()
    {
        var selected = new[] { AutomaticChoice, LightweightChoice, BalancedChoice, PerformanceChoice }
            .FirstOrDefault(choice => choice.IsChecked == true);
        return selected?.Tag is string value && Enum.TryParse(value, out AiProfile profile)
            ? profile
            : AiProfile.Automatic;
    }

    private static string ProfileName(AiProfile profile) => profile switch
    {
        AiProfile.Lightweight => "Leve",
        AiProfile.Balanced => "Equilibrado",
        AiProfile.Performance => "Desempenho",
        _ => "Automático"
    };

    private static string FormatBytes(long bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double mib = 1024d * 1024d;
        return bytes >= gib
            ? (bytes / gib).ToString("0.00", PtBr) + " GB"
            : (bytes / mib).ToString("0", PtBr) + " MB";
    }
}
