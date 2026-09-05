using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class AiAssistantWindow : Window
{
    private readonly IAiAssistantController _controller;
    private readonly StudyRepository _repository;
    private AiProposalKind _proposalKind = AiProposalKind.StudyPlan;
    private bool _initializationStarted;

    public ObservableCollection<AiProposalCardView> HistoryItems { get; } = new();

    public AiAssistantWindow(IAiAssistantController controller, StudyRepository repository)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        DataContext = this;
        _controller.StateChanged += Controller_StateChanged;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        RefreshState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted) return;
        _initializationStarted = true;
        await _controller.InitializeAsync();
        if (IsLoaded) RefreshState();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _controller.StateChanged -= Controller_StateChanged;
        Loaded -= Window_Loaded;
        Closed -= Window_Closed;
    }

    private void Controller_StateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RefreshState();
        else _ = Dispatcher.BeginInvoke(RefreshState);
    }

    private void RefreshState()
    {
        var state = _controller.State;
        StatusText.Text = state.StatusMessage;
        ProfileText.Text = "Perfil · " + ProfileName(state.EffectiveProfile);

        InstallationNotice.Visibility = state.InstallationState == AiInstallationState.Ready
            ? Visibility.Collapsed
            : Visibility.Visible;
        InstallationTitle.Text = state.InstallationState == AiInstallationState.RuntimeInstalled
            ? "Runtime pronto; modelo local ausente"
            : "IA local ainda não instalada";

        WarningNotice.Visibility = state.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = state.Warnings.Count == 0 ? "" : state.Warnings[^1];

        HistoryItems.Clear();
        foreach (var item in state.History.OrderByDescending(item => item.UpdatedAtUtc))
            HistoryItems.Add(new AiProposalCardView(item));
        EmptyHistoryPanel.Visibility = HistoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryCountText.Text = HistoryItems.Count == 1 ? "1 proposta" : $"{HistoryItems.Count} propostas";

        var generating = state.Activity == AiAssistantActivity.Generating;
        CancelButton.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        SetInputEnabled(!state.IsBusy);
        UpdateSendState(state);
    }

    private void SetInputEnabled(bool enabled)
    {
        RequestBox.IsEnabled = enabled;
        NewPlanChoice.IsEnabled = enabled;
        ChangePlanChoice.IsEnabled = enabled;
        QuickBuildPlanButton.IsEnabled = enabled;
        foreach (var button in FindVisualChildren<Button>(this).Where(button => button.Tag is string tag &&
                     Enum.TryParse<AiAssistantQuickAction>(tag, out _)))
            button.IsEnabled = enabled;
    }

    private void UpdateSendState(AiAssistantState? state = null)
    {
        state ??= _controller.State;
        var hasRequest = !string.IsNullOrWhiteSpace(RequestBox.Text);
        SendButton.IsEnabled = state.IsInitialized && state.IsOfflineReady && !state.IsBusy && hasRequest;
        SendHintText.Text = !state.IsInitialized || state.Activity == AiAssistantActivity.Loading
            ? "Carregando o estado local…"
            : !state.IsOfflineReady
                ? "O envio será liberado quando runtime e modelo locais estiverem instalados."
                : hasRequest
                    ? "A resposta ficará somente como prévia até uma confirmação futura."
                    : "Descreva seu pedido ou escolha uma ação rápida.";
    }

    private void RequestBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SendButton is not null) UpdateSendState();
    }

    private void ProposalKind_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } && Enum.TryParse(value, out AiProposalKind kind))
            _proposalKind = kind;
    }

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } ||
            !Enum.TryParse(value, out AiAssistantQuickAction action)) return;

        try
        {
            var input = _controller.ApplyQuickAction(new AiAssistantInput { FreeText = RequestBox.Text }, action);
            RequestBox.Text = input.FreeText;
            _proposalKind = _controller.SuggestedKind(action);
            NewPlanChoice.IsChecked = _proposalKind == AiProposalKind.StudyPlan;
            ChangePlanChoice.IsChecked = _proposalKind == AiProposalKind.PlanChanges;
            RequestBox.Focus();
            RequestBox.CaretIndex = RequestBox.Text.Length;
        }
        catch (AiContractValidationException ex)
        {
            SendHintText.Text = ex.Message;
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var request = RequestBox.Text.Trim();
        if (request.Length == 0)
        {
            SendHintText.Text = "Descreva seu pedido antes de enviar.";
            return;
        }

        var result = await _controller.SendAsync(new AiAssistantInput { FreeText = request }, _proposalKind);
        if (result is not null && IsLoaded)
        {
            RequestBox.Clear();
            RefreshState();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _controller.CancelCurrentOperation();

    private void OpenExternalPrompt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AiPromptWindow(_repository) { Owner = this };
        dialog.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string ProfileName(AiProfile profile) => profile switch
    {
        AiProfile.Lightweight => "Leve",
        AiProfile.Balanced => "Equilibrado",
        AiProfile.Performance => "Desempenho",
        _ => "Automático"
    };

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T result) yield return result;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}

