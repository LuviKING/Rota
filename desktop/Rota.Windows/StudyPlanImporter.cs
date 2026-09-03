using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rota.Desktop;

public static partial class StudyPlanImporter
{
    public const int MaxCodeChars = 256_000;
    public const int MaxSessions = 1_000;
    public const string RuntimeReviewIdMarker = "::review::";

    public static PlanPackage Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Cole um StudyPlan Code antes de importar.");
        if (raw.Length > MaxCodeChars)
            throw new ArgumentException("O plano excede o limite de 256 mil caracteres.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
        }
        catch (JsonException)
        {
            throw new ArgumentException("JSON inválido. Verifique vírgulas, aspas, tipos e chaves.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("O StudyPlan precisa ser um objeto JSON.");

            RejectUnknownKeys(root, "raiz", "format", "format_version", "plan", "objective", "sessions");

            if (!string.Equals(RequiredText(root, "format", 20), "studyplan", StringComparison.Ordinal))
                throw new ArgumentException("Campo format deve ser \"studyplan\".");

            var formatVersion = RequiredText(root, "format_version", 10);
            if (formatVersion is not "0.1" and not "0.2")
                throw new ArgumentException("Versão do StudyPlan não suportada: " + formatVersion);

            var plan = RequireObject(root, "plan");
            RejectUnknownKeys(plan, "plan", "id", "revision", "title");
            var planId = RequiredText(plan, "id", 80);
            var revision = RequiredInteger(plan, "revision");
            if (revision < 1)
                throw new ArgumentException("plan.revision deve ser maior que zero.");
            var title = OptionalNonBlankText(plan, "title", "Meu plano", 120);

            string objectiveName = title;
            string objectiveDate = "";
            if (root.TryGetProperty("objective", out var objectiveRaw))
            {
                if (objectiveRaw.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("objective precisa ser um objeto quando informado.");
                RejectUnknownKeys(objectiveRaw, "objective", "name", "date");
                objectiveName = OptionalNonBlankText(objectiveRaw, "name", title, 120);
                objectiveDate = OptionalText(objectiveRaw, "date", "", 10);
                if (objectiveDate.Length > 0)
                    RequireIsoDate(objectiveDate, "objective.date");
            }

            if (!root.TryGetProperty("sessions", out var sessionsRaw) || sessionsRaw.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Campo obrigatório ausente ou inválido: sessions.");
            if (sessionsRaw.GetArrayLength() == 0)
                throw new ArgumentException("O plano não possui sessões.");
            if (sessionsRaw.GetArrayLength() > MaxSessions)
                throw new ArgumentException("O plano excede o limite de 1000 sessões.");

            var sessions = new List<SessionItem>(sessionsRaw.GetArrayLength());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in sessionsRaw.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException($"sessions[{index}] precisa ser um objeto.");

                RejectUnknownKeys(item, $"sessions[{index}]", "id", "date", "subject", "topic", "minutes", "target", "kind", "review_label");

                var id = RequiredText(item, "id", 100);
                if (id.Contains(RuntimeReviewIdMarker, StringComparison.Ordinal))
                    throw new ArgumentException($"O ID {id} usa o marcador reservado {RuntimeReviewIdMarker}.");
                if (!ids.Add(id))
                    throw new ArgumentException("ID de sessão duplicado: " + id);

                var date = RequiredText(item, "date", 10);
                RequireIsoDate(date, $"sessions[{index}].date");
                if (objectiveDate.Length > 0 && string.CompareOrdinal(date, objectiveDate) > 0)
                    throw new ArgumentException($"A sessão {id} está depois da data do objetivo ({objectiveDate}).");

                var subject = RequiredText(item, "subject", 80);
                var topic = RequiredText(item, "topic", 160);
                var minutes = RequiredInteger(item, "minutes");
                if (minutes < 10 || minutes > 360)
                    throw new ArgumentException("minutes deve ficar entre 10 e 360.");

                var target = formatVersion == "0.2"
                    ? RequiredText(item, "target", 180)
                    : OptionalText(item, "target", "", 180);
                var kind = OptionalText(item, "kind", "study", 20);
                if (kind is not "study" and not "review" and not "assessment")
                    throw new ArgumentException("kind inválido em " + id + ".");
                var reviewLabel = OptionalText(item, "review_label", "", 40);
                if (kind != "review" && reviewLabel.Length > 0)
                    throw new ArgumentException("review_label só pode ser usado em sessões kind=\"review\".");

                sessions.Add(new SessionItem
                {
                    Id = id,
                    PlanId = planId,
                    PlanRevision = revision,
                    Date = date,
                    Subject = subject,
                    Topic = topic,
                    Minutes = minutes,
                    Target = target,
                    Kind = kind,
                    ReviewLabel = reviewLabel,
                    Status = "planned",
                    Origin = "plan"
                });
                index++;
            }

            return new PlanPackage(planId, revision, title, objectiveName, objectiveDate, sessions);
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Campo obrigatório ausente ou inválido: {key}.");
        return value;
    }

    private static string RequiredText(JsonElement parent, string key, int max)
    {
        if (!parent.TryGetProperty(key, out var value))
            throw new ArgumentException("Campo obrigatório ausente: " + key);
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException(key + " precisa ser texto.");
        var text = (value.GetString() ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException("Campo obrigatório vazio: " + key);
        ValidateText(text, key, max);
        return text;
    }

    private static string OptionalNonBlankText(JsonElement parent, string key, string fallback, int max)
    {
        if (!parent.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException(key + " precisa ser texto quando informado.");
        var text = (value.GetString() ?? "").Trim();
        if (text.Length == 0) text = fallback;
        ValidateText(text, key, max);
        return text;
    }

    private static string OptionalText(JsonElement parent, string key, string fallback, int max)
    {
        if (!parent.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException(key + " precisa ser texto quando informado.");
        var text = (value.GetString() ?? "").Trim();
        ValidateText(text, key, max);
        return text;
    }

    private static int RequiredInteger(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var value))
            throw new ArgumentException("Campo obrigatório ausente: " + key);
        if (value.ValueKind != JsonValueKind.Number || !IntegerLexemeRegex().IsMatch(value.GetRawText()) || !value.TryGetInt32(out var result))
            throw new ArgumentException(key + " precisa ser um número inteiro.");
        return result;
    }

    private static void RejectUnknownKeys(JsonElement element, string scope, params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new ArgumentException($"Campo duplicado em {scope}: {property.Name}.");
            if (!allowedSet.Contains(property.Name))
                throw new ArgumentException($"Campo não suportado em {scope}: {property.Name}.");
        }
    }

    private static void ValidateText(string text, string field, int max)
    {
        foreach (var ch in text)
        {
            if (char.IsControl(ch))
                throw new ArgumentException(field + " contém caractere de controle não permitido.");
        }
        if (text.Length > max)
            throw new ArgumentException($"{field} excede {max} caracteres.");
    }

    private static void RequireIsoDate(string value, string field)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException(field + " deve usar AAAA-MM-DD.");
    }

    [GeneratedRegex("^-?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerLexemeRegex();
}

