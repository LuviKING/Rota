using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Rota.Desktop;

public partial class ProgressHistoryWindow : Window
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public string TotalCompletedMinutesLabel { get; }
    public string TotalCompletedSessionsLabel { get; }
    public string ActiveMonthsLabel { get; }
    public IReadOnlyList<MonthlyProgressItemView> Months { get; }
    public IReadOnlyList<WeeklyComparisonItemView> Weeks { get; }

    public ProgressHistoryWindow(RepositoryApplicationSnapshot snapshot)
    {
        var history = ProgressHistoryAnalyzer.Analyze(snapshot);
        TotalCompletedMinutesLabel = $"{history.TotalCompletedMinutes} min";
        TotalCompletedSessionsLabel = history.TotalCompletedSessions.ToString(Portuguese);
        ActiveMonthsLabel = history.ActiveMonths.ToString(Portuguese);
        Months = history.Months.Select(month => new MonthlyProgressItemView(
            Capitalize(month.Month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Portuguese)),
            month.CompletionPercent,
            $"{month.CompletionPercent}%",
            $"{month.CompletedMinutes}/{month.PlannedMinutes} min")).ToList();
        Weeks = history.Weeks.Select(week =>
        {
            var neutral = week.CompletedMinutesChange == 0;
            var positive = week.CompletedMinutesChange > 0;
            return new WeeklyComparisonItemView(
                $"{week.WeekStart:dd/MM} a {week.WeekEnd:dd/MM}",
                $"{week.CompletedSessions}/{week.PlannedSessions} sessões · {week.CompletionPercent}%",
                $"{week.CompletedMinutes} min",
                neutral ? "igual" : $"{(positive ? "+" : "−")}{Math.Abs(week.CompletedMinutesChange)} min",
                ThemeManager.ResourceBrush(positive ? "SuccessSoftBrush" : neutral ? "SubtleBrush" : "ErrorSoftBrush"),
                ThemeManager.ResourceBrush(positive ? "SuccessBrush" : neutral ? "MutedBrush" : "ErrorBrush"));
        }).ToList();

        InitializeComponent();
        DataContext = this;
        WindowSizing.FitToWorkArea(this);
    }

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpper(value[0], Portuguese) + value[1..];

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record MonthlyProgressItemView(
    string MonthLabel,
    int CompletionPercent,
    string CompletionLabel,
    string MinutesLabel);

public sealed record WeeklyComparisonItemView(
    string RangeLabel,
    string SessionsLabel,
    string MinutesLabel,
    string ChangeLabel,
    Brush ChangeBackground,
    Brush ChangeForeground);
