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
    private readonly IAiInstallationController _installationController;
    private readonly IAiProposalApplicationService _applicationService;
    private readonly StudyRepository _repository;
    private AiProposalKind _proposalKind = AiProposalKind.StudyPlan;
    private bool _initializationStarted;
    private bool _calendarActionRunning;
    private string _applicationWarning = "";

    public ObservableCollection<AiProposalCardView> HistoryItems { get; } = new();
    public ObservableCollection<AiConversationTurnView> ConversationItems { get; } = new();

    public AiAssistantWindow(
        IAiAssistantController controller,
        IAiInstallationController installationController,
        IAiProposalApplicationService applicationService,
        StudyRepository repository)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _installationController = installationController ?? throw new ArgumentNullException(nameof(installationController));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
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
        try
        {
            await _applicationService.ReconcileAsync();
            _applicationWarning = "";
        }
        catch (Exception ex) when (ex is AiProposalApplicationException or IOException or UnauthorizedAccessException)
        {
            _applicationWarning = "O histórico de aplicações precisa de atenção: " + ex.Message;
        }
        await _controller.InitializeAsync();
        if (IsLoaded) RefreshState();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _controller.CancelCurrentOperation();
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

        var warning = string.IsNullOrWhiteSpace(_applicationWarning)
            ? state.Warnings.LastOrDefault() ?? ""
            : _applicationWarning;
        WarningNotice.Visibility = warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = warning;

        ConversationItems.Clear();
        foreach (var turn in state.Conversation)
            ConversationItems.Add(new AiConversationTurnView(turn));
        EmptyConversationPanel.Visibility = ConversationItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConversationCountText.Text = ConversationItems.Count == 1
            ? "1 mensagem"
            : $"{ConversationItems.Count} mensagens";
        if (ConversationItems.Count > 0)
            _ = Dispatcher.BeginInvoke(ConversationScroll.ScrollToEnd);

        HistoryItems.Clear();
        foreach (var item in state.History.OrderByDescending(item => item.UpdatedAtUtc))
            HistoryItems.Add(new AiProposalCardView(
                item,
                _applicationService.GetCalendarState(item.Proposal.Id),
                !_calendarActionRunning));
        EmptyHistoryPanel.Visibility = HistoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryCountText.Text = HistoryItems.Count == 1 ? "1 proposta" : $"{HistoryItems.Count} propostas";

        var generating = state.Activity == AiAssistantActivity.Generating;
        CancelButton.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        SetInputEnabled(!state.IsBusy && !_calendarActionRunning, HasAdjustablePlan());
        UpdateSendState(state);
    }

    private void SetInputEnabled(bool enabled, bool hasAdjustablePlan)
    {
        if (!hasAdjustablePlan && _proposalKind == AiProposalKind.PlanChanges)
        {
            _proposalKind = AiProposalKind.StudyPlan;
            NewPlanChoice.IsChecked = true;
        }
        RequestBox.IsEnabled = enabled;
        NewPlanChoice.IsEnabled = enabled;
        ChangePlanChoice.IsEnabled = enabled && hasAdjustablePlan;
        ChangePlanChoice.ToolTip = hasAdjustablePlan
            ? "Propor mudanças somente nas sessões futuras do plano atual."
            : "Crie e aplique um plano antes de tentar ajustá-lo.";
        QuickBuildPlanButton.IsEnabled = enabled;
        OpenAiDiagnosticsButton.IsEnabled = enabled;
        var canAdjust = enabled && hasAdjustablePlan;
        QuickReorganizeWeekButton.IsEnabled = canAdjust;
        QuickAdjustLoadButton.IsEnabled = canAdjust;
        QuickReviewDelaysButton.IsEnabled = canAdjust;
    }

    private bool HasAdjustablePlan()
    {
        var snapshot = _repository.CaptureApplicationSnapshot();
        return snapshot.Settings.ActivePlanId.Length > 0 && snapshot.Sessions.Any(session =>
            !session.IsCompleted &&
            string.Equals(session.PlanId, snapshot.Settings.ActivePlanId, StringComparison.Ordinal) &&
            string.CompareOrdinal(session.Date, snapshot.SnapshotDate) >= 0);
    }

    private void UpdateSendState(AiAssistantState? state = null)
    {
        state ??= _controller.State;
        var hasRequest = !string.IsNullOrWhiteSpace(RequestBox.Text);
        SendButton.IsEnabled = state.IsInitialized && state.IsOfflineReady && !state.IsBusy && !_calendarActionRunning && hasRequest;
        var hint = !state.IsInitialized || state.Activity == AiAssistantActivity.Loading
            ? "Carregando o estado local…"
            : !state.IsOfflineReady
                ? "O envio será liberado quando runtime e modelo locais estiverem instalados."
                : hasRequest
                    ? "A resposta ficará como prévia; depois você poderá revisar e confirmar a aplicação."
                    : "Descreva seu pedido ou escolha uma ação rápida.";
        var length = RequestBox.Text.Length.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        SendHintText.Text = $"{hint} · {length}/8.000 caracteres";
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

    private async void ReviewAndApply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryProposalId(sender, out var proposalId) || _calendarActionRunning) return;
        await RunCalendarActionAsync(async () =>
        {
            var prepared = await _applicationService.PrepareAsync(proposalId);
            var dialog = new AiProposalConfirmationWindow(prepared) { Owner = this };
            if (dialog.ShowDialog() != true || !prepared.CanApply) return;

            var result = await _applicationService.ApplyAsync(prepared.ConfirmationId);
            MessageBox.Show(
                result.Message,
                result.Success ? "Proposta aplicada" : "Aplicação não realizada",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        });
    }

    private async void RejectProposal_Click(object sender, RoutedEventArgs e)
    {
        if (!TryProposalId(sender, out var proposalId) || _calendarActionRunning) return;
        var answer = MessageBox.Show(
            "Rejeitar esta proposta? Ela continuará registrada no histórico e não alterará o calendário.",
            "Rejeitar proposta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunCalendarActionAsync(async () =>
        {
            await _applicationService.RejectAsync(proposalId);
        });
    }

    private async void UndoProposal_Click(object sender, RoutedEventArgs e)
    {
        if (!TryProposalId(sender, out var proposalId) || _calendarActionRunning) return;
        var answer = MessageBox.Show(
            "Desfazer esta aplicação?\n\nO Rota só continuará se o calendário ainda estiver exatamente como ficou após a aplicação. Conclusões ou ajustes posteriores nunca serão apagados.",
            "Desfazer aplicação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunCalendarActionAsync(async () =>
        {
            var result = await _applicationService.UndoAsync(proposalId);
            MessageBox.Show(
                result.Message,
                result.Success ? "Aplicação desfeita" : "Nada foi desfeito",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        });
    }

    private async Task RunCalendarActionAsync(Func<Task> action)
    {
        _calendarActionRunning = true;
        RefreshState();
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is AiProposalApplicationException or AiContractValidationException or
                                   IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(ex.Message, "Assistente IA", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                await _controller.InitializeAsync();
            }
            finally
            {
                _calendarActionRunning = false;
                if (IsLoaded) RefreshState();
            }
        }
    }

    private static bool TryProposalId(object sender, out Guid proposalId)
    {
        proposalId = Guid.Empty;
        return sender is Button { Tag: Guid value } && (proposalId = value) != Guid.Empty;
    }

    private void OpenExternalPrompt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AiPromptWindow(_repository) { Owner = this };
        dialog.ShowDialog();
    }

    private void OpenAiDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AiDiagnosticsWindow(_controller) { Owner = this };
        dialog.ShowDialog();
        if (IsLoaded) RefreshState();
    }

    private async void ConfigureLocalAi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AiInstallationWindow(_installationController) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _controller.InitializeAsync();
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

