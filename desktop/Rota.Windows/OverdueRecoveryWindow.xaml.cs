using System.Globalization;
using System.Windows;

namespace Rota.Desktop;

public partial class OverdueRecoveryWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly OverdueStudySnapshot _snapshot;

    public OverdueRecoveryWindow(OverdueStudySnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (!snapshot.HasOverdue)
            throw new ArgumentException("A recuperação precisa de ao menos um bloco atrasado.", nameof(snapshot));

        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        SummaryTitleText.Text =
            $"{snapshot.TotalCount} {(snapshot.TotalCount == 1 ? "bloco atrasado" : "blocos atrasados")} · " +
            $"{snapshot.TotalMinutes} min";
        SummaryDetailText.Text = snapshot.RuntimeProtectedCount == 0
            ? "O calendário continua intacto enquanto você revisa as opções."
            : $"{snapshot.RuntimeProtectedCount} " +
              $"{(snapshot.RuntimeProtectedCount == 1 ? "revisão automática está protegida" : "revisões automáticas estão protegidas")} contra alterações diretas.";
        OldestBadgeText.Text = snapshot.MostDelayedDays == 1
            ? "MAIS ANTIGO · 1 DIA"
            : $"MAIS ANTIGO · {snapshot.MostDelayedDays} DIAS";

        var items = snapshot.Items.Select(item => new OverdueRecoveryItemView(
            item.PlannedDate.ToString("dd/MM/yyyy", PtBr),
            item.PlannedDate == snapshot.SnapshotDate.AddDays(-1)
                ? "1 dia de atraso"
                : $"{snapshot.SnapshotDate.DayNumber - item.PlannedDate.DayNumber} dias de atraso",
            item.Subject,
            item.Topic,
            item.Minutes + " min",
            item.IsRuntimeProtected ? Visibility.Visible : Visibility.Collapsed)).ToList();
        OverdueItemsControl.ItemsSource = items;
        VisibleCountText.Text = items.Count == snapshot.TotalCount
            ? $"{items.Count} exibidos"
            : $"{items.Count} de {snapshot.TotalCount} exibidos";
    }

    public bool OpenAssistantRequested { get; private set; }
    public DateOnly? SelectedDate { get; private set; }

    private void NotNow_Click(object sender, RoutedEventArgs e) => Close();

    private void ViewOldest_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = _snapshot.OldestDate;
        DialogResult = true;
    }

    private void PrepareWithAi_Click(object sender, RoutedEventArgs e)
    {
        OpenAssistantRequested = true;
        DialogResult = true;
    }
}

public sealed record OverdueRecoveryItemView(
    string DateText,
    string DelayText,
    string Subject,
    string Topic,
    string MinutesText,
    Visibility ProtectedVisibility);
