using System.Windows;
using System.Windows.Threading;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private bool _isSmokeTest;
    private bool _isReminder;
    private LocalAiServices? _localAiServices;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _isSmokeTest = e.Args.Any(argument => string.Equals(argument, "--smoke-test", StringComparison.Ordinal));
        _isReminder = e.Args.Any(argument => string.Equals(argument, "--reminder", StringComparison.Ordinal));

        var mutexName = _isSmokeTest
            ? @"Local\Rota.Desktop.SmokeTest"
            : _isReminder ? @"Local\Rota.Desktop.Reminder" : @"Local\Rota.Desktop.SingleInstance";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            if (!_isSmokeTest && !_isReminder)
            {
                MessageBox.Show(
                    "O Rota já está aberto. Use a janela existente para evitar alterações concorrentes no calendário.",
                    "Rota",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            Shutdown(_isSmokeTest ? 1 : 0);
            return;
        }

        try
        {
            var smokeDataPath = _isSmokeTest ? Environment.GetEnvironmentVariable("ROTA_SMOKE_DATA_PATH") : null;
            var repository = new StudyRepository(string.IsNullOrWhiteSpace(smokeDataPath) ? null : smokeDataPath);
            if (_isReminder)
            {
                var reminderWindow = new ReminderWindow(repository);
                MainWindow = reminderWindow;
                reminderWindow.Show();
                if (_isSmokeTest)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        reminderWindow.UpdateLayout();
                        Shutdown(0);
                    });
                }
                return;
            }
            _localAiServices = LocalAiServices.Create(repository);
            var window = new MainWindow(
                repository,
                _localAiServices.AssistantController,
                _localAiServices.InstallationController,
                _localAiServices.ApplicationService,
                _localAiServices.HardwareDiagnostics,
                _localAiServices.PerformanceDiagnostics);
            MainWindow = window;
            window.Show();
            if (_isSmokeTest)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    window.UpdateLayout();
                    Shutdown(0);
                });
            }
            else if (repository.CompletedOnboardingStep < OnboardingSteps.FirstPlan)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    ShowPendingOnboarding(window, repository, _localAiServices);
                });
            }
        }
        catch (Exception) when (_isSmokeTest)
        {
            Shutdown(1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "O Rota não conseguiu acessar o arquivo local de dados. Verifique as permissões e se outro processo está usando a pasta.\n\n" + ex.Message,
                "Rota",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void ShowPendingOnboarding(
        MainWindow window,
        StudyRepository repository,
        LocalAiServices localAiServices)
    {
        if (!window.IsVisible) return;

        if (repository.CompletedOnboardingStep < OnboardingSteps.Welcome)
        {
            var welcome = new WelcomeWindow(repository) { Owner = window };
            welcome.ShowDialog();
            if (!window.IsVisible || repository.CompletedOnboardingStep < OnboardingSteps.Welcome) return;
        }

        if (repository.CompletedOnboardingStep < OnboardingSteps.Routine)
        {
            var routine = new OnboardingRoutineWindow(repository) { Owner = window };
            routine.ShowDialog();
            if (!window.IsVisible || repository.CompletedOnboardingStep < OnboardingSteps.Routine) return;
        }

        if (repository.CompletedOnboardingStep < OnboardingSteps.FirstPlan)
        {
            var firstPlan = new OnboardingFirstPlanWindow(
                repository,
                localAiServices.AssistantController,
                localAiServices.InstallationController,
                localAiServices.ApplicationService) { Owner = window };
            firstPlan.ShowDialog();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_localAiServices is not null)
        {
            try { _localAiServices.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            _localAiServices = null;
        }
        if (_ownsSingleInstanceMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_isSmokeTest)
        {
            e.Handled = true;
            Shutdown(1);
            return;
        }
        MessageBox.Show(
            "O Rota encontrou um erro inesperado. Nenhuma alteração incompleta foi aplicada aos dados locais.\n\n" + e.Exception.Message,
            "Rota",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