public sealed class AiConversationTurnView
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public AiConversationTurnView(AiConversationTurn turn)
    {
        var isUser = turn.Role == AiConversationRole.User;
        RoleDisplay = isUser ? "VOCÊ" : "ROTA IA";
        Text = turn.Text;
        BubbleAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Background = ThemeManager.ResourceBrush(isUser ? "PrimarySoftBrush" : "BackgroundBrush");
        CreatedAtDisplay = turn.CreatedAtUtc.ToLocalTime().ToString("dd/MM 'às' HH:mm", PtBr);
        ProposalVisibility = !isUser && turn.ProposalId != Guid.Empty
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProposalDisplay = turn.ProposalId == Guid.Empty
            ? ""
            : turn.ProposalKind == AiProposalKind.StudyPlan
                ? "Proposta de plano vinculada"
                : "Proposta de ajuste vinculada";
        StatusDisplay = turn.Status switch
        {
            AiConversationTurnStatus.Pending => "GERANDO…",
            AiConversationTurnStatus.Cancelled => "CANCELADA",
            AiConversationTurnStatus.Failed => "NÃO CONCLUÍDA",
            _ => ""
        };
        StatusVisibility = StatusDisplay.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public string RoleDisplay { get; }
    public string Text { get; }
    public HorizontalAlignment BubbleAlignment { get; }
    public Brush Background { get; }
    public string CreatedAtDisplay { get; }
    public string ProposalDisplay { get; }
    public string StatusDisplay { get; }
    public Visibility ProposalVisibility { get; }
    public Visibility StatusVisibility { get; }
}

