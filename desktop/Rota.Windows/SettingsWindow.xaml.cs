using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Rota.Desktop;

public partial class SettingsWindow : Window
{
    private readonly StudyRepository _repository;
    private readonly IStudyReminderScheduler _reminderScheduler;
    private readonly string _executablePath;
    private readonly IAppUpdateService? _updateService;

    public SettingsWindow(
        StudyRepository repository,
        IStudyReminderScheduler? reminderScheduler = null,
        string? executablePath = null,
        IAppUpdateService? updateService = null)
    {
        _repository = repository;
        _reminderScheduler = reminderScheduler ?? new WindowsStudyReminderScheduler();
        _executablePath = executablePath ?? Environment.ProcessPath ?? "Rota.exe";
        _updateService = updateService;
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
        ReminderEnabled.IsChecked = settings.ReminderEnabled;
        ReminderTimeBox.Text = settings.ReminderTime;
        var availableDays = settings.AvailableStudyDays.ToHashSet();
        foreach (var (choice, day) in DayChoices())
            choice.IsChecked = availableDays.Contains(day);
        DataPathText.Text = repository.DataPath;
        UpdateSliderLabels();
    }

    private void HoursSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();
    private void BlockSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();

    private void UpdateSliderLabels()
    {
        if (HoursValue is null || BlockValue is null) return;
        HoursValue.Text = $"{HoursSlider.Value:0.##} h/dia";
        BlockValue.Text = $"{(int)Math.Round(BlockSlider.Value)} min";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var previous = StudyRepository.CopySettings(_repository.Settings);
        try
        {
            var reminder = StudyReminderConfiguration.Parse(ReminderEnabled.IsChecked == true, ReminderTimeBox.Text);
            _repository.SavePreferences(
                ObjectiveBox.Text,
                ObjectiveDateBox.Text.Trim(),
                HoursSlider.Value,
                (int)Math.Round(BlockSlider.Value / 15.0) * 15,
                ReviewD1.IsChecked == true,
                ReviewD3.IsChecked == true,
                ReviewD7.IsChecked == true,
                SelectedDays(),
                reminder.Enabled,
                reminder.TimeText);
            try
            {
                _reminderScheduler.Apply(reminder, _executablePath);
            }
            catch (Exception scheduleError)
            {
                try
                {
                    RestorePreferences(previous);
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        "O lembrete falhou e o Rota também não conseguiu restaurar as preferências anteriores.",
                        new AggregateException(scheduleError, rollbackError));
                }
                throw;
            }
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ex.ParamName == "time" || ex.Message.Contains("horário", StringComparison.OrdinalIgnoreCase))
                ReminderTimeBox.Focus();
            else if (ex.Message.Contains("dia de estudo", StringComparison.OrdinalIgnoreCase))
                MondayChoice.Focus();
            else
                (ex.Message.Contains("Nome do objetivo", StringComparison.Ordinal) ? ObjectiveBox : ObjectiveDateBox).Focus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show("Não foi possível concluir o salvamento das configurações.\n\n" + ex.Message, "Configurações", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            MessageBox.Show("As configurações anteriores foram mantidas porque o lembrete não pôde ser atualizado.\n\n" + ex.Message, "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_repository.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _repository.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                "Não foi possível abrir a pasta de dados.\n\n" + ex.Message,
                "Rota",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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

    private void RestorePreferences(AppSettings settings) => _repository.SavePreferences(
        settings.ObjectiveName,
        settings.ObjectiveDate,
        settings.DailyHours,
        settings.BlockMinutes,
        settings.ReviewD1,
        settings.ReviewD3,
        settings.ReviewD7,
        settings.AvailableStudyDays,
        settings.ReminderEnabled,
        settings.ReminderTime);

    private List<DayOfWeek> SelectedDays() => DayChoices()
        .Where(choice => choice.Button.IsChecked == true)
        .Select(choice => choice.Day)
        .ToList();

    private IEnumerable<(ToggleButton Button, DayOfWeek Day)> DayChoices()
    {
        yield return (MondayChoice, DayOfWeek.Monday);
        yield return (TuesdayChoice, DayOfWeek.Tuesday);
        yield return (WednesdayChoice, DayOfWeek.Wednesday);
        yield return (ThursdayChoice, DayOfWeek.Thursday);
        yield return (FridayChoice, DayOfWeek.Friday);
        yield return (SaturdayChoice, DayOfWeek.Saturday);
        yield return (SundayChoice, DayOfWeek.Sunday);
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var window = new AppUpdateWindow(_updateService) { Owner = this };
        window.ShowDialog();
    }
}

