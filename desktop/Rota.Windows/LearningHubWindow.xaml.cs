using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

public partial class LearningHubWindow : Window
{
    private readonly string _root;
    private readonly Version _version;
    private readonly LearningProgressStore _progressStore;
    private readonly LearningQuestionAttemptStore _questionAttemptStore;
    private readonly IAiTeacherService? _teacherService;
    private LearningContentLibrarySnapshot _snapshot = new(Array.Empty<LearningLibraryPackage>(), Array.Empty<LearningLibraryIssue>());
    private IReadOnlyList<LearningPracticeQuestion> _practiceQuestions = Array.Empty<LearningPracticeQuestion>();
    private int _practiceIndex;
    private string? _selectedPracticeOptionId;

    public ObservableCollection<LearningHubPackageView> Packages { get; } = new();

    public LearningHubWindow(
        string rootDirectory,
        Version applicationVersion,
        IAiTeacherService? teacherService = null)
    {
        _root = rootDirectory;
        _version = applicationVersion;
        _progressStore = new LearningProgressStore(Path.Combine(_root, "progress.json"));
        _questionAttemptStore = new LearningQuestionAttemptStore(Path.Combine(_root, "question-attempts.json"));
        _teacherService = teacherService;
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        PackageList.ItemsSource = Packages;
        AskTeacherButton.IsEnabled = _teacherService is not null;
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh(string? preferredPackageId = null)
    {
        preferredPackageId ??= (PackageList.SelectedItem as LearningHubPackageView)?.Package.Package.Id;
        _snapshot = new LearningContentLibrary(_root, _version).Load();
        Packages.Clear();
        foreach (var item in _snapshot.Packages) Packages.Add(new LearningHubPackageView(item));

        EmptyText.Visibility = Packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = Packages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Packages.Count == 0)
        {
            PracticePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var preferredIndex = preferredPackageId is null
            ? -1
            : Packages.Select((item, index) => (item, index))
                .Where(pair => string.Equals(pair.item.Package.Package.Id, preferredPackageId, StringComparison.Ordinal))
                .Select(pair => pair.index)
                .DefaultIfEmpty(-1)
                .First();
        PackageList.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var packageDialog = new OpenFileDialog
        {
            Title = "Abrir pacote pedagógico",
            Filter = "Pacote Rota (*.json)|*.json",
            CheckFileExists = true
        };
        if (packageDialog.ShowDialog(this) != true) return;

        var manifestDialog = new OpenFileDialog
        {
            Title = "Abrir manifesto do pacote",
            Filter = "Manifesto Rota (*.json)|*.json",
            CheckFileExists = true
        };
        if (manifestDialog.ShowDialog(this) != true) return;

        try
        {
            var packageBytes = ReadImportFile(packageDialog.FileName, LearningContentPackageFormat.MaximumPackageBytes);
            var manifestBytes = ReadImportFile(manifestDialog.FileName, LearningPackageManifestFormat.MaximumManifestBytes);
            var installer = new LearningPackageInstaller(_root, _version);
            var change = installer.Inspect(packageBytes, manifestBytes);

            if (change.Kind == LearningPackageChangeKind.Update)
            {
                var confirmation = MessageBox.Show(
                    $"Atualizar \"{change.Title}\"?\n\n" +
                    $"Versão {change.PreviousVersion} → {change.NewVersion}\n" +
                    $"{change.ContentCount} conteúdo(s) verificado(s)\n\n" +
                    "Seu progresso de aprendizagem continuará separado e será preservado. " +
                    "A versão anterior do material ficará guardada como recuperação local.",
                    "Atualizar material",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes) return;
            }

            var result = installer.InstallOrUpdate(packageBytes, manifestBytes);
            Refresh(result.Installed.Id);

            var message = result.Change.Kind == LearningPackageChangeKind.Update
                ? $"Material atualizado com segurança para a versão {result.Installed.Version}.\n\nO progresso do aluno foi preservado e a versão anterior continua disponível para recuperação."
                : $"Material instalado com sucesso.\n\nVersão {result.Installed.Version} · {result.Change.ContentCount} conteúdo(s).";
            MessageBox.Show(message, "Aprender", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(exception.Message, "Importar ou atualizar material", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static byte[] ReadImportFile(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length is < 1 or > int.MaxValue || info.Length > maximumBytes)
            throw new InvalidDataException("O arquivo escolhido possui tamanho inválido ou excede o limite seguro.");
        return File.ReadAllBytes(path);
    }

    private void PackageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackageList.SelectedItem is not LearningHubPackageView view) return;
        PackageTitleText.Text = view.Package.Package.Title;
        PackageMetaText.Text = $"{view.Manifest.Author.Name} · {view.Manifest.EducationLevel} · v{view.Package.Package.Version}";
        ContentList.ItemsSource = view.Package.Catalog.Contents.OrderBy(item => item.Position).Select(item => item.Copy()).ToList();
        ContentList.SelectedIndex = 0;
    }

    private void ContentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackageList.SelectedItem is not LearningHubPackageView view || ContentList.SelectedItem is not LearningContent content) return;
        var guide = LearningStudyGuideService.Create(view.Package, content.Id);
        LessonTitleText.Text = guide.TheoryMaterial?.Title ?? guide.Content.Title;
        LessonGoalText.Text = guide.TheoryMaterial?.LearningGoal ?? guide.Content.Summary;
        LessonBodyText.Text = guide.TheoryMaterial is null
            ? "Este conteúdo ainda não possui uma explicação no pacote."
            : string.Join("\n\n", guide.TheoryMaterial.Sections.OrderBy(section => section.Position).Select(section => section.Title + "\n" + section.Body));

        _practiceQuestions = guide.PracticeQuestions.Select(question => question.Copy()).ToList();
        _practiceIndex = 0;
        PracticeText.Text = _practiceQuestions.Count == 0 ? "" : $"Prática disponível: {_practiceQuestions.Count} questão(ões).";
        ShowPracticeQuestion();

        AssessmentText.Text = view.Package.Assessments.Count == 0 ? "" : $"Simulados disponíveis: {view.Package.Assessments.Count}.";
        AskTeacherButton.IsEnabled = _teacherService is not null;
        RefreshProgress(content.Id);
    }