public sealed class AiProposalCardView
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public AiProposalCardView(AiStoredProposal stored)
        : this(stored, new CalendarApplicationState(false, false, false, false, ""), actionsEnabled: true)
    {
    }

    public AiProposalCardView(
        AiStoredProposal stored,
        CalendarApplicationState calendarState,
        bool actionsEnabled)
    {
        ProposalId = stored.Proposal.Id;
        Summary = string.IsNullOrWhiteSpace(stored.Proposal.Summary) ? "Proposta sem resumo" : stored.Proposal.Summary;
        Message = stored.Preview.Message;
        KindDisplay = stored.Proposal.Kind == AiProposalKind.StudyPlan ? "PLANO NOVO" : "AJUSTE DO PLANO";
        (StatusDisplay, StatusBackground, StatusForeground) = Status(stored, calendarState);
        MetricsDisplay = BuildMetrics(stored.Preview);
        CreatedAtDisplay = stored.UpdatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", PtBr);
        var canReview = !calendarState.Exists && stored.Preview.CanProceed &&
            stored.Proposal.Status is AiProposalStatus.Validated or AiProposalStatus.Accepted;
        ApplyVisibility = canReview ? Visibility.Visible : Visibility.Collapsed;
        RejectVisibility = canReview ? Visibility.Visible : Visibility.Collapsed;
        UndoVisibility = calendarState.IsApplied || stored.Proposal.Status == AiProposalStatus.Applied
            ? Visibility.Visible
            : Visibility.Collapsed;
        CanApply = canReview && actionsEnabled;
        CanReject = canReview && actionsEnabled;
        CanUndo = calendarState.CanUndo && actionsEnabled;
        UndoHintVisibility = (calendarState.IsApplied || stored.Proposal.Status == AiProposalStatus.Applied) && !calendarState.CanUndo
            ? Visibility.Visible
            : Visibility.Collapsed;
        UndoHint = calendarState.Message;
    }

    public Guid ProposalId { get; }
    public string Summary { get; }
    public string Message { get; }
    public string KindDisplay { get; }
    public string StatusDisplay { get; }
    public string MetricsDisplay { get; }
    public string CreatedAtDisplay { get; }
    public Brush StatusBackground { get; }
    public Brush StatusForeground { get; }
    public Visibility ApplyVisibility { get; }
    public Visibility RejectVisibility { get; }
    public Visibility UndoVisibility { get; }
    public Visibility UndoHintVisibility { get; }
    public bool CanApply { get; }
    public bool CanReject { get; }
    public bool CanUndo { get; }
    public string UndoHint { get; }

    private static (string Label, Brush Background, Brush Foreground) Status(
        AiStoredProposal stored,
        CalendarApplicationState calendarState)
    {
        if (stored.Preview.State == AiProposalPreviewState.Blocked || stored.Proposal.Status == AiProposalStatus.Failed)
            return ("BLOQUEADA", ThemeManager.ResourceBrush("ErrorSoftBrush"), ThemeManager.ResourceBrush("ErrorBrush"));
        if (calendarState.IsUndone)
            return ("DESFEITA", ThemeManager.ResourceBrush("SubtleBrush"), ThemeManager.ResourceBrush("MutedBrush"));
        if (calendarState.IsApplied)
            return ("APLICADA", ThemeManager.ResourceBrush("SuccessSoftBrush"), ThemeManager.ResourceBrush("SuccessBrush"));
        return stored.Proposal.Status switch
        {
            AiProposalStatus.Accepted => ("ACEITA", ThemeManager.ResourceBrush("PrimarySoftBrush"), ThemeManager.ResourceBrush("PrimaryTextBrush")),
            AiProposalStatus.Applied => ("APLICADA", ThemeManager.ResourceBrush("SuccessSoftBrush"), ThemeManager.ResourceBrush("SuccessBrush")),
            AiProposalStatus.Undone => ("DESFEITA", ThemeManager.ResourceBrush("SubtleBrush"), ThemeManager.ResourceBrush("MutedBrush")),
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
