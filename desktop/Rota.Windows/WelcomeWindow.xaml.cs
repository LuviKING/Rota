using System.Windows;

namespace Rota.Desktop;

public partial class WelcomeWindow : Window
{
    private readonly StudyRepository _repository;

    public WelcomeWindow(StudyRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
    }

    public bool WelcomeCompleted { get; private set; }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _repository.CompleteOnboardingStep(OnboardingSteps.Welcome);
            WelcomeCompleted = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Não foi possível salvar o início da configuração. O Rota não alterou seu calendário.\n\n" + ex.Message,
                "Boas-vindas · Rota",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NotNow_Click(object sender, RoutedEventArgs e) => Close();
}
