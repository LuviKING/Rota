using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rota.Desktop;

public sealed class StudyRepository
{
    private const int CurrentStateVersion = 1;
    private const long MaxStateFileBytes = 8 * 1024 * 1024;
    private const int MaxStoredSessions = 20_000;
    private readonly object _gate = new();
    private readonly string _dataPath;
    private readonly Func<DateTime> _now;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private AppState _state;

    public string DataPath => _dataPath;
    public string DataDirectory => Path.GetDirectoryName(_dataPath)!;
    public string LastLoadWarning { get; private set; } = "";

    public StudyRepository(string? dataPath = null, Func<DateTime>? nowProvider = null)
    {
        _dataPath = Path.GetFullPath(dataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rota",
            "desktop-state.json"));
        _now = nowProvider ?? (() => DateTime.Now);
        _state = LoadState();
    }

    public AppSettings Settings
    {
        get
        {
            lock (_gate) return CloneSettings(_state.Settings);
        }
    }

    public IReadOnlyList<SessionItem> SessionsForDate(DateOnly date)
    {
        var iso = Iso(date);
        lock (_gate)
        {
            return _state.Sessions
                .Where(s => s.Date == iso)
                .OrderBy(s => s.IsCompleted ? 1 : 0)
                .ThenBy(s => KindOrder(s.Kind))
                .ThenBy(s => s.Subject, StringComparer.CurrentCultureIgnoreCase)
                .Select(s => s.Copy())
                .ToList();
        }
    }

    public IReadOnlyList<SessionItem> UpcomingSessions(DateOnly fromDate, int limit = 6)
    {
        var iso = Iso(fromDate);
        lock (_gate)
        {
            return _state.Sessions
                .Where(s => !s.IsCompleted && string.CompareOrdinal(s.Date, iso) >= 0)
                .OrderBy(s => s.Date, StringComparer.Ordinal)
                .ThenBy(s => KindOrder(s.Kind))
                .ThenBy(s => s.Subject, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Max(1, limit))
                .Select(s => s.Copy())
                .ToList();
        }
    }

    public SessionProgress ProgressForDate(DateOnly date)
    {
        var sessions = SessionsForDate(date);
        return new SessionProgress(sessions.Count(s => s.IsCompleted), sessions.Count, sessions.Sum(s => s.Minutes));
    }

    public IReadOnlyList<WeekDaySummary> WeekSummary(DateOnly anyDate)
    {
        var monday = StartOfWeek(anyDate);
        var result = new List<WeekDaySummary>(7);
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            var progress = ProgressForDate(date);
            result.Add(new WeekDaySummary(date, progress.Completed, progress.Total, progress.Minutes));
        }
        return result;
    }

    public ApplyResult PreviewPlan(PlanPackage plan)
    {
        lock (_gate) return EvaluatePlan(plan).Result;
    }

    public ApplyResult ApplyPlan(PlanPackage plan)
    {
        lock (_gate)
        {
            var evaluation = EvaluatePlan(plan);
            if (!evaluation.Result.Success) return evaluation.Result;

            var next = CloneState(_state);
            var today = Iso(DateOnly.FromDateTime(_now()));
            var removed = next.Sessions.RemoveAll(s =>
                s.Origin == "plan" && !s.IsCompleted && string.CompareOrdinal(s.Date, today) >= 0);

            foreach (var incoming in evaluation.Eligible)
                next.Sessions.Add(incoming.Copy());

            next.Settings.ActivePlanId = plan.PlanId;
            next.Settings.ActivePlanRevision = plan.Revision;
            next.Settings.ActivePlanTitle = plan.Title;
            next.Settings.ObjectiveName = plan.ObjectiveName;
            next.Settings.ObjectiveDate = plan.ObjectiveDate;
            next.Settings.PlanRevisions[plan.PlanId] = plan.Revision;
            Commit(next);

            return new ApplyResult(true, evaluation.Eligible.Count, removed, "Plano aplicado com segurança.");
        }
    }

    public bool MarkCompleted(string planId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(sessionId)) return false;
        lock (_gate)
        {
            var next = CloneState(_state);
            var item = FindIdentity(next, planId, sessionId);
            if (item is null || item.IsCompleted) return false;

            var now = _now();
            item.Status = "completed";
            item.CompletedAtUnixMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

            if (item.Kind == "study")
            {
                var completionDate = DateOnly.FromDateTime(now);
                foreach (var day in EnabledReviewDays())
                {
                    var reviewId = item.Id + StudyPlanImporter.RuntimeReviewIdMarker + day;
                    if (FindIdentity(next, item.PlanId, reviewId) is not null) continue;

                    next.Sessions.Add(new SessionItem
                    {
                        Id = reviewId,
                        PlanId = item.PlanId,
                        PlanRevision = item.PlanRevision,
                        Date = Iso(completionDate.AddDays(day)),
                        Subject = item.Subject,
                        Topic = item.Topic,
                        Minutes = ReviewMinutes(item.Minutes),
                        Target = "Recupere de memória e faça 5 questões.",
                        Kind = "review",
                        ReviewLabel = "D+" + day,
                        Status = "planned",
                        Origin = "runtime"
                    });
                }
            }

            Commit(next);
            return true;
        }
    }

    public void SavePreferences(string objectiveName, string objectiveDate, int dailyHours, int blockMinutes, bool d1, bool d3, bool d7)
    {
        objectiveDate = (objectiveDate ?? "").Trim();
        if (objectiveDate.Length > 0 && !DateOnly.TryParseExact(objectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Use uma data válida no formato AAAA-MM-DD.");
        var cleanObjectiveName = CleanText(objectiveName, "Meu objetivo", 120, "Nome do objetivo");

        lock (_gate)
        {
            var next = CloneState(_state);
            next.Settings.ObjectiveName = cleanObjectiveName;
            next.Settings.ObjectiveDate = objectiveDate;
            next.Settings.DailyHours = Math.Clamp(dailyHours, 1, 12);
            next.Settings.BlockMinutes = Math.Clamp(blockMinutes, 30, 180);
            next.Settings.ReviewD1 = d1;
            next.Settings.ReviewD3 = d3;
            next.Settings.ReviewD7 = d7;
            Commit(next);
        }
    }

    public void ExportBackup(string destinationPath)
    {
        lock (_gate)
        {
            WriteStateAtomically(CloneState(_state), destinationPath, keepRecoveryBackup: false);
        }
    }

    private (ApplyResult Result, List<SessionItem> Eligible) EvaluatePlan(PlanPackage plan)
    {
        var knownRevision = _state.Settings.PlanRevisions.TryGetValue(plan.PlanId, out var storedRevision) ? storedRevision : 0;
        knownRevision = Math.Max(knownRevision, _state.Sessions.Where(s => s.PlanId == plan.PlanId).Select(s => s.PlanRevision).DefaultIfEmpty(0).Max());
        if (_state.Settings.ActivePlanId == plan.PlanId)
            knownRevision = Math.Max(knownRevision, _state.Settings.ActivePlanRevision);

        if (plan.Revision <= knownRevision)
        {
            return (new ApplyResult(false, 0, 0,
                $"A revisão importada precisa ser maior que a maior revisão já aplicada deste plano ({knownRevision})."), new());
        }

        var today = Iso(DateOnly.FromDateTime(_now()));
        var eligible = new List<SessionItem>();
        foreach (var incoming in plan.Sessions)
        {
            if (string.CompareOrdinal(incoming.Date, today) < 0) continue;
            var existing = FindIdentity(incoming.PlanId, incoming.Id);
            if (existing is not null && (existing.IsCompleted || existing.Origin == "runtime" || string.CompareOrdinal(existing.Date, today) < 0))
                continue;
            eligible.Add(incoming.Copy());
        }

        if (eligible.Count == 0)
            return (new ApplyResult(false, 0, 0, "O plano não contém nenhuma sessão futura que possa ser aplicada com segurança."), eligible);

        var protectedSessions = _state.Sessions.Where(s =>
            string.CompareOrdinal(s.Date, today) >= 0 && (s.IsCompleted || s.Origin == "runtime"));
        var capacity = eligible.Concat(protectedSessions).ToList();
        var dailyLimit = Math.Clamp(_state.Settings.DailyHours, 1, 12) * 60;
        var overloadedDate = FirstOverloadedDate(capacity, dailyLimit);
        if (overloadedDate.Length > 0)
        {
            var shown = DateOnly.ParseExact(overloadedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
            return (new ApplyResult(false, 0, 0,
                $"O plano, somado ao histórico concluído e às revisões automáticas preservadas, ultrapassa seu limite diário em {shown}. Ajuste o plano ou o limite diário.", overloadedDate), eligible);
        }

        var removable = _state.Sessions.Count(s => s.Origin == "plan" && !s.IsCompleted && string.CompareOrdinal(s.Date, today) >= 0);
        return (new ApplyResult(true, eligible.Count, removable, $"{eligible.Count} sessões prontas para aplicar. {removable} sessões futuras pendentes serão substituídas."), eligible);
    }

    private SessionItem? FindIdentity(string planId, string id) =>
        FindIdentity(_state, planId, id);

    private static SessionItem? FindIdentity(AppState state, string planId, string id) =>
        state.Sessions.FirstOrDefault(s => s.PlanId == planId && s.Id == id);

    private IEnumerable<int> EnabledReviewDays()
    {
        if (_state.Settings.ReviewD1) yield return 1;
        if (_state.Settings.ReviewD3) yield return 3;
        if (_state.Settings.ReviewD7) yield return 7;
    }

    public static int ReviewMinutes(int originalMinutes) => Math.Max(15, Math.Min(35, originalMinutes / 3));

    public static string FirstOverloadedDate(IEnumerable<SessionItem> sessions, int maxMinutesPerDay)
    {
        if (maxMinutesPerDay <= 0) return "";
        var totals = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            totals.TryGetValue(session.Date, out var current);
            totals[session.Date] = current + Math.Max(0L, session.Minutes);
        }
        return totals.FirstOrDefault(entry => entry.Value > maxMinutesPerDay).Key ?? "";
    }

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int KindOrder(string kind) => kind switch
    {
        "review" => 0,
        "study" => 1,
        "assessment" => 2,
        _ => 3
    };

    private AppState LoadState()
    {
        if (!File.Exists(_dataPath))
        {
            var fresh = CreateFreshState();
            SaveStateInternal(fresh);
            return fresh;
        }

        try
        {
            return ReadState(_dataPath);
        }
        catch (Exception ex) when (IsInvalidStateError(ex))
        {
            var corruptPath = PreserveInvalidFile(_dataPath, "desktop-state.corrupt");
            var backupPath = _dataPath + ".bak";
            try
            {
                if (File.Exists(backupPath))
                {
                    var recovered = ReadState(backupPath);
                    SaveStateInternal(recovered);
                    LastLoadWarning =
                        $"O estado principal estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. " +
                        "O Rota recuperou automaticamente a última versão íntegra.";
                    return recovered;
                }
            }
            catch (Exception backupError) when (IsInvalidStateError(backupError))
            {
                PreserveInvalidFile(backupPath, "desktop-state.backup-corrupt");
            }

            var fresh = CreateFreshState();
            SaveStateInternal(fresh);
            LastLoadWarning =
                $"O estado local estava inválido e foi preservado como {Path.GetFileName(corruptPath)}. " +
                "Não havia uma cópia íntegra anterior; o Rota iniciou um estado novo.";
            return fresh;
        }
    }

    private AppState ReadState(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxStateFileBytes)
            throw new InvalidDataException("O arquivo de estado excede o limite seguro de 8 MB.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        var loaded = JsonSerializer.Deserialize<AppState>(json, _jsonOptions)
            ?? throw new InvalidDataException("O arquivo de estado está vazio.");
        ValidateState(loaded);
        return CloneState(loaded);
    }

    private void Commit(AppState next)
    {
        ValidateState(next);
        SaveStateInternal(next);
        _state = next;
    }

    private void SaveStateInternal(AppState state) =>
        WriteStateAtomically(state, _dataPath, keepRecoveryBackup: true);

    private void WriteStateAtomically(AppState state, string destinationPath, bool keepRecoveryBackup)
    {
        ValidateState(state);
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Não foi possível determinar a pasta de destino.");
        Directory.CreateDirectory(directory);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
        if (bytes.LongLength > MaxStateFileBytes)
            throw new IOException("O estado do Rota excedeu o limite seguro de 8 MB.");

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(fullPath))
            {
                File.Move(tempPath, fullPath);
            }
            else if (keepRecoveryBackup)
            {
                File.Replace(tempPath, fullPath, fullPath + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullPath, overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private string PreserveInvalidFile(string path, string prefix)
    {
        if (!File.Exists(path)) return "";
        Directory.CreateDirectory(DataDirectory);
        var preserved = Path.Combine(DataDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Move(path, preserved);
        return preserved;
    }

    private static bool IsInvalidStateError(Exception ex) =>
        ex is JsonException or InvalidDataException or DecoderFallbackException or NotSupportedException;

    private static AppState CreateFreshState() => new()
    {
        StateVersion = CurrentStateVersion,
        Settings = new AppSettings(),
        Sessions = new List<SessionItem>()
    };

    private static void ValidateState(AppState state)
    {
        if (state.StateVersion != CurrentStateVersion)
            throw new InvalidDataException($"Versão de estado não suportada: {state.StateVersion}.");
        if (state.Settings is null || state.Sessions is null)
            throw new InvalidDataException("O estado local está incompleto.");

        var settings = state.Settings;
        ValidateStoredText(settings.ObjectiveName, "ObjectiveName", 120, allowEmpty: false);
        ValidateStoredText(settings.ObjectiveDate, "ObjectiveDate", 10, allowEmpty: true);
        if (settings.ObjectiveDate.Length > 0 && !DateOnly.TryParseExact(settings.ObjectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("ObjectiveDate contém uma data inválida.");
        if (settings.DailyHours is < 1 or > 12 || settings.BlockMinutes is < 30 or > 180)
            throw new InvalidDataException("As preferências de duração estão fora dos limites.");
        ValidateStoredText(settings.ActivePlanId, "ActivePlanId", 80, allowEmpty: true);
        ValidateStoredText(settings.ActivePlanTitle, "ActivePlanTitle", 120, allowEmpty: true);
        if (settings.ActivePlanRevision < 0 || (settings.ActivePlanId.Length > 0 && settings.ActivePlanRevision < 1))
            throw new InvalidDataException("A revisão do plano ativo é inválida.");
        if (settings.PlanRevisions is null || settings.PlanRevisions.Count > 10_000)
            throw new InvalidDataException("O índice de revisões de planos é inválido.");
        foreach (var entry in settings.PlanRevisions)
        {
            ValidateStoredText(entry.Key, "PlanRevisions.id", 80, allowEmpty: false);
            if (entry.Value < 1) throw new InvalidDataException("O índice contém uma revisão inválida.");
        }

        if (state.Sessions.Count > MaxStoredSessions)
            throw new InvalidDataException($"O estado excede o limite de {MaxStoredSessions} sessões armazenadas.");
        var identities = new HashSet<(string PlanId, string Id)>();
        foreach (var session in state.Sessions)
        {
            if (session is null) throw new InvalidDataException("O estado contém uma sessão nula.");
            ValidateStoredText(session.Id, "Session.Id", 100, allowEmpty: false);
            ValidateStoredText(session.PlanId, "Session.PlanId", 80, allowEmpty: false);
            if (!identities.Add((session.PlanId, session.Id)))
                throw new InvalidDataException("O estado contém identidades de sessão duplicadas.");
            if (session.PlanRevision < 1) throw new InvalidDataException("Uma sessão possui revisão inválida.");
            ValidateStoredText(session.Date, "Session.Date", 10, allowEmpty: false);
            if (!DateOnly.TryParseExact(session.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Uma sessão possui data inválida.");
            ValidateStoredText(session.Subject, "Session.Subject", 80, allowEmpty: false);
            ValidateStoredText(session.Topic, "Session.Topic", 160, allowEmpty: false);
            ValidateStoredText(session.Target, "Session.Target", 180, allowEmpty: true);
            ValidateStoredText(session.ReviewLabel, "Session.ReviewLabel", 40, allowEmpty: true);
            if (session.Minutes is < 10 or > 360) throw new InvalidDataException("Uma sessão possui duração inválida.");
            if (session.Kind is not "study" and not "review" and not "assessment") throw new InvalidDataException("Uma sessão possui kind inválido.");
            if (session.Kind != "review" && session.ReviewLabel.Length > 0) throw new InvalidDataException("Uma sessão não-review possui review_label.");
            if (session.Status is not "planned" and not "completed") throw new InvalidDataException("Uma sessão possui status inválido.");
            if (session.Origin is not "plan" and not "runtime") throw new InvalidDataException("Uma sessão possui origem inválida.");
            var usesRuntimeNamespace = session.Id.Contains(StudyPlanImporter.RuntimeReviewIdMarker, StringComparison.Ordinal);
            if (session.Origin == "runtime" && (session.Kind != "review" || !usesRuntimeNamespace))
                throw new InvalidDataException("Uma revisão runtime possui identidade inválida.");
            if (session.Origin == "plan" && usesRuntimeNamespace)
                throw new InvalidDataException("Uma sessão importada usa o namespace runtime.");
            if (session.IsCompleted != (session.CompletedAtUnixMs > 0))
                throw new InvalidDataException("O registro de conclusão de uma sessão é inconsistente.");
        }
    }

    private static void ValidateStoredText(string? value, string field, int max, bool allowEmpty)
    {
        if (value is null || value.Length > max || value.Any(char.IsControl) ||
            (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException($"{field} contém texto inválido.");
    }

    private static string CleanText(string? value, string fallback, int max, string field)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (clean.Length > max) throw new ArgumentException($"{field} deve ter no máximo {max} caracteres.");
        if (clean.Any(char.IsControl)) throw new ArgumentException($"{field} contém caractere de controle não permitido.");
        return clean;
    }

    private static AppState CloneState(AppState source) => new()
    {
        StateVersion = source.StateVersion,
        Settings = CloneSettings(source.Settings),
        Sessions = source.Sessions.Select(session => session.Copy()).ToList()
    };

    private static AppSettings CloneSettings(AppSettings source) => new()
    {
        ObjectiveName = source.ObjectiveName,
        ObjectiveDate = source.ObjectiveDate,
        DailyHours = source.DailyHours,
        BlockMinutes = source.BlockMinutes,
        ReviewD1 = source.ReviewD1,
        ReviewD3 = source.ReviewD3,
        ReviewD7 = source.ReviewD7,
        ActivePlanId = source.ActivePlanId,
        ActivePlanRevision = source.ActivePlanRevision,
        ActivePlanTitle = source.ActivePlanTitle,
        PlanRevisions = new Dictionary<string, int>(source.PlanRevisions, StringComparer.Ordinal)
    };
}

