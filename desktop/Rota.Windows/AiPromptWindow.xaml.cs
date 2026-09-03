using System.Globalization;
using System.Windows;

namespace Rota.Desktop;

public partial class AiPromptWindow : Window
{
    private readonly StudyRepository _repository;

    public AiPromptWindow(StudyRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        RefreshContext();
        BuildPrompt();
    }

    private void RefreshContext()
    {
        var settings = _repository.Settings;
        var reviews = new List<string>();
        if (settings.ReviewD1) reviews.Add("D+1");
        if (settings.ReviewD3) reviews.Add("D+3");
        if (settings.ReviewD7) reviews.Add("D+7");
        ContextText.Text =
            $"Objetivo: {settings.ObjectiveName}\n" +
            $"Prazo: {(settings.ObjectiveDate.Length == 0 ? "não definido" : settings.ObjectiveDate)}\n" +
            $"Limite: {settings.DailyHours} h/dia · bloco preferido: {settings.BlockMinutes} min\n" +
            $"Revisões automáticas: {(reviews.Count == 0 ? "desligadas" : string.Join(", ", reviews))}\n" +
            $"Plano ativo: {(settings.ActivePlanId.Length == 0 ? "nenhum" : settings.ActivePlanId + " · rev. " + settings.ActivePlanRevision.ToString(CultureInfo.InvariantCulture))}";
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => BuildPrompt();

    private void BuildPrompt()
    {
        PromptBox.Text = AiPromptBuilder.Build(RequestBox.Text, DateOnly.FromDateTime(DateTime.Today), _repository.Settings);
        LengthText.Text = PromptBox.Text.Length.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " caracteres";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        BuildPrompt();
        try
        {
            Clipboard.SetText(PromptBox.Text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            MessageBox.Show("A área de transferência está ocupada por outro aplicativo. Tente copiar novamente.\n\n" + ex.Message, "Rota", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var originalBackground = LengthBadge.Background;
        var originalForeground = LengthText.Foreground;
        LengthBadge.Background = ThemeManager.ResourceBrush("SuccessSoftBrush");
        LengthText.Foreground = ThemeManager.ResourceBrush("SuccessBrush");
        LengthText.Text = "Prompt copiado ✓";

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LengthBadge.Background = originalBackground;
            LengthText.Foreground = originalForeground;
            LengthText.Text = PromptBox.Text.Length.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " caracteres";
        };
        timer.Start();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

