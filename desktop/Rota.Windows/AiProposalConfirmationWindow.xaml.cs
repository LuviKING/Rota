using System.Windows;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class AiProposalConfirmationWindow : Window
{
    public AiProposalConfirmationWindow(AiPreparedApplication prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        DataContext = new AiProposalConfirmationView(prepared);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class AiProposalConfirmationView
{
    public AiProposalConfirmationView(AiPreparedApplication prepared)
    {
        Summary = prepared.Summary;
        KindDisplay = prepared.Kind == AiProposalKind.StudyPlan ? "PLANO NOVO" : "AJUSTE DO PLANO";
        PreviewMessage = prepared.Preview.Message;
        ReviewMessage = prepared.Message;
        SessionMetrics = $"{prepared.Preview.BeforeSessionCount} → {prepared.Preview.AfterSessionCount}";
        MinuteMetrics = $"{prepared.Preview.BeforeMinutes} → {prepared.Preview.AfterMinutes} min";
        Operations = prepared.Preview.Operations.Select(operation => new AiConfirmationOperationView(operation)).ToList();
        Warnings = prepared.Preview.Warnings.ToList();
        CanApply = prepared.CanApply;
        ApplyVisibility = prepared.CanApply ? Visibility.Visible : Visibility.Collapsed;
        RecalculatedVisibility = prepared.CanApply && prepared.ChangedSinceSavedPreview
            ? Visibility.Visible
            : Visibility.Collapsed;
        BlockedVisibility = prepared.CanApply ? Visibility.Collapsed : Visibility.Visible;
        OperationsVisibility = Operations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningsVisibility = Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Summary { get; }
    public string KindDisplay { get; }
    public string PreviewMessage { get; }
    public string ReviewMessage { get; }
    public string SessionMetrics { get; }
    public string MinuteMetrics { get; }
    public IReadOnlyList<AiConfirmationOperationView> Operations { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool CanApply { get; }
    public Visibility ApplyVisibility { get; }
    public Visibility RecalculatedVisibility { get; }
    public Visibility BlockedVisibility { get; }
    public Visibility OperationsVisibility { get; }
    public Visibility WarningsVisibility { get; }
}

public sealed class AiConfirmationOperationView
{
    public AiConfirmationOperationView(AiPreviewOperation operation)
    {
        Summary = operation.Summary;
        Glyph = operation.Type switch
        {
            AiPlanOperationType.MoveSession => "\uE787",
            AiPlanOperationType.AddSession => "\uE710",
            AiPlanOperationType.RemoveFutureSession => "\uE74D",
            AiPlanOperationType.ChangeSubjectPriority => "\uE8E3",
            AiPlanOperationType.SetAvailability => "\uE823",
            AiPlanOperationType.RedistributeLoad => "\uE9D9",
            _ => "\uE946"
        };
        Detail = operation.Type switch
        {
            AiPlanOperationType.MoveSession when operation.OriginalDate.Length > 0 =>
                $"{operation.SessionId} · {operation.OriginalDate} → {operation.ProposedDate}",
            AiPlanOperationType.AddSession => $"Nova sessão em {operation.ProposedDate}",
            AiPlanOperationType.RemoveFutureSession => $"Remover {operation.SessionId} de {operation.OriginalDate}",
            _ => operation.Type switch
            {
                AiPlanOperationType.ChangeSubjectPriority => "Prioridade registrada nas preferências locais",
                AiPlanOperationType.SetAvailability => "Disponibilidade registrada e validada",
                _ => "Operação validada pelo Rota"
            }
        };
    }

    public string Summary { get; }
    public string Detail { get; }
    public string Glyph { get; }
}
