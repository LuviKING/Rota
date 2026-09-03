using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;

namespace Rota.Desktop;

public partial class SettingsWindow : Window
{
    private readonly StudyRepository _repository;

    public SettingsWindow(StudyRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        var settings = repository.Settings;
        ObjectiveBox.Text = settings.ObjectiveName;
        ObjectiveDateBox.Text = settings.ObjectiveDate;
        HoursSlider.Value = settings.DailyHours;
        BlockSlider.Value = settings.BlockMinutes;
        ReviewD1.IsChecked = settings.ReviewD1;
        ReviewD3.IsChecked = settings.ReviewD3;
        ReviewD7.IsChecked = settings.ReviewD7;
        DataPathText.Text = repository.DataPath;
        UpdateSliderLabels();
    }

    private void HoursSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();
    private void BlockSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();

    private void UpdateSliderLabels()
    {
        if (HoursValue is null || BlockValue is null) return;
        HoursValue.Text = $"{(int)Math.Round(HoursSlider.Value)} h/dia";
        BlockValue.Text = $"{(int)Math.Round(BlockSlider.Value)} min";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _repository.SavePreferences(
                ObjectiveBox.Text,
                ObjectiveDateBox.Text.Trim(),
                (int)Math.Round(HoursSlider.Value),
                (int)Math.Round(BlockSlider.Value / 15.0) * 15,
                ReviewD1.IsChecked == true,
                ReviewD3.IsChecked == true,
                ReviewD7.IsChecked == true);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            (ex.Message.Contains("Nome do objetivo", StringComparison.Ordinal) ? ObjectiveBox : ObjectiveDateBox).Focus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Não foi possível salvar as configurações. Os valores anteriores foram mantidos.\n\n" + ex.Message, "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_repository.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _repository.DataDirectory) { UseShellExecute = true });
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar backup do Rota",
            Filter = "Backup JSON (*.json)|*.json",
            FileName = $"Rota-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _repository.ExportBackup(dialog.FileName);
            MessageBox.Show("Backup exportado com sucesso.", "Rota", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Não foi possível exportar o backup.\n\n" + ex.Message, "Rota", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

