using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace Rota.Desktop;

public partial class ReminderWindow : Window
{
    public string TodaySummary { get; }
    public string OverdueSummary { get; }
    public string TotalMinutesLabel { get; }
    public IReadOnlyList<ReminderSessionItem> TodayItems { get; }
    public string EmptyMessage { get; }
    public Visibility EmptyVisibility => TodayItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public ReminderWindow(StudyRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var snapshot = repository.CaptureApplicationSnapshot();
        var today = snapshot.SnapshotDate;
        var todaySessions = snapshot.Sessions
            .Where(session => !session.IsCompleted && string.Equals(session.Date, today, StringComparison.Ordinal))
            .OrderBy(session => session.Subject, StringComparer.CurrentCultureIgnoreCase)
            .Take(5)
            .ToList();
        var allToday = snapshot.Sessions
            .Where(session => !session.IsCompleted && string.Equals(session.Date, today, StringComparison.Ordinal))
            .ToList();
        var overdue = OverdueStudyAnalyzer.Analyze(snapshot);

        TodayItems = todaySessions.Select(session => new ReminderSessionItem(
            session.Subject,
            session.Topic,
            $"{session.Minutes} min")).ToList();
        var todayCount = allToday.Count;
        TodaySummary = todayCount == 0
            ? "Nenhum bloco pendente para hoje"
            : $"{todayCount} {(todayCount == 1 ? "bloco pendente" : "blocos pendentes")}";
        var totalMinutes = allToday.Sum(session => session.Minutes);
        TotalMinutesLabel = $"{totalMinutes} min";
        OverdueSummary = overdue.TotalCount == 0
            ? "Você não tem estudos atrasados."
            : $"Além disso, {overdue.TotalCount} {(overdue.TotalCount == 1 ? "bloco está atrasado" : "blocos estão atrasados")} ({overdue.TotalMinutes} min).";
        EmptyMessage = "Sua agenda de hoje está livre. Abra o calendário para ver os próximos dias.";

        InitializeComponent();
        DataContext = this;
        WindowSizing.FitToWorkArea(this);
    }

    private void OpenRota_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new FileNotFoundException("O executável do Rota não foi encontrado.");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        {
            MessageBox.Show("Não foi possível abrir o calendário.\n\n" + ex.Message, "Rota", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record ReminderSessionItem(string Subject, string Topic, string MinutesLabel);
