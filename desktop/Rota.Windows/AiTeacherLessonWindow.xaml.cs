using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Rota.Desktop.LocalAI;

namespace Rota.Desktop;

/// <summary>
/// Janela de estudo vinculada a um único conteúdo verificado. Histórico, resumo e
/// memória por matéria ficam locais e passivos; nenhum deles é reenviado ao modelo.
/// </summary>
public partial class AiTeacherLessonWindow : Window
{
    private readonly AiTeacherLessonController _controller;
    private readonly IAiTeacherConversationStore _conversationStore;
    private readonly bool _ownsConversationStore;
    private readonly IAiTeacherLessonSummaryStore? _ownedSummaryStore;
    private readonly IAiTeacherSubjectBindingStore? _ownedSubjectBindingStore;
    private readonly IAiTeacherSubjectMemoryStore? _ownedSubjectMemoryStore;
    private CancellationTokenSource? _generationCancellation;
    private bool _ownedStoresDisposed;
    private bool _isGenerating;
    private Guid _conversationId;

    public ObservableCollection<AiTeacherExplanationStyleDescriptor> Styles { get; } = new();

    public AiTeacherLessonWindow(
        IAiTeacherService teacherService,
        AiTeacherLessonContext lessonContext,
        IAiTeacherConversationStore? conversationStore = null,
        IAiTeacherLessonSummaryService? summaryService = null,
        AiTeacherSubjectBinding? subjectBinding = null,
        IAiTeacherSubjectMemoryService? subjectMemoryService = null)
    {
        _conversationStore = conversationStore ?? new AiTeacherConversationStore();
        _ownsConversationStore = conversationStore is null;
        if (summaryService is null)
        {
            var summaryStore = new AiTeacherLessonSummaryStore();
            _ownedSummaryStore = summaryStore;
            summaryService = new AiTeacherLessonSummaryService(_conversationStore, summaryStore);
        }
        if (subjectBinding is not null && subjectMemoryService is null)
        {
            var bindingStore = new AiTeacherSubjectBindingStore();
            var memoryStore = new AiTeacherSubjectMemoryStore();
            _ownedSubjectBindingStore = bindingStore;
            _ownedSubjectMemoryStore = memoryStore;
            subjectMemoryService = new AiTeacherSubjectMemoryService(
                _conversationStore,
                bindingStore,
                memoryStore);
        }
        _controller = new AiTeacherLessonController(
            teacherService,
            lessonContext,
            _conversationStore,
            summaryService,
            subjectBinding,
            subjectMemoryService);
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
        if (!_isGenerating) DisposeOwnedStores();
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
        var generationCancellation = new CancellationTokenSource();
        _generationCancellation = generationCancellation;
        OperationNoticeText.Text = "A Professora Local está preparando uma explicação a partir do material interno…";
        RefreshInputState();
        try
        {
            var style = (StylePicker.SelectedItem as AiTeacherExplanationStyleDescriptor)?.Style
                ?? AiTeacherExplanationStyle.StepByStep;
            var turn = await _controller.AskAsync(
                _conversationId,
                question,
                studentAttempt: "",
                AiTeacherRequestMode.Explain,
                style,
                generationCancellation.Token);
            if (!IsLoaded) return;

            _conversationId = turn.ConversationId;
            ShowAnswer(turn.GroundedAnswer);
            var exchangeCount = turn.Conversation?.Exchanges.Count ?? 0;
            var summaryStatus = string.IsNullOrWhiteSpace(turn.SummaryWarning)
                ? " Resumo automático atualizado localmente."
                : " " + turn.SummaryWarning;
            var memoryStatus = string.IsNullOrWhiteSpace(turn.SubjectMemoryWarning)
                ? " Memória da matéria atualizada localmente."
                : " " + turn.SubjectMemoryWarning;
            OperationNoticeText.Text = turn.GroundedAnswer.Knowledge.CanAnswerSubstantively
                ? $"Explicação pronta. Conversa salva localmente · {exchangeCount} troca(s).{summaryStatus}{memoryStatus} As fontes e o nível de cobertura aparecem abaixo."
                : $"A Professora Local não iniciou o modelo: o pacote ainda não sustenta uma explicação segura. Conversa salva localmente · {exchangeCount} troca(s).{summaryStatus}{memoryStatus}";
        }
        catch (OperationCanceledException) when (generationCancellation.IsCancellationRequested)
        {
            if (IsLoaded)
                OperationNoticeText.Text = "Explicação cancelada. O cancelamento ficou registrado no histórico local; nenhuma alteração foi feita no seu plano ou progresso.";
        }
        catch (Exception ex) when (ex is AiInferenceException or AiContractValidationException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (IsLoaded) OperationNoticeText.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_generationCancellation, generationCancellation))
                _generationCancellation = null;
            generationCancellation.Dispose();
            _isGenerating = false;
            if (IsLoaded) RefreshInputState();
            else DisposeOwnedStores();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _generationCancellation?.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void DisposeOwnedStores()
    {
        if (_ownedStoresDisposed) return;
        _ownedStoresDisposed = true;
        if (_ownedSubjectMemoryStore is IDisposable memoryDisposable) memoryDisposable.Dispose();
        if (_ownedSubjectBindingStore is IDisposable bindingDisposable) bindingDisposable.Dispose();
        if (_ownedSummaryStore is IDisposable summaryDisposable) summaryDisposable.Dispose();
        if (_ownsConversationStore && _conversationStore is IDisposable conversationDisposable)
            conversationDisposable.Dispose();
    }

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
