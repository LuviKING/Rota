using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Rota.Desktop;

public partial class OnboardingRoutineWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly StudyRepository _repository;
    private bool _initialized;

    public OnboardingRoutineWindow(StudyRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        LoadCurrentRoutine();
        _initialized = true;
        UpdateFormState();
    }

    public bool RoutineCompleted { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DeadlinePicker.ApplyTemplate();
        var calendarButton = FindVisualChild<Button>(DeadlinePicker, "PART_Button");
        if (calendarButton is not null)
        {
            calendarButton.MinWidth = 40;
            calendarButton.MinHeight = 40;
            calendarButton.Padding = new Thickness(0);
            calendarButton.BorderThickness = new Thickness(0);
            calendarButton.Background = Brushes.Transparent;
            calendarButton.Template = FindResource("RoutineCalendarButtonTemplate") as ControlTemplate;
        }

        var dateTextBox = FindVisualChild<DatePickerTextBox>(DeadlinePicker, "PART_TextBox");
        if (dateTextBox is null) return;
        dateTextBox.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        dateTextBox.SetResourceReference(Control.BackgroundProperty, "SurfaceRaisedBrush");
        dateTextBox.BorderThickness = new Thickness(0);
        UpdateDateWatermark();
    }

    private void LoadCurrentRoutine()
    {
        var settings = _repository.Settings;
        ObjectiveBox.Text = string.Equals(settings.ObjectiveName, "Meu objetivo", StringComparison.Ordinal)
            ? ""
            : settings.ObjectiveName;

        var tomorrow = DateTime.Today.AddDays(1);
        DeadlinePicker.DisplayDateStart = tomorrow;
        if (DateOnly.TryParseExact(
                settings.ObjectiveDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var storedDeadline) && storedDeadline > DateOnly.FromDateTime(DateTime.Today))
        {
            DeadlinePicker.SelectedDate = storedDeadline.ToDateTime(TimeOnly.MinValue);
            DeadlinePicker.DisplayDate = DeadlinePicker.SelectedDate.Value;
        }
        else
        {
            DeadlinePicker.DisplayDate = DateTime.Today.AddMonths(6);
        }

        HoursSlider.Value = settings.DailyHours;
        var availableDays = settings.AvailableStudyDays.ToHashSet();
        foreach (var (choice, day) in DayChoices())
            choice.IsChecked = availableDays.Contains(day);
    }

    private void FormInput_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized) UpdateFormState();
    }

    private void UpdateFormState()
    {
        var selectedDays = SelectedDays();
        var hours = Math.Round(HoursSlider.Value * 2, MidpointRounding.AwayFromZero) / 2d;
        HoursValueText.Text = hours == 1 ? "1 h por dia" : $"{hours:0.#} h por dia";
        WeeklyCapacityText.Text = selectedDays.Count == 0
            ? "Escolha ao menos um dia para calcular sua disponibilidade semanal."
            : $"{selectedDays.Count} {(selectedDays.Count == 1 ? "dia" : "dias")} × {hours:0.#} h = {selectedDays.Count * hours:0.#} h disponíveis por semana";

        var hasObjective = !string.IsNullOrWhiteSpace(ObjectiveBox.Text);
        var hasFutureDeadline = DeadlinePicker.SelectedDate is DateTime deadline && deadline.Date > DateTime.Today;
        var ready = hasObjective && hasFutureDeadline && selectedDays.Count > 0;
        SaveButton.IsEnabled = ready;
        FormHintText.Text = !hasObjective
            ? "Digite o nome do seu objetivo."
            : !hasFutureDeadline
                ? "Escolha uma data posterior a hoje."
                : selectedDays.Count == 0
                    ? "Escolha pelo menos um dia de estudo."
                    : "Tudo pronto para salvar sua rotina.";
        ValidationPanel.Visibility = Visibility.Collapsed;
        UpdateDateWatermark();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled || DeadlinePicker.SelectedDate is not DateTime deadline) return;

        try
        {
            _repository.SaveOnboardingRoutine(
                ObjectiveBox.Text,
                DateOnly.FromDateTime(deadline).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HoursSlider.Value,
                SelectedDays());
            RoutineCompleted = true;
            Close();
        }
        catch (ArgumentException ex)
        {
            ShowValidation(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            ShowValidation(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Não foi possível salvar sua rotina. Os valores anteriores foram mantidos.\n\n" + ex.Message,
                "Configuração inicial · Rota",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationPanel.Visibility = Visibility.Visible;
        ValidationPanel.BringIntoView();
    }

    private void NotNow_Click(object sender, RoutedEventArgs e) => Close();

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

    private void UpdateDateWatermark()
    {
        DeadlinePicker.ApplyTemplate();
        var watermark = FindVisualChild<ContentControl>(DeadlinePicker, "PART_Watermark");
        if (watermark is not null)
            watermark.Visibility = DeadlinePicker.SelectedDate is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal)) return match;
            var nested = FindVisualChild<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }
}
