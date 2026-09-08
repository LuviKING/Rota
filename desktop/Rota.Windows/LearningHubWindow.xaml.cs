using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Rota.Desktop;

public partial class LearningHubWindow : Window
{
    private readonly string _root;
    private readonly Version _version;
    private readonly LearningProgressStore _progressStore;
    private LearningContentLibrarySnapshot _snapshot = new(Array.Empty<LearningLibraryPackage>(), Array.Empty<LearningLibraryIssue>());
    public ObservableCollection<LearningHubPackageView> Packages { get; } = new();

    public LearningHubWindow(string rootDirectory, Version applicationVersion)
    {
        _root = rootDirectory;
        _version = applicationVersion;
        _progressStore = new LearningProgressStore(Path.Combine(_root, "progress.json"));
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        PackageList.ItemsSource = Packages;
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Refresh()
    {
        _snapshot = new LearningContentLibrary(_root, _version).Load();
        Packages.Clear();
        foreach (var item in _snapshot.Packages) Packages.Add(new LearningHubPackageView(item));
        EmptyText.Visibility = Packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = Packages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Packages.Count > 0) PackageList.SelectedIndex = 0;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var packageDialog = new OpenFileDialog { Title = "Abrir pacote pedagógico", Filter = "Pacote Rota (*.json)|*.json", CheckFileExists = true };
        if (packageDialog.ShowDialog(this) != true) return;
        var manifestDialog = new OpenFileDialog { Title = "Abrir manifesto do pacote", Filter = "Manifesto Rota (*.json)|*.json", CheckFileExists = true };
        if (manifestDialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(packageDialog.FileName).Length > LearningContentPackageFormat.MaximumPackageBytes || new FileInfo(manifestDialog.FileName).Length > LearningPackageManifestFormat.MaximumManifestBytes)
                throw new InvalidDataException("O arquivo escolhido excede o limite seguro.");
            _ = new LearningPackageInstaller(_root, _version).Install(File.ReadAllBytes(packageDialog.FileName), File.ReadAllBytes(manifestDialog.FileName));
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { MessageBox.Show(exception.Message, "Importar material", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void PackageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackageList.SelectedItem is not LearningHubPackageView view) return;
        PackageTitleText.Text = view.Package.Package.Title;
        PackageMetaText.Text = $"{view.Manifest.Author.Name} · {view.Manifest.EducationLevel}";
        ContentList.ItemsSource = view.Package.Catalog.Contents.OrderBy(item => item.Position).Select(item => item.Copy()).ToList();
        ContentList.SelectedIndex = 0;
    }

    private void ContentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackageList.SelectedItem is not LearningHubPackageView view || ContentList.SelectedItem is not LearningContent content) return;
        var guide = LearningStudyGuideService.Create(view.Package, content.Id);
        LessonTitleText.Text = guide.TheoryMaterial?.Title ?? guide.Content.Title;
        LessonGoalText.Text = guide.TheoryMaterial?.LearningGoal ?? guide.Content.Summary;
        LessonBodyText.Text = guide.TheoryMaterial is null ? "Este conteúdo ainda não possui uma explicação no pacote." : string.Join("\n\n", guide.TheoryMaterial.Sections.OrderBy(section => section.Position).Select(section => section.Title + "\n" + section.Body));
        PracticeText.Text = guide.PracticeQuestions.Count == 0 ? "" : $"Prática disponível: {guide.PracticeQuestions.Count} questão(ões).";
        AssessmentText.Text = view.Package.Assessments.Count == 0 ? "" : $"Simulados disponíveis: {view.Package.Assessments.Count}.";
        RefreshProgress(content.Id);
    }

    private void CompleteContent_Click(object sender, RoutedEventArgs e)
    {
        if (ContentList.SelectedItem is not LearningContent content) return;
        try
        {
            _progressStore.MarkContentCompleted(content.Id);
            RefreshProgress(content.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Não foi possível salvar seu progresso.\n\n" + exception.Message, "Aprender", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshProgress(string contentId)
    {
        var completed = _progressStore.Snapshot().CompletedContentIds.Contains(contentId);
        ProgressText.Text = completed ? "✓ Estudado" : "";
        CompleteContentButton.IsEnabled = !completed;
    }
}

public sealed class LearningHubPackageView
{
    public LearningContentPackage Package { get; }
    public LearningPackageManifest Manifest { get; }
    public string Title => Package.Package.Title;
    public string Detail => $"{Manifest.Author.Name} · {Manifest.EducationLevel}";
    public LearningHubPackageView(LearningLibraryPackage item) { Package = item.Package; Manifest = item.Manifest; }
}
