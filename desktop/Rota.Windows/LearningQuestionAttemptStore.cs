using System.Text.Json;

namespace Rota.Desktop;

/// <summary>
/// Histórico local de respostas a questões práticas. Fica separado do calendário,
/// do StudyPlan e dos pacotes pedagógicos para não misturar progresso de aprendizagem
/// com a agenda principal.
/// </summary>
public sealed class LearningQuestionAttemptStore
{
    private const int CurrentVersion = 1;
    public const int MaximumAttempts = 10_000;

    private readonly string _path;
    private readonly object _gate = new();
    private LearningQuestionAttemptState _state;

    public LearningQuestionAttemptStore(string path)
    {
        _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        _state = Load();
    }

    public LearningQuestionAttemptSnapshot Snapshot()
    {
        lock (_gate)
            return new LearningQuestionAttemptSnapshot(_state.Attempts.Select(item => item with { }).ToList());
    }

    public LearningQuestionAttempt Record(
        string questionId,
        string contentId,
        string selectedOptionId,
        bool isCorrect,
        DateTimeOffset answeredAtUtc)
    {
        RequireId(questionId, nameof(questionId));
        RequireId(contentId, nameof(contentId));
        RequireId(selectedOptionId, nameof(selectedOptionId));
        if (answeredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A tentativa precisa usar horário UTC.", nameof(answeredAtUtc));

        var attempt = new LearningQuestionAttempt(questionId, contentId, selectedOptionId, isCorrect, answeredAtUtc);
        lock (_gate)
        {
            var next = CopyState();
            next.Attempts.Add(attempt);
            if (next.Attempts.Count > MaximumAttempts)
                next.Attempts.RemoveRange(0, next.Attempts.Count - MaximumAttempts);
            Save(next);
            _state = next;
        }
        return attempt;
    }

    /// <summary>Remove somente a tentativa mais recente da questão informada.</summary>
    public bool UndoLatest(string questionId)
    {
        RequireId(questionId, nameof(questionId));
        lock (_gate)
        {
            var index = -1;
            for (var current = _state.Attempts.Count - 1; current >= 0; current--)
            {
                if (!string.Equals(_state.Attempts[current].QuestionId, questionId, StringComparison.Ordinal))
                    continue;
                index = current;
                break;
            }
            if (index < 0) return false;

            var next = CopyState();
            next.Attempts.RemoveAt(index);
            Save(next);
            _state = next;
            return true;
        }
    }

    private LearningQuestionAttemptState CopyState() => new()
    {
        Version = CurrentVersion,
        Attempts = _state.Attempts.Select(item => item with { }).ToList()
    };

    private LearningQuestionAttemptState Load()
    {
        if (!File.Exists(_path)) return new LearningQuestionAttemptState();
        var state = JsonSerializer.Deserialize<LearningQuestionAttemptState>(File.ReadAllBytes(_path))
            ?? throw new InvalidDataException("O histórico de questões está vazio.");
        if (state.Version != CurrentVersion || state.Attempts is null || state.Attempts.Count > MaximumAttempts ||
            state.Attempts.Any(item => item is null ||
                !LearningCatalogIds.IsValid(item.QuestionId) ||
                !LearningCatalogIds.IsValid(item.ContentId) ||
                !LearningCatalogIds.IsValid(item.SelectedOptionId) ||
                item.AnsweredAtUtc.Offset != TimeSpan.Zero))
        {
            throw new InvalidDataException("O histórico de questões é inválido.");
        }
        return state;
    }

    private void Save(LearningQuestionAttemptState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static void RequireId(string id, string parameterName)
    {
        if (!LearningCatalogIds.IsValid(id))
            throw new ArgumentException("O identificador da tentativa é inválido.", parameterName);
    }

    private sealed class LearningQuestionAttemptState
    {
        public int Version { get; set; } = CurrentVersion;
        public List<LearningQuestionAttempt> Attempts { get; set; } = new();
    }
}

public sealed record LearningQuestionAttempt(
    string QuestionId,
    string ContentId,
    string SelectedOptionId,
    bool IsCorrect,
    DateTimeOffset AnsweredAtUtc);

public sealed record LearningQuestionAttemptSnapshot(IReadOnlyList<LearningQuestionAttempt> Attempts);

