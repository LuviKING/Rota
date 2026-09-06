using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class OnboardingFirstPlanWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly StudyRepository _repository;
    private readonly IAiAssistantController _controller;
    private readonly IAiInstallationController _installationController;
    private readonly IAiProposalApplicationService _applicationService;
    private AiStoredProposal? _proposal;
    private bool _initializationStarted;
    private bool _calendarActionRunning;

    public OnboardingFirstPlanWindow(
        StudyRepository repository,
        IAiAssistantController controller,
        IAiInstallationController installationController,
        IAiProposalApplicationService applicationService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _installationController = installationController ?? throw new ArgumentNullException(nameof(installationController));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        _controller.StateChanged += Controller_StateChanged;
        LoadRoutineSummary();
        RefreshState();
    }

    public bool FirstPlanCompleted { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted || HasActiveFuturePlan()) return;
        _initializationStarted = true;
        try
        {
            await _applicationService.ReconcileAsync();
            await _controller.InitializeAsync();
            ResumePreparedProposal();
        }
        catch (Exception ex) when (ex is AiProposalApplicationException or AiContractValidationException or
                                   IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowError("Não foi possível carregar o estado da IA local. " + ex.Message);
        }
        if (IsLoaded) RefreshState();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _controller.CancelCurrentOperation();
        _controller.StateChanged -= Controller_StateChanged;
    }

    private void Controller_StateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RefreshState();
        else _ = Dispatcher.BeginInvoke(RefreshState);
    }

    private void LoadRoutineSummary()
    {
        var settings = _repository.Settings;
        ObjectiveSummaryText.Text = settings.ObjectiveName;
        var date = DateOnly.TryParseExact(
            settings.ObjectiveDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var deadline)
            ? deadline.ToString("dd/MM/yyyy", PtBr)
            : "sem prazo";
        RoutineSummaryText.Text =
            $"Prazo {date} · {settings.AvailableStudyDays.Count} " +
            $"{(settings.AvailableStudyDays.Count == 1 ? "dia" : "dias")} por semana · {settings.DailyHours:0.#} h por dia";
    }

    private void RefreshState()
    {
        var existingPlan = HasActiveFuturePlan();
        ExistingPlanPanel.Visibility = existingPlan ? Visibility.Visible : Visibility.Collapsed;
        NewPlanPanel.Visibility = existingPlan ? Visibility.Collapsed : Visibility.Visible;
        if (existingPlan)
        {
            var settings = _repository.Settings;
            ExistingPlanText.Text = string.IsNullOrWhiteSpace(settings.ActivePlanTitle)
                ? "Seu calendário atual será preservado integralmente."
                : $"“{settings.ActivePlanTitle}” será mantido e nenhuma sessão será substituída.";
            return;
        }

        var state = _controller.State;
        var busy = state.IsBusy || _calendarActionRunning;
        var ready = state.IsInitialized && state.IsOfflineReady;
        AiStatusText.Text = _calendarActionRunning
            ? "Validando o calendário antes da aplicação…"
            : state.StatusMessage;
        AiUnavailablePanel.Visibility = state.IsInitialized && !state.IsOfflineReady
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConfigureAiButton.IsEnabled = !busy;
        DifficultiesBox.IsEnabled = !busy && DifficultiesUnknownCheck.IsChecked != true;
        DifficultiesUnknownCheck.IsEnabled = !busy;
        NotesBox.IsEnabled = !busy;
        var hasDifficulty = DifficultiesUnknownCheck.IsChecked == true ||
            !string.IsNullOrWhiteSpace(DifficultiesBox.Text);
        GenerateButton.IsEnabled = ready && !busy && hasDifficulty;
        CancelGenerationButton.Visibility = state.Activity == AiAssistantActivity.Generating
            ? Visibility.Visible
            : Visibility.Collapsed;

        PreviewPanel.Visibility = _proposal is null ? Visibility.Collapsed : Visibility.Visible;
        if (_proposal is not null)
        {
            var canProceed = _proposal.Preview.CanProceed;
            ProposalSummaryText.Text = _proposal.Proposal.Summary;
            ProposalMetricsText.Text =
                $"{_proposal.Preview.BeforeSessionCount} → {_proposal.Preview.AfterSessionCount} sessões · " +
                $"{_proposal.Preview.BeforeMinutes} → {_proposal.Preview.AfterMinutes} min";
            ProposalMessageText.Text = _proposal.Preview.Message;
            ProposalStatusText.Text = canProceed ? "PRONTA" : "BLOQUEADA";
            ProposalStatusBadge.SetResourceReference(
                Border.BackgroundProperty,
                canProceed ? "SuccessSoftBrush" : "ErrorSoftBrush");
            ProposalStatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                canProceed ? "SuccessBrush" : "ErrorBrush");
            ReviewAndApplyButton.Visibility = canProceed ? Visibility.Visible : Visibility.Collapsed;
            ReviewAndApplyButton.IsEnabled = !busy;
        }

        if (state.Activity == AiAssistantActivity.Error)
        {
            var detail = state.Warnings.LastOrDefault();
            ShowError(detail is null ? state.StatusMessage : state.StatusMessage + " " + detail);
        }
        else if (!busy)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void FormInput_Changed(object sender, RoutedEventArgs e)
    {
        if (DifficultiesBox is not null) RefreshState();
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            var input = OnboardingFirstPlanInputBuilder.Build(
                _repository.Settings,
                DifficultiesBox.Text,
                DifficultiesUnknownCheck.IsChecked == true,
                NotesBox.Text);
            _proposal = await _controller.SendAsync(input, AiProposalKind.StudyPlan);
            if (_proposal is null && _controller.State.Activity != AiAssistantActivity.Error)
                ShowError("A geração não foi concluída. Você pode tentar novamente sem perder sua rotina.");
        }
        catch (AiContractValidationException ex)
        {
            ShowError(ex.Message);
        }
        if (IsLoaded) RefreshState();
    }

    private void CancelGeneration_Click(object sender, RoutedEventArgs e) => _controller.CancelCurrentOperation();

    private async void ConfigureAi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AiInstallationWindow(_installationController) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _controller.InitializeAsync();
        if (IsLoaded) RefreshState();
    }

    private async void ReviewAndApply_Click(object sender, RoutedEventArgs e)
    {
        if (_proposal is null || _calendarActionRunning) return;
        _calendarActionRunning = true;
        RefreshState();
        try
        {
            var prepared = await _applicationService.PrepareAsync(_proposal.Proposal.Id);
            var dialog = new AiProposalConfirmationWindow(prepared) { Owner = this };
            if (dialog.ShowDialog() != true || !prepared.CanApply) return;

            var result = await _applicationService.ApplyAsync(prepared.ConfirmationId);
            if (!result.Success)
            {
                ShowError(result.Message);
                return;
            }
            _repository.CompleteOnboardingStep(OnboardingSteps.FirstPlan);
            FirstPlanCompleted = true;
            Close();
        }
        catch (Exception ex) when (ex is AiProposalApplicationException or AiContractValidationException or
                                   IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _calendarActionRunning = false;
            if (IsLoaded) RefreshState();
        }
    }

    private void UseExistingPlan_Click(object sender, RoutedEventArgs e)
    {
        if (!HasActiveFuturePlan()) return;
        try
        {
            _repository.CompleteOnboardingStep(OnboardingSteps.FirstPlan);
            FirstPlanCompleted = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError(ex.Message);
        }
    }

    private void ResumePreparedProposal()
    {
        _proposal = _controller.State.History
            .Where(item => item.Proposal.Kind == AiProposalKind.StudyPlan && item.Preview.CanProceed)
            .Where(item => item.Proposal.Status is AiProposalStatus.Validated or AiProposalStatus.Accepted)
            .Where(item => !_applicationService.GetCalendarState(item.Proposal.Id).Exists)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();
    }

    private bool HasActiveFuturePlan()
    {
        var snapshot = _repository.CaptureApplicationSnapshot();
        return snapshot.Settings.ActivePlanId.Length > 0 && snapshot.Sessions.Any(session =>
            !session.IsCompleted &&
            string.Equals(session.PlanId, snapshot.Settings.ActivePlanId, StringComparison.Ordinal) &&
            string.CompareOrdinal(session.Date, snapshot.SnapshotDate) >= 0);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorPanel.BringIntoView();
    }

    private void NotNow_Click(object sender, RoutedEventArgs e) => Close();
}
