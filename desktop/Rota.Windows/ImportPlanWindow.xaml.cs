using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Rota.Desktop;

public partial class ImportPlanWindow : Window
{
    private readonly StudyRepository _repository;
    private PlanPackage? _validatedPlan;
    private string _validatedSource = "";

    public ImportPlanWindow(StudyRepository repository, string? initialText = null)
    {
        _repository = repository;
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        if (!string.IsNullOrWhiteSpace(initialText))
        {
            CodeBox.Text = initialText;
            Loaded += (_, _) => ValidateCurrent();
        }
    }

    private void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir StudyPlan JSON",
            Filter = "StudyPlan JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1_048_576)
            {
                ShowError("O arquivo excede o limite seguro de importação.");
                return;
            }
            CodeBox.Text = File.ReadAllText(dialog.FileName);
            ValidateCurrent();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Não foi possível ler o arquivo. " + ex.Message);
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e) => ValidateCurrent();

    private void CodeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_validatedPlan is null || string.Equals(_validatedSource, CodeBox.Text, StringComparison.Ordinal)) return;
        InvalidatePreview("O JSON foi alterado. Valide novamente antes de aplicar.");
    }

    private void ValidateCurrent()
    {
        try
        {
            var source = CodeBox.Text;
            var plan = StudyPlanImporter.Parse(source);
            var preview = _repository.PreviewPlan(plan);
            _validatedPlan = preview.Success ? plan : null;
            _validatedSource = preview.Success ? source : "";
            ApplyButton.IsEnabled = preview.Success;

            PlanText.Text = plan.Title;
            RevisionText.Text = plan.Revision.ToString(CultureInfo.InvariantCulture);
            SessionsText.Text = plan.Sessions.Count.ToString(CultureInfo.InvariantCulture);
            var dates = plan.Sessions.Select(s => s.Date).OrderBy(x => x, StringComparer.Ordinal).ToList();
            PeriodText.Text = dates.Count == 0 ? "—" : $"{ShortDate(dates.First())} → {ShortDate(dates.Last())}";
            MinutesText.Text = FormatMinutes(plan.Sessions.Sum(s => s.Minutes));
            AppliedText.Text = preview.Success ? preview.Applied.ToString(CultureInfo.InvariantCulture) : "0";
            RemovedText.Text = preview.Success ? preview.Removed.ToString(CultureInfo.InvariantCulture) : "0";

            SetStatus(
                preview.Success ? "✓ " + preview.Message : "Não pode ser aplicado: " + preview.Message,
                preview.Success);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            _validatedPlan = null;
            _validatedSource = "";
            ApplyButton.IsEnabled = false;
            ClearPreview();
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message) => SetStatus(message, success: false);

    private void SetStatus(string message, bool success)
    {
        StatusCard.Background = ThemeManager.ResourceBrush(success ? "SuccessSoftBrush" : "ErrorSoftBrush");
        StatusText.Foreground = ThemeManager.ResourceBrush(success ? "SuccessBrush" : "ErrorBrush");
        StatusText.Text = message;
    }

    private void ClearPreview()
    {
        PlanText.Text = RevisionText.Text = SessionsText.Text = PeriodText.Text = MinutesText.Text = AppliedText.Text = RemovedText.Text = "—";
    }

    private void InvalidatePreview(string message)
    {
        _validatedPlan = null;
        _validatedSource = "";
        ApplyButton.IsEnabled = false;
        AppliedText.Text = RemovedText.Text = "—";
        ShowError(message);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_validatedPlan is null || !string.Equals(_validatedSource, CodeBox.Text, StringComparison.Ordinal))
        {
            InvalidatePreview("O JSON precisa ser validado novamente antes de aplicar.");
            return;
        }

        try
        {
            var result = _repository.ApplyPlan(_validatedPlan);
            if (!result.Success)
            {
                InvalidatePreview(result.Message);
                return;
            }
            MessageBox.Show($"Plano aplicado.\n\n{result.Applied} sessões adicionadas ao futuro do calendário.", "Rota", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Não foi possível gravar o plano. O calendário anterior foi mantido. " + ex.Message);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string ShortDate(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM/yyyy");
    private static string FormatMinutes(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}min" : minutes + " min";
}

