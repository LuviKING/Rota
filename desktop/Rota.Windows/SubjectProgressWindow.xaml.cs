using System.Globalization;
using System.Windows;

namespace Rota.Desktop;

public partial class SubjectProgressWindow : Window
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public string TotalSubjectsLabel { get; }
    public string CompletedMinutesLabel { get; }
    public string PlannedMinutesLabel { get; }
    public string CompletedSessionsLabel { get; }
    public string PlannedSessionsLabel { get; }
    public int CompletionPercent { get; }
    public string CompletionLabel { get; }
    public string OverallMessage { get; }
    public string DisplayCountLabel { get; }
    public Visibility EmptyVisibility => Subjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<SubjectProgressItemView> Subjects { get; }

    public SubjectProgressWindow(RepositoryApplicationSnapshot snapshot)
    {
        var progress = SubjectProgressAnalyzer.Analyze(snapshot);
        TotalSubjectsLabel = progress.TotalSubjects.ToString(Portuguese);
        CompletedMinutesLabel = $"{progress.CompletedMinutes} min";
        PlannedMinutesLabel = $"de {progress.PlannedMinutes} min planejados";
        CompletedSessionsLabel = progress.CompletedSessions.ToString(Portuguese);
        PlannedSessionsLabel = $"de {progress.PlannedSessions} sessões";
        CompletionPercent = progress.CompletionPercent;
        CompletionLabel = $"{CompletionPercent}%";
        OverallMessage = progress.PlannedSessions == 0
            ? "Ainda não há matérias no calendário"
            : $"{progress.CompletedSessions} de {progress.PlannedSessions} sessões concluídas";
        var displayedLabel = progress.Subjects.Count == 1 ? "1 exibida" : $"{progress.Subjects.Count} exibidas";
        var hiddenLabel = progress.HiddenSubjectCount == 1 ? "1 oculta" : $"{progress.HiddenSubjectCount} ocultas";
        DisplayCountLabel = progress.HiddenSubjectCount == 0
            ? displayedLabel
            : $"{displayedLabel} · {hiddenLabel}";
        Subjects = progress.Subjects.Select(subject => new SubjectProgressItemView(
            subject.Subject,
            subject.CompletionPercent,
            $"{subject.CompletionPercent}%",
            $"{subject.CompletedMinutes}/{subject.PlannedMinutes} min",
            subject.RemainingMinutes == 0 ? "nada pendente" : $"faltam {subject.RemainingMinutes} min",
            $"{subject.CompletedSessions}/{subject.PlannedSessions} sessões",
            subject.CompletedStudyDays == 1 ? "1 dia com conclusão" : $"{subject.CompletedStudyDays} dias com conclusão")).ToList();

        InitializeComponent();
        DataContext = this;
        WindowSizing.FitToWorkArea(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record SubjectProgressItemView(
    string Subject,
    int CompletionPercent,
    string CompletionLabel,
    string MinutesLabel,
    string RemainingMinutesLabel,
    string SessionsLabel,
    string StudyDaysLabel);