    private LearningPracticeQuestion? CurrentPracticeQuestion =>
        _practiceIndex >= 0 && _practiceIndex < _practiceQuestions.Count ? _practiceQuestions[_practiceIndex] : null;

    private void ShowPracticeQuestion()
    {
        var question = CurrentPracticeQuestion;
        if (question is null)
        {
            PracticePanel.Visibility = Visibility.Collapsed;
            return;
        }

        PracticePanel.Visibility = Visibility.Visible;
        PracticeCounterText.Text = $"QUESTÃO {_practiceIndex + 1} DE {_practiceQuestions.Count}";
        PracticePromptText.Text = question.Prompt;
        NextPracticeButton.IsEnabled = _practiceIndex + 1 < _practiceQuestions.Count;
        PreparePracticeAnswer(question);
        RefreshPracticeStats(question.Id);
    }

    private void PreparePracticeAnswer(LearningPracticeQuestion question)
    {
        _selectedPracticeOptionId = null;
        PracticeOptionsList.ItemsSource = null;
        PracticeOptionsList.ItemsSource = question.Options.Select(option => option.Copy()).ToList();
        PracticeOptionsList.IsEnabled = true;
        AnswerPracticeButton.IsEnabled = false;
        RetryPracticeButton.Visibility = Visibility.Collapsed;
        PracticeResultText.Text = "";
        PracticeExplanationText.Text = "";
        PracticeExplanationPanel.Visibility = Visibility.Collapsed;
    }

