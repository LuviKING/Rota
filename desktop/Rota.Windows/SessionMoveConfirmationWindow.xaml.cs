using System.Globalization;
using System.Windows;

namespace Rota.Desktop;

public partial class SessionMoveConfirmationWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public SessionMoveConfirmationWindow(SessionCardView card, SessionMoveResult preview)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.Success || preview.AlreadyHandled || preview.SourceDate.Length == 0 || preview.TargetDate.Length == 0)
            throw new ArgumentException("A confirmação exige uma prévia válida de movimento.", nameof(preview));

        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        SubjectText.Text = $"{card.Subject} · {card.MinutesLabel}";
        TopicText.Text = card.Topic;
        SourceDateText.Text = FormatDate(preview.SourceDate);
        TargetDateText.Text = FormatDate(preview.TargetDate);
    }

    private static string FormatDate(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("ddd, dd/MM/yyyy", PtBr);

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
