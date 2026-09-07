using System.Text.Json;

namespace Rota.Desktop;

/// <summary>Estado pessoal de aprendizagem, separado do calendário e dos pacotes.</summary>
public sealed class LearningProgressStore
{
    private const int CurrentVersion = 1;
    private readonly string _path;
    private readonly object _gate = new();
    private LearningProgressState _state;

    public LearningProgressStore(string path)
    {
        _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        _state = Load();
    }

    public LearningProgressSnapshot Snapshot()
    {
        lock (_gate) return new LearningProgressSnapshot(_state.CompletedContentIds.ToHashSet(StringComparer.Ordinal), _state.AssessmentAttempts.Select(item => item with { }).ToList());
    }

    public void MarkContentCompleted(string contentId)
    {
        RequireId(contentId);
        lock (_gate)
        {
            if (!_state.CompletedContentIds.Add(contentId)) return;
            Save();
        }
    }

    public void RecordAssessment(string assessmentId, int scorePercentage, DateTimeOffset completedAtUtc)
    {
        RequireId(assessmentId);
        if (scorePercentage is < 0 or > 100 || completedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("O resultado da avaliação é inválido.");
        lock (_gate)
        {
            _state.AssessmentAttempts.Add(new LearningAssessmentAttempt(assessmentId, scorePercentage, completedAtUtc));
            if (_state.AssessmentAttempts.Count > 2_000) _state.AssessmentAttempts.RemoveRange(0, _state.AssessmentAttempts.Count - 2_000);
            Save();
        }
    }

    private LearningProgressState Load()
    {
        if (!File.Exists(_path)) return new LearningProgressState();
        var state = JsonSerializer.Deserialize<LearningProgressState>(File.ReadAllBytes(_path)) ?? throw new InvalidDataException("O progresso de aprendizagem está vazio.");
        if (state.Version != CurrentVersion || state.CompletedContentIds is null || state.AssessmentAttempts is null || state.CompletedContentIds.Any(id => !LearningCatalogIds.IsValid(id)) || state.AssessmentAttempts.Count > 2_000 || state.AssessmentAttempts.Any(item => !LearningCatalogIds.IsValid(item.AssessmentId) || item.ScorePercentage is < 0 or > 100 || item.CompletedAtUtc.Offset != TimeSpan.Zero))
            throw new InvalidDataException("O progresso de aprendizagem é inválido.");
        return state;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(_state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static void RequireId(string id)
    {
        if (!LearningCatalogIds.IsValid(id)) throw new ArgumentException("O identificador de aprendizagem é inválido.", nameof(id));
    }

    private sealed class LearningProgressState
    {
        public int Version { get; set; } = CurrentVersion;
        public HashSet<string> CompletedContentIds { get; set; } = new(StringComparer.Ordinal);
        public List<LearningAssessmentAttempt> AssessmentAttempts { get; set; } = new();
    }
}

public sealed record LearningAssessmentAttempt(string AssessmentId, int ScorePercentage, DateTimeOffset CompletedAtUtc);
public sealed record LearningProgressSnapshot(IReadOnlySet<string> CompletedContentIds, IReadOnlyList<LearningAssessmentAttempt> AssessmentAttempts);
