using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;

namespace Rota.Desktop;

public partial class AppUpdateWindow : Window
{
    private readonly IAppUpdateService _updateService;
    private readonly CancellationTokenSource _lifetime = new();
    private AppUpdateInfo? _availableUpdate;
    private bool _busy;

    public AppUpdateWindow(IAppUpdateService? updateService = null)
    {
        _updateService = updateService ?? AppUpdateService.CreateDefault();
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        var current = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
        VersionBadgeText.Text = $"VERSÃO ATUAL · {current.Major}.{current.Minor}.{Math.Max(0, current.Build)}";
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_availableUpdate is null)
            await CheckAsync();
        else
            await DownloadAndInstallAsync(_availableUpdate);
    }

    private async Task CheckAsync()
    {
        SetBusy(true, "Verificando…");
        StatusTitleText.Text = "Procurando uma versão mais recente";
        StatusDetailText.Text = "Consultando o canal oficial do Rota…";
        try
        {
            var update = await _updateService.CheckAsync(_lifetime.Token);
            if (!update.IsUpdateAvailable)
            {
                StatusTitleText.Text = "Você já está na versão mais recente";
                StatusDetailText.Text = $"Versão instalada: {FormatVersion(update.CurrentVersion)}.";
                NotesText.Text = "Nenhuma atualização precisa ser instalada agora.";
                return;
            }

            _availableUpdate = update;
            StatusTitleText.Text = $"Rota {FormatVersion(update.LatestVersion)} disponível";
            StatusDetailText.Text = $"Pacote de {FormatBytes(update.InstallerBytes)} pronto para baixar após sua confirmação.";
            NotesText.Text = update.Notes;
            PrimaryActionButton.Content = "Baixar e instalar";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UriFormatException or TaskCanceledException)
        {
            StatusTitleText.Text = "Não foi possível verificar agora";
            StatusDetailText.Text = "O canal pode estar indisponível ou exigir acesso ao repositório privado.";
            NotesText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false, _availableUpdate is null ? "Verificar novamente" : "Baixar e instalar");
        }
    }

    private async Task DownloadAndInstallAsync(AppUpdateInfo update)
    {
        var answer = MessageBox.Show(
            $"Baixar {FormatBytes(update.InstallerBytes)}, verificar o arquivo e abrir o instalador do Rota {FormatVersion(update.LatestVersion)}?\n\nO calendário e o histórico local serão preservados.",
            "Instalar atualização",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        SetBusy(true, "Baixando…");
        StatusTitleText.Text = "Baixando e verificando a atualização";
        StatusDetailText.Text = "Não feche esta janela enquanto o pacote é conferido.";
        try
        {
            var progress = new Progress<double>(value => DownloadProgress.Value = Math.Clamp(value * 100, 0, 100));
            var download = await _updateService.DownloadAsync(update, progress, _lifetime.Token);
            StatusTitleText.Text = "Atualização verificada";
            StatusDetailText.Text = "O instalador íntegro será aberto agora. Confirme também as telas do Windows.";
            _updateService.LaunchInstaller(download);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or Win32Exception or TaskCanceledException)
        {
            StatusTitleText.Text = "A atualização não foi instalada";
            StatusDetailText.Text = "O Rota atual continua intacto.";
            NotesText.Text = ex.Message;
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false, "Tentar novamente");
        }
    }

    private void SetBusy(bool busy, string buttonText)
    {
        _busy = busy;
        PrimaryActionButton.IsEnabled = !busy;
        PrimaryActionButton.Content = buttonText;
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private static string FormatBytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.0 MB", CultureInfo.GetCultureInfo("pt-BR"));
}