public sealed class AiProposalCardView
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public AiProposalCardView(AiStoredProposal stored)
    {
        Summary = string.IsNullOrWhiteSpace(stored.Proposal.Summary) ? "Proposta sem resumo" : stored.Proposal.Summary;
        Message = stored.Preview.Message;
        KindDisplay = stored.Proposal.Kind == AiProposalKind.StudyPlan ? "PLANO NOVO" : "AJUSTE DO PLANO";
        (StatusDisplay, StatusBackground, StatusForeground) = Status(stored);
        MetricsDisplay = BuildMetrics(stored.Preview);
        CreatedAtDisplay = stored.UpdatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", PtBr);
    }

    public string Summary { get; }
    public string Message { get; }
    public string KindDisplay { get; }
    public string StatusDisplay { get; }
    public string MetricsDisplay { get; }
    public string CreatedAtDisplay { get; }
    public Brush StatusBackground { get; }
    public Brush StatusForeground { get; }

    private static (string Label, Brush Background, Brush Foreground) Status(AiStoredProposal stored)
    {
        if (stored.Preview.State == AiProposalPreviewState.Blocked || stored.Proposal.Status == AiProposalStatus.Failed)
            return ("BLOQUEADA", ThemeManager.ResourceBrush("ErrorSoftBrush"), ThemeManager.ResourceBrush("ErrorBrush"));
        return stored.Proposal.Status switch
        {
            AiProposalStatus.Accepted => ("ACEITA", ThemeManager.ResourceBrush("PrimarySoftBrush"), ThemeManager.ResourceBrush("PrimaryBrush")),
            AiProposalStatus.Applied => ("APLICADA", ThemeManager.ResourceBrush("SuccessSoftBrush"), ThemeManager.ResourceBrush("SuccessBrush")),
            AiProposalStatus.Rejected => ("REJEITADA", ThemeManager.ResourceBrush("SubtleBrush"), ThemeManager.ResourceBrush("MutedBrush")),
            _ => ("VALIDADA", ThemeManager.ResourceBrush("SuccessSoftBrush"), ThemeManager.ResourceBrush("SuccessBrush"))
        };
    }

    private static string BuildMetrics(AiProposalPreview preview)
    {
        var changes = new List<string>();
        if (preview.AddedSessionCount > 0) changes.Add($"+{preview.AddedSessionCount} sessões");
        if (preview.RemovedSessionCount > 0) changes.Add($"−{preview.RemovedSessionCount} sessões");
        if (preview.MovedSessionCount > 0) changes.Add($"{preview.MovedSessionCount} movidas");
        var summary = $"{preview.BeforeSessionCount} → {preview.AfterSessionCount} sessões · {preview.BeforeMinutes} → {preview.AfterMinutes} min";
        return changes.Count == 0 ? summary : summary + " · " + string.Join(" · ", changes);
    }
}
