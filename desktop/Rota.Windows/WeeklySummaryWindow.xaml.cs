using System.Globalization;
using System.Windows;

namespace Rota.Desktop;

public partial class WeeklySummaryWindow : Window
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private readonly RepositoryApplicationSnapshot _snapshot;

    public string WeekRangeLabel { get; }
    public string CompletedMinutesLabel { get; }
    public string PlannedMinutesLabel { get; }
    public string CompletedSessionsLabel { get; }
    public string PlannedSessionsLabel { get; }
    public string CompletionLabel { get; }
    public string RemainingMinutesLabel { get; }
    public string RemainingSessionsLabel { get; }
    public string ProgressMessage { get; }
    public int CompletionPercent { get; }
    public IReadOnlyList<WeeklyProgressDayView> Days { get; }

    public WeeklySummaryWindow(RepositoryApplicationSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        var summary = WeeklyProgressAnalyzer.Analyze(snapshot);
        WeekRangeLabel = $"{summary.WeekStart:dd/MM/yyyy} a {summary.WeekEnd:dd/MM/yyyy}";
        CompletedMinutesLabel = $"{summary.CompletedMinutes} min";
        PlannedMinutesLabel = $"de {summary.PlannedMinutes} min planejados";
        CompletedSessionsLabel = summary.CompletedSessions.ToString(Portuguese);
        PlannedSessionsLabel = $"de {summary.PlannedSessions} sessões";
        CompletionPercent = summary.CompletionPercent;
        CompletionLabel = $"{CompletionPercent}%";
        RemainingMinutesLabel = $"{summary.RemainingMinutes} min";
        RemainingSessionsLabel = $"{summary.RemainingSessions} {(summary.RemainingSessions == 1 ? "sessão" : "sessões")}";
        ProgressMessage = summary.PlannedSessions == 0
            ? "Semana sem sessões planejadas"
            : summary.RemainingSessions == 0 ? "Semana concluída" : $"{summary.RemainingSessions} por concluir";
        Days = summary.Days.Select(day => new WeeklyProgressDayView(
            Portuguese.DateTimeFormat.GetDayName(day.Date.DayOfWeek),
            day.Date.ToString("dd/MM", Portuguese),
            day.CompletedMinutes,
            Math.Max(1, day.PlannedMinutes),
            $"{day.CompletedMinutes}/{day.PlannedMinutes} min",
            $"{day.CompletedSessions}/{day.PlannedSessions} sessões")).ToList();

        InitializeComponent();
        DataContext = this;
        WindowSizing.FitToWorkArea(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SubjectProgress_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SubjectProgressWindow(_snapshot) { Owner = this };
        dialog.ShowDialog();
    }

    private void ProgressHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProgressHistoryWindow(_snapshot) { Owner = this };
        dialog.ShowDialog();
    }
}

public sealed record WeeklyProgressDayView(
    string DayName,
    string DateLabel,
    int CompletedMinutes,
    int ProgressMaximum,
    string MinutesLabel,
    string SessionsLabel);
