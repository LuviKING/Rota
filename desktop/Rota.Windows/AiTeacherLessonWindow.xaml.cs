using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

/// <summary>
/// Janela de estudo vinculada a um único conteúdo verificado. Ela não conversa
/// com o planejamento, não aplica alterações e exibe as fontes/limites decididos
/// deterministicamente pelo Rota junto de toda explicação.
/// </summary>
public partial class AiTeacherLessonWindow : Window
{
    private readonly AiTeacherLessonController _controller;
    private CancellationTokenSource? _generationCancellation;
    private bool _isGenerating;

    public ObservableCollection<AiTeacherExplanationStyleDescriptor> Styles { get; } = new();

    public AiTeacherLessonWindow(
        IAiTeacherService teacherService,
        AiTeacherLessonContext lessonContext)
    {
        _controller = new AiTeacherLessonController(teacherService, lessonContext);
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        foreach (var style in AiTeacherExplanationStyles.All) Styles.Add(style);
        StylePicker.ItemsSource = Styles;
        StylePicker.SelectedItem = Styles.Single(item => item.Style == AiTeacherExplanationStyle.StepByStep);
        LessonTitleText.Text = _controller.LessonContext.ContentTitle;
        ShowEvidence(
            AiTeacherGroundingMetadataFactory.Create(_controller.LessonContext),
            AiTeacherKnowledgeDisclosureFactory.Create(_controller.LessonContext));
        RefreshInputState();
        Closed += Window_Closed;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _generationCancellation?.Cancel();
        _generationCancellation?.Dispose();
        _generationCancellation = null;
        Closed -= Window_Closed;
    }

    private void QuestionBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshInputState();

    private async void Explain_Click(object sender, RoutedEventArgs e)
    {
        if (_isGenerating) return;
        var question = QuestionBox.Text.Trim();
        if (question.Length == 0)
        {
            OperationNoticeText.Text = "Escreva uma pergunta sobre este conteúdo antes de pedir a explicação.";
            return;
        }

        _isGenerating = true;
        _generationCancellation = new CancellationTokenSource();
        OperationNoticeText.Text = "A Professora Local está preparando uma explicação a partir do material interno…";
        RefreshInputState();
        try
        {
            var style = (StylePicker.SelectedItem as AiTeacherExplanationStyleDescriptor)?.Style
                ?? AiTeacherExplanationStyle.StepByStep;
            var answer = await _controller.ExplainAsync(question, style, _generationCancellation.Token);
            if (!IsLoaded) return;
            ShowAnswer(answer);
            OperationNoticeText.Text = answer.Knowledge.CanAnswerSubstantively
                ? "Explicação pronta. As fontes e o nível de cobertura aparecem abaixo."
                : "A Professora Local não iniciou o modelo: o pacote ainda não sustenta uma explicação segura.";
        }
        catch (OperationCanceledException) when (_generationCancellation?.IsCancellationRequested == true)
        {
            OperationNoticeText.Text = "Explicação cancelada. Nenhuma alteração foi feita no seu plano ou progresso.";
        }
        catch (Exception ex) when (ex is AiInferenceException or AiContractValidationException or IOException or UnauthorizedAccessException)
        {
            OperationNoticeText.Text = ex.Message;
        }
        finally
        {
            _generationCancellation?.Dispose();
            _generationCancellation = null;
            _isGenerating = false;
            if (IsLoaded) RefreshInputState();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _generationCancellation?.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshInputState()
    {
        var hasQuestion = !string.IsNullOrWhiteSpace(QuestionBox.Text);
        ExplainButton.IsEnabled = !_isGenerating && hasQuestion;
        CancelButton.Visibility = _isGenerating ? Visibility.Visible : Visibility.Collapsed;
        QuestionBox.IsEnabled = !_isGenerating;
        StylePicker.IsEnabled = !_isGenerating;
        QuestionHintText.Text = $"{QuestionBox.Text.Length:N0}/4.000 caracteres";
    }

    private void ShowAnswer(AiTeacherGroundedAnswer result)
    {
        ShowEvidence(result.Grounding, result.Knowledge);
        AnswerTitleText.Text = result.Answer.Title;
        AnswerIntroductionText.Text = result.Answer.Introduction;
        AnswerStepsList.ItemsSource = result.Answer.Steps;
        AnswerRecapText.Text = result.Answer.Recap;
        LimitationsList.ItemsSource = result.Answer.Limitations;
        LimitationsPanel.Visibility = result.Answer.Limitations.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AnswerPanel.Visibility = Visibility.Visible;
    }

    private void ShowEvidence(
        AiTeacherGroundingMetadata grounding,
        AiTeacherKnowledgeDisclosure knowledge)
    {
        KnowledgeStatusText.Text = AiTeacherKnowledgeDisclosureFactory.GetStatusLabel(knowledge.Status);
        KnowledgeReasonText.Text = knowledge.Reason;
        ConfidenceText.Text = AiTeacherGroundingMetadataFactory.GetConfidenceLabel(grounding.Confidence);
        SourcesList.ItemsSource = grounding.Sources.Select(source =>
            $"{AiTeacherGroundingMetadataFactory.GetSourceKindLabel(source.Kind)} · {source.Title}").ToList();
    }
}