    private void PracticeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string optionId } || CurrentPracticeQuestion is null) return;
        _selectedPracticeOptionId = optionId;
        AnswerPracticeButton.IsEnabled = true;
    }

    private void AnswerPractice_Click(object sender, RoutedEventArgs e)
    {
        var question = CurrentPracticeQuestion;
        var selectedOptionId = _selectedPracticeOptionId;
        if (question is null || selectedOptionId is null) return;

        try
        {
            var result = LearningPracticeQuestionGrader.Grade(question, selectedOptionId);
            _questionAttemptStore.Record(
                question.Id,
                question.ContentId,
                result.SelectedOptionId,
                result.IsCorrect,
                DateTimeOffset.UtcNow);

            PracticeOptionsList.IsEnabled = false;
            AnswerPracticeButton.IsEnabled = false;
            RetryPracticeButton.Visibility = Visibility.Visible;
            PracticeResultText.Text = result.IsCorrect
                ? "✓ Resposta correta."
                : $"Resposta incorreta. Gabarito: {result.CorrectOptionText}";
            PracticeResultText.Foreground = (System.Windows.Media.Brush)FindResource(result.IsCorrect ? "SuccessBrush" : "AssessmentBrush");
            PracticeExplanationText.Text = result.Explanation;
            PracticeExplanationPanel.Visibility = Visibility.Visible;
            RefreshPracticeStats(question.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            MessageBox.Show("Não foi possível registrar esta resposta.\n\n" + exception.Message, "Aprender", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RetryPractice_Click(object sender, RoutedEventArgs e)
    {
        var question = CurrentPracticeQuestion;
        if (question is null) return;
        PreparePracticeAnswer(question);
        RefreshPracticeStats(question.Id);
    }

    private void UndoPracticeAttempt_Click(object sender, RoutedEventArgs e)
    {
        var question = CurrentPracticeQuestion;
        if (question is null) return;
        try
        {
            if (!_questionAttemptStore.UndoLatest(question.Id)) return;
            PreparePracticeAnswer(question);
            PracticeResultText.Text = "Última tentativa removida do progresso.";
            PracticeResultText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            RefreshPracticeStats(question.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show("Não foi possível desfazer a tentativa.\n\n" + exception.Message, "Aprender", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NextPractice_Click(object sender, RoutedEventArgs e)
    {
        if (_practiceIndex + 1 >= _practiceQuestions.Count) return;
        _practiceIndex++;
        ShowPracticeQuestion();
    }

    private void RefreshPracticeStats(string questionId)
    {
        var attempts = _questionAttemptStore.Snapshot().Attempts
            .Where(item => string.Equals(item.QuestionId, questionId, StringComparison.Ordinal))
            .ToList();
        var correct = attempts.Count(item => item.IsCorrect);
        PracticeStatsText.Text = attempts.Count == 0 ? "Sem tentativas" : $"{attempts.Count} tentativa(s) · {correct} acerto(s)";
        UndoPracticeAttemptButton.IsEnabled = attempts.Count > 0;
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

    private void AskTeacher_Click(object sender, RoutedEventArgs e)
    {
        if (_teacherService is null ||
            PackageList.SelectedItem is not LearningHubPackageView view ||
            ContentList.SelectedItem is not LearningContent content)
        {
            return;
        }

        try
        {
            var context = AiTeacherLessonContextFactory.Create(view.Package, content.Id);
            var dialog = new AiTeacherLessonWindow(_teacherService, context) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception exception) when (exception is AiContractValidationException or InvalidDataException)
        {
            MessageBox.Show(exception.Message, "Professora Local", MessageBoxButton.OK, MessageBoxImage.Warning);
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
    public string Detail => $"{Manifest.Author.Name} · {Manifest.EducationLevel} · v{Package.Package.Version}";
    public LearningHubPackageView(LearningLibraryPackage item) { Package = item.Package; Manifest = item.Manifest; }
}
