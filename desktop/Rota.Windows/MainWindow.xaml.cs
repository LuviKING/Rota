using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly StudyRepository _repository;
    private readonly IAiAssistantController _aiAssistantController;
    private readonly IAiInstallationController _aiInstallationController;
    private readonly IAiProposalApplicationService _aiProposalApplicationService;
    private readonly IAiHardwareDiagnosticsService _aiHardwareDiagnosticsService;
    private readonly IAiPerformanceDiagnosticsService? _aiPerformanceDiagnosticsService;
    private DateOnly _selectedDate;
    private DateOnly _displayMonth;
    private string _theme;
    private Point? _sessionDragStart;

    public ObservableCollection<MonthDayCardView> MonthDays { get; } = new();
    public ObservableCollection<MonthTabView> MonthTabs { get; } = new();
    public ObservableCollection<SessionCardView> DaySessions { get; } = new();
    public ObservableCollection<OverviewCardView> OverviewCards { get; } = new();

    private string _selectedDateHeading = "";
    public string SelectedDateHeading { get => _selectedDateHeading; private set => Set(ref _selectedDateHeading, value); }

    private string _selectedDateMeta = "";
    public string SelectedDateMeta { get => _selectedDateMeta; private set => Set(ref _selectedDateMeta, value); }

    private string _objectiveTitle = "";
    public string ObjectiveTitle { get => _objectiveTitle; private set => Set(ref _objectiveTitle, value); }

    private string _objectiveDateDisplay = "";
    public string ObjectiveDateDisplay { get => _objectiveDateDisplay; private set => Set(ref _objectiveDateDisplay, value); }

    private string _planLabel = "";
    public string PlanLabel { get => _planLabel; private set => Set(ref _planLabel, value); }

    private string _planContextLabel = "";
    public string PlanContextLabel { get => _planContextLabel; private set => Set(ref _planContextLabel, value); }

    private string _monthTitle = "";
    public string MonthTitle { get => _monthTitle; private set => Set(ref _monthTitle, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    private Visibility _emptyVisibility;
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    private Visibility _undoMoveVisibility;
    public Visibility UndoMoveVisibility { get => _undoMoveVisibility; private set => Set(ref _undoMoveVisibility, value); }

    private Visibility _overdueStatusVisibility;
    public Visibility OverdueStatusVisibility { get => _overdueStatusVisibility; private set => Set(ref _overdueStatusVisibility, value); }

    private string _overdueStatusTitle = "";
    public string OverdueStatusTitle { get => _overdueStatusTitle; private set => Set(ref _overdueStatusTitle, value); }

    private string _overdueStatusDetail = "";
    public string OverdueStatusDetail { get => _overdueStatusDetail; private set => Set(ref _overdueStatusDetail, value); }

    private Visibility _darkThemeSelectedVisibility;
    public Visibility DarkThemeSelectedVisibility { get => _darkThemeSelectedVisibility; private set => Set(ref _darkThemeSelectedVisibility, value); }

    private Visibility _lightThemeSelectedVisibility;
    public Visibility LightThemeSelectedVisibility { get => _lightThemeSelectedVisibility; private set => Set(ref _lightThemeSelectedVisibility, value); }

    private string _themeGlyph = "☾";
    public string ThemeGlyph { get => _themeGlyph; private set => Set(ref _themeGlyph, value); }

    private string _themeLabel = "Escuro";
    public string ThemeLabel { get => _themeLabel; private set => Set(ref _themeLabel, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(
        StudyRepository repository,
        IAiAssistantController aiAssistantController,
        IAiInstallationController aiInstallationController,
        IAiProposalApplicationService aiProposalApplicationService,
        IAiHardwareDiagnosticsService? aiHardwareDiagnosticsService = null,
        IAiPerformanceDiagnosticsService? aiPerformanceDiagnosticsService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _aiAssistantController = aiAssistantController ?? throw new ArgumentNullException(nameof(aiAssistantController));
        _aiInstallationController = aiInstallationController ?? throw new ArgumentNullException(nameof(aiInstallationController));
        _aiProposalApplicationService = aiProposalApplicationService ?? throw new ArgumentNullException(nameof(aiProposalApplicationService));
        _aiHardwareDiagnosticsService = aiHardwareDiagnosticsService ?? new AiHardwareDiagnosticsService();
        _aiPerformanceDiagnosticsService = aiPerformanceDiagnosticsService;
        _selectedDate = DateOnly.FromDateTime(DateTime.Today);
        _displayMonth = new DateOnly(_selectedDate.Year, _selectedDate.Month, 1);
        _theme = ThemeManager.LoadPreference(_repository.DataDirectory);
        ThemeManager.Apply(_theme);

        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        DataContext = this;
        RefreshAll();

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_repository.LastLoadWarning))
                MessageBox.Show(_repository.LastLoadWarning, "Rota", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
    }

    private void RefreshAll()
    {
        var settings = _repository.Settings;
        var progress = _repository.ProgressForDate(_selectedDate);
        var sessions = _repository.SessionsForDate(_selectedDate);

        SelectedDateHeading = FormatDayHeading(_selectedDate);
        SelectedDateMeta = progress.Total == 0
            ? "dia livre"
            : $"{progress.Total} {(progress.Total == 1 ? "bloco" : "blocos")} · {progress.Minutes} min";
        ProgressText = progress.Total == 0 ? "0%" : $"{Math.Round(progress.Fraction * 100):0}%";

        ObjectiveTitle = settings.ObjectiveName;
        ObjectiveDateDisplay = FormatObjectiveDate(settings.ObjectiveDate);
        PlanLabel = string.IsNullOrWhiteSpace(settings.ActivePlanId)
            ? "Nenhum StudyPlan importado"
            : $"{settings.ActivePlanTitle}\n{settings.ActivePlanId} · revisão {settings.ActivePlanRevision}";
        PlanContextLabel = string.IsNullOrWhiteSpace(settings.ActivePlanId)
            ? "Sem StudyPlan ativo"
            : settings.ActivePlanTitle;

        DaySessions.Clear();
        foreach (var session in sessions)
            DaySessions.Add(new SessionCardView(session));
        EmptyVisibility = DaySessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoMoveVisibility = _repository.CanUndoSessionMove ? Visibility.Visible : Visibility.Collapsed;

        RefreshThemeProperties();
        RefreshOverdueStatus();
        RefreshMonth();
        RefreshOverviewCards();
    }

    private void RefreshOverdueStatus()
    {
        var overdue = OverdueStudyAnalyzer.Analyze(_repository.CaptureApplicationSnapshot());
        OverdueStatusVisibility = overdue.HasOverdue ? Visibility.Visible : Visibility.Collapsed;
        OverdueStatusTitle = overdue.HasOverdue
            ? $"{overdue.TotalCount} {(overdue.TotalCount == 1 ? "bloco atrasado" : "blocos atrasados")} · {overdue.TotalMinutes} min"
            : "";
        OverdueStatusDetail = overdue.OldestDate is null
            ? ""
            : $"O mais antigo estava previsto para {overdue.OldestDate.Value.ToString("dd/MM/yyyy", PtBr)}. " +
              "O Rota apenas identificou o atraso; nenhuma data foi alterada.";
    }

    private void RefreshMonth()
    {
        MonthTitle = Capitalize(_displayMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", PtBr));

        MonthTabs.Clear();
        for (var offset = -1; offset <= 2; offset++)
        {
            var month = _displayMonth.AddMonths(offset);
            MonthTabs.Add(new MonthTabView(month, month == _displayMonth));
        }

        var first = _displayMonth;
        var mondayOffset = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-mondayOffset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        MonthDays.Clear();
        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index);
            var sessions = _repository.SessionsForDate(date);
            MonthDays.Add(new MonthDayCardView(
                date,
                sessions,
                date.Month == _displayMonth.Month && date.Year == _displayMonth.Year,
                date == _selectedDate,
                date == today));
        }
    }

    private void RefreshOverviewCards()
    {
        var settings = _repository.Settings;
        var days = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        var total = 0;
        var completed = 0;
        var minutes = 0;

        for (var day = 1; day <= days; day++)
        {
            var progress = _repository.ProgressForDate(new DateOnly(_displayMonth.Year, _displayMonth.Month, day));
            total += progress.Total;
            completed += progress.Completed;
            minutes += progress.Minutes;
        }

        var percent = total == 0 ? 0 : (int)Math.Round((double)completed / total * 100);
        var monthName = Capitalize(_displayMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM", PtBr));

        OverviewCards.Clear();
        OverviewCards.Add(new OverviewCardView(
            "PLANO ATIVO",
            string.IsNullOrWhiteSpace(settings.ActivePlanTitle) ? "Sem plano importado" : settings.ActivePlanTitle,
            string.IsNullOrWhiteSpace(settings.ActivePlanId) ? "Importe um StudyPlan para começar" : $"{settings.ActivePlanId} · revisão {settings.ActivePlanRevision}"));
        OverviewCards.Add(new OverviewCardView(
            "OBJETIVO",
            settings.ObjectiveName,
            FormatObjectiveDate(settings.ObjectiveDate)));
        OverviewCards.Add(new OverviewCardView(
            monthName.ToUpper(PtBr),
            total == 0 ? "Sem blocos planejados" : $"{total} {(total == 1 ? "bloco" : "blocos")} · {minutes} min",
            "Visão mensal do calendário"));
        OverviewCards.Add(new OverviewCardView(
            "PROGRESSO DO MÊS",
            total == 0 ? "0%" : $"{percent}%",
            total == 0 ? "Nenhuma sessão neste mês" : $"{completed} de {total} concluídos"));
    }

    private void RefreshThemeProperties()
    {
        var dark = _theme == ThemeManager.Dark;
        DarkThemeSelectedVisibility = dark ? Visibility.Visible : Visibility.Collapsed;
        LightThemeSelectedVisibility = dark ? Visibility.Collapsed : Visibility.Visible;
        ThemeGlyph = dark ? "☾" : "☀";
        ThemeLabel = dark ? "Escuro" : "Claro";
    }

    private void SetTheme(string theme)
    {
        var normalized = ThemeManager.Normalize(theme);
        if (_theme == normalized) return;

        _theme = normalized;
        ThemeManager.Apply(_theme);
        try
        {
            ThemeManager.SavePreference(_repository.DataDirectory, _theme);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "O tema foi aplicado, mas o Rota não conseguiu salvar essa preferência para a próxima abertura.\n\n" + ex.Message,
                "Tema do Rota",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        RefreshAll();
    }

    private void SetSelectedDate(DateOnly date)
    {
        _selectedDate = date;
        _displayMonth = new DateOnly(date.Year, date.Month, 1);
        RefreshAll();
    }

    private void SetDisplayMonth(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        _displayMonth = first;

        if (_selectedDate.Year != first.Year || _selectedDate.Month != first.Month)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            _selectedDate = today.Year == first.Year && today.Month == first.Month ? today : first;
        }

        RefreshAll();
    }

    private void TodayNav_Click(object sender, RoutedEventArgs e) =>
        SetSelectedDate(DateOnly.FromDateTime(DateTime.Today));

    private void CalendarNav_Click(object sender, RoutedEventArgs e)
    {
        CalendarHost.BringIntoView();
        Keyboard.Focus(this);
    }

    private void MonthDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string iso } &&
            DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            SetSelectedDate(date);
    }

    private void SessionCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _sessionDragStart = e.GetPosition(this);
    }

    private void SessionCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _sessionDragStart is not Point start ||
            sender is not FrameworkElement { DataContext: SessionCardView card } || !card.CanMove)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _sessionDragStart = null;
        DragDrop.DoDragDrop((DependencyObject)sender, card, DragDropEffects.Move);
    }

    private void MonthDay_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is Button { Tag: string iso } &&
                    DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                    e.Data.GetDataPresent(typeof(SessionCardView))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void MonthDay_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string iso } ||
            !DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var targetDate) ||
            e.Data.GetData(typeof(SessionCardView)) is not SessionCardView card)
            return;

        try
        {
            var preview = _repository.PreviewSessionMove(card.PlanId, card.Id, targetDate);
            if (preview.AlreadyHandled)
            {
                SetSelectedDate(targetDate);
                return;
            }
            if (!preview.Success)
            {
                MessageBox.Show(preview.Message, "Mover sessão", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = new SessionMoveConfirmationWindow(card, preview) { Owner = this };
            if (confirmation.ShowDialog() != true) return;

            var result = _repository.MoveSession(card.PlanId, card.Id, targetDate, preview.MutationVersion);
            if (result.Success)
            {
                SetSelectedDate(targetDate);
                return;
            }
            MessageBox.Show(result.Message, "Mover sessão", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Não foi possível salvar a nova data. A sessão não foi alterada.\n\n" + ex.Message,
                "Mover sessão",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UndoSessionMove_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Deseja devolver a última sessão movida para a data anterior?",
            "Desfazer movimento",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var result = _repository.UndoLastSessionMove();
            if (result.Success && DateOnly.TryParseExact(
                    result.TargetDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var restoredDate))
            {
                SetSelectedDate(restoredDate);
                return;
            }

            RefreshAll();
            MessageBox.Show(result.Message, "Desfazer movimento", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Não foi possível desfazer. O calendário atual foi preservado.\n\n" + ex.Message,
                "Desfazer movimento",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MonthTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string iso } &&
            DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            SetDisplayMonth(month);
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e) =>
        SetDisplayMonth(_displayMonth.AddMonths(-1));

    private void NextMonth_Click(object sender, RoutedEventArgs e) =>
        SetDisplayMonth(_displayMonth.AddMonths(1));

    private void ThemeToggle_Click(object sender, RoutedEventArgs e) =>
        SetTheme(_theme == ThemeManager.Dark ? ThemeManager.Light : ThemeManager.Dark);

    private void ThemeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string theme })
            SetTheme(theme);
    }

    private void AiNav_Click(object sender, RoutedEventArgs e)
        => OpenAiAssistant();

    private void OpenAiAssistant(
        string? initialRequest = null,
        AiProposalKind initialProposalKind = AiProposalKind.StudyPlan)
    {
        var dialog = new AiAssistantWindow(
            _aiAssistantController,
            _aiInstallationController,
            _aiProposalApplicationService,
            _repository,
            _aiHardwareDiagnosticsService,
            _aiPerformanceDiagnosticsService,
            initialRequest,
            initialProposalKind)
        { Owner = this };
        dialog.ShowDialog();
        RefreshAll();
    }

    private void WeeklySummary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WeeklySummaryWindow(_repository.CaptureApplicationSnapshot()) { Owner = this };
        dialog.ShowDialog();
    }

    private void OverdueRecovery_Click(object sender, RoutedEventArgs e)
    {
        var overdue = OverdueStudyAnalyzer.Analyze(_repository.CaptureApplicationSnapshot());
        if (!overdue.HasOverdue)
        {
            RefreshAll();
            return;
        }

        var dialog = new OverdueRecoveryWindow(overdue) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.OpenAssistantRequested)
        {
            OpenAiAssistant(
                OverdueRecoveryPresentation.BuildAiRequest(overdue),
                AiProposalKind.PlanChanges);
            return;
        }
        if (dialog.SelectedDate is DateOnly date)
        {
            SetSelectedDate(date);
            CalendarHost.BringIntoView();
        }
    }

    private void ImportNav_Click(object sender, RoutedEventArgs e) => ShowImport();

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_repository) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshAll();
    }

    private void CompleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SessionCardView card }) return;

        var answer = MessageBox.Show(
            $"Marcar como concluído?\n\n{card.Subject}\n{card.Topic}\n\nSe for um bloco de estudo, as revisões automáticas serão calculadas a partir de agora.",
            "Concluir sessão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (_repository.MarkCompleted(card.PlanId, card.Id))
                RefreshAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Não foi possível salvar a conclusão. A sessão permanece pendente.\n\n" + ex.Message,
                "Rota",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.L)
        {
            ThemeToggle_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.T:
                SetSelectedDate(DateOnly.FromDateTime(DateTime.Today));
                e.Handled = true;
                break;
            case Key.I:
                ShowImport();
                e.Handled = true;
                break;
            case Key.G:
                AiNav_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.OemComma:
                SettingsNav_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private void ShowImport(string? initialText = null)
    {
        var dialog = new ImportPlanWindow(_repository, initialText) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var settings = _repository.Settings;
            if (DateOnly.TryParseExact(settings.ObjectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var objective) &&
                _selectedDate > objective)
                SetSelectedDate(DateOnly.FromDateTime(DateTime.Today));
            else
                RefreshAll();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasJsonFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!HasJsonFile(e.Data)) return;

        try
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var path = files.First(p => string.Equals(Path.GetExtension(p), ".json", StringComparison.OrdinalIgnoreCase));
            if (new FileInfo(path).Length > 1_048_576)
                throw new InvalidOperationException("O arquivo excede o limite seguro de importação.");
            ShowImport(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Importar StudyPlan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool HasJsonFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        return ((string[])data.GetData(DataFormats.FileDrop)!)
            .Any(p => string.Equals(Path.GetExtension(p), ".json", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatDayHeading(DateOnly date)
    {
        var asDateTime = date.ToDateTime(TimeOnly.MinValue);
        var weekday = Capitalize(asDateTime.ToString("dddd", PtBr));
        var month = Capitalize(asDateTime.ToString("MMMM", PtBr));
        return $"{weekday}, {date.Day:00} de {month}";
    }

    private static string FormatObjectiveDate(string iso)
    {
        if (DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return "Prazo · " + date.ToString("dd 'de' MMMM 'de' yyyy", PtBr);
        return "Sem prazo definido";
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpper(value[0], PtBr) + value[1..];

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record OverviewCardView(string Caption, string Value, string Detail);

public sealed class MonthTabView
{
    public string IsoMonth { get; }
    public string Label { get; }
    public Brush Background { get; }
    public Brush Border { get; }
    public Brush Foreground { get; }

    public MonthTabView(DateOnly month, bool selected)
    {
        IsoMonth = StudyRepository.Iso(new DateOnly(month.Year, month.Month, 1));
        Label = MonthLabel(month);
        Background = ThemeManager.ResourceBrush(selected ? "PrimaryBrush" : "SurfaceRaisedBrush");
        Border = ThemeManager.ResourceBrush(selected ? "PrimaryBrush" : "BorderBrush");
        Foreground = selected ? Brushes.White : ThemeManager.ResourceBrush("TextBrush");
    }

    private static string MonthLabel(DateOnly month)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var text = month.ToDateTime(TimeOnly.MinValue).ToString("MMMM", culture);
        return string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], culture) + text[1..];
    }
}

public sealed class MonthDayCardView
{
    public string IsoDate { get; }
    public string Day { get; }
    public string Summary { get; }
    public string StatusSymbol { get; }
    public Brush Background { get; }
    public Brush Border { get; }
    public Brush Foreground { get; }
    public Brush MutedForeground { get; }
    public Brush AccentBrush { get; }
    public double Opacity { get; }

    public MonthDayCardView(
        DateOnly date,
        IReadOnlyList<SessionItem> sessions,
        bool currentMonth,
        bool selected,
        bool today)
    {
        IsoDate = StudyRepository.Iso(date);
        Day = date.Day.ToString(CultureInfo.InvariantCulture);

        var complete = sessions.Count > 0 && sessions.All(session => session.IsCompleted);
        var hasReview = sessions.Any(session => session.Kind == "review");
        var hasAssessment = sessions.Any(session => session.Kind == "assessment");

        StatusSymbol = complete ? "✓" : hasAssessment ? "★" : hasReview ? "↻" : sessions.Count > 0 ? "•" : "";
        AccentBrush = ThemeManager.ResourceBrush(
            complete ? "SuccessBrush" :
            hasAssessment ? "AssessmentBrush" :
            hasReview ? "ReviewBrush" :
            "PrimaryTextBrush");

        Summary = BuildSummary(sessions, complete);
        Background = ThemeManager.ResourceBrush(
            selected ? "PrimarySoftBrush" :
            currentMonth ? "SurfaceRaisedBrush" :
            "CalendarOutsideBrush");
        Border = ThemeManager.ResourceBrush(selected || today ? "PrimaryBrush" : "BorderBrush");
        Foreground = ThemeManager.ResourceBrush("TextBrush");
        MutedForeground = ThemeManager.ResourceBrush("MutedBrush");
        Opacity = currentMonth ? 1.0 : 0.52;
    }

    private static string BuildSummary(IReadOnlyList<SessionItem> sessions, bool complete)
    {
        if (sessions.Count == 0) return "";
        if (complete) return "Concluído";

        var subjects = sessions
            .Select(session => session.Subject.Trim())
            .Where(subject => subject.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (subjects.Count == 0) return $"{sessions.Count} blocos";
        if (subjects.Count <= 2) return string.Join(" • ", subjects);

        return subjects[0] + " • " + subjects[1] + $" • +{subjects.Count - 2}";
    }
}

public sealed class SessionCardView
{
    public string Id { get; }
    public string PlanId { get; }
    public string Subject { get; }
    public string Topic { get; }
    public string TargetText { get; }
    public string MinutesLabel { get; }
    public string KindDisplay { get; }
    public string IconGlyph { get; }
    public Brush AccentBrush { get; }
    public Brush BadgeBackground { get; }
    public Brush CardBackground { get; }
    public Visibility CompleteButtonVisibility { get; }
    public Visibility CompletedVisibility { get; }
    public Visibility MoveHintVisibility { get; }
    public bool CanMove { get; }

    public SessionCardView(SessionItem session)
    {
        Id = session.Id;
        PlanId = session.PlanId;
        Subject = session.Subject;
        Topic = session.Topic;
        TargetText = string.IsNullOrWhiteSpace(session.Target) ? "Sem meta adicional definida." : session.Target;
        MinutesLabel = session.Minutes + " min";
        KindDisplay = session.Kind switch
        {
            "review" => session.ReviewLabel.Length > 0 ? session.ReviewLabel : "REVISÃO",
            "assessment" => "SIMULADO / PROVA",
            _ => "ESTUDO"
        };

        AccentBrush = ThemeManager.ResourceBrush(
            session.IsCompleted ? "SuccessBrush" :
            session.Kind == "review" ? "ReviewBrush" :
            session.Kind == "assessment" ? "AssessmentBrush" :
            "PrimaryTextBrush");
        BadgeBackground = ThemeManager.ResourceBrush(
            session.IsCompleted ? "SuccessSoftBrush" :
            session.Kind == "review" ? "ReviewSoftBrush" :
            session.Kind == "assessment" ? "AssessmentSoftBrush" :
            "PrimarySoftBrush");
        CardBackground = ThemeManager.ResourceBrush(session.IsCompleted ? "SubtleBrush" : "SurfaceRaisedBrush");
        IconGlyph = IconFor(session);
        CompleteButtonVisibility = session.IsCompleted ? Visibility.Collapsed : Visibility.Visible;
        CompletedVisibility = session.IsCompleted ? Visibility.Visible : Visibility.Collapsed;
        CanMove = !session.IsCompleted && session.Origin != "runtime";
        MoveHintVisibility = CanMove ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string IconFor(SessionItem session)
    {
        if (session.Kind == "review") return "↻";
        if (session.Kind == "assessment") return "★";

        var subject = session.Subject.ToLowerInvariant();
        if (subject.Contains("matem") || subject.Contains("álgebra") || subject.Contains("algebra") ||
            subject.Contains("geometr") || subject.Contains("trigonom") || subject.Contains("cálculo") ||
            subject.Contains("calculo")) return "∑";
        if (subject.Contains("fís") || subject.Contains("fis")) return "⚡";
        if (subject.Contains("port") || subject.Contains("liter")) return "¶";
        if (subject.Contains("geo")) return "◎";
        if (subject.Contains("ingl") || subject.Contains("english")) return "A";
        if (subject.Contains("quím") || subject.Contains("quim")) return "◇";
        if (subject.Contains("bio")) return "✤";
        if (subject.Contains("hist")) return "◷";
        if (subject.Contains("erro")) return "!";
        return "◆";
    }
}
