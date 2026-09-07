using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rota.Desktop;

/// <summary>
/// Arquivo pedagógico portátil do Rota. O pacote é deliberadamente declarativo:
/// ele descreve uma base de aprendizagem, sem scripts ou instruções executáveis.
/// </summary>
public sealed class LearningContentPackage
{
    public const string ExpectedFormat = "rota-learning-package";
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("format")]
    public string Format { get; set; } = ExpectedFormat;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("package")]
    public LearningPackageIdentity Package { get; set; } = new();

    [JsonPropertyName("catalog")]
    public LearningCatalog Catalog { get; set; } = new();

    public LearningContentPackage Copy() => new()
    {
        Format = Format,
        SchemaVersion = SchemaVersion,
        Package = Package.Copy(),
        Catalog = Catalog.Copy()
    };
}

public sealed class LearningPackageIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "pt-BR";

    public LearningPackageIdentity Copy() => new()
    {
        Id = Id,
        Version = Version,
        Title = Title,
        Locale = Locale
    };
}

public static class LearningContentPackageFormat
{
    public const int MaximumPackageBytes = 16 * 1024 * 1024;
    private static readonly Regex SemanticVersion = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.Compiled);
    private static readonly Regex Locale = new(@"^[a-z]{2,3}-[A-Z]{2}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64,
        WriteIndented = true
    };

    public static byte[] Serialize(LearningContentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var safeCopy = package.Copy();
        Validate(safeCopy);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(safeCopy, JsonOptions);
        if (bytes.Length > MaximumPackageBytes)
            throw new InvalidDataException("O pacote pedagógico excede o limite seguro de 16 MB.");
        return bytes;
    }

    public static LearningContentPackage Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is 0 or > MaximumPackageBytes)
            throw new InvalidDataException("O tamanho do pacote pedagógico é inválido.");

        try
        {
            var raw = utf8.ToArray();
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("O pacote pedagógico precisa ser um objeto JSON.");
            EnsureNoDuplicateProperties(document.RootElement);

            var package = JsonSerializer.Deserialize<LearningContentPackage>(raw, JsonOptions)
                ?? throw new InvalidDataException("O pacote pedagógico está vazio.");
            Validate(package);
            return package.Copy();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("O pacote pedagógico possui JSON inválido.", exception);
        }
    }

    public static void Validate(LearningContentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Format != LearningContentPackage.ExpectedFormat)
            throw new InvalidDataException("O formato do pacote pedagógico não é reconhecido.");
        if (package.SchemaVersion != LearningContentPackage.CurrentSchemaVersion)
            throw new InvalidDataException($"A versão do pacote pedagógico não é suportada: {package.SchemaVersion}.");
        if (package.Package is null || package.Catalog is null)
            throw new InvalidDataException("O pacote pedagógico está incompleto.");

        ValidateIdentity(package.Package);
        LearningCatalogValidator.Validate(package.Catalog);
        if (package.Catalog.Subjects.Count == 0 || package.Catalog.Courses.Count == 0 ||
            package.Catalog.Modules.Count == 0 || package.Catalog.Lessons.Count == 0 || package.Catalog.Contents.Count == 0)
        {
            throw new InvalidDataException("O pacote pedagógico precisa conter pelo menos uma trilha completa de conteúdo.");
        }
    }

    private static void ValidateIdentity(LearningPackageIdentity identity)
    {
        if (!LearningCatalogIds.IsValid(identity.Id))
            throw new InvalidDataException("O identificador do pacote pedagógico é inválido.");
        if (!SemanticVersion.IsMatch(identity.Version ?? ""))
            throw new InvalidDataException("A versão do pacote pedagógico precisa usar o formato maior.menor.revisão.");
        ValidateText(identity.Title, "título do pacote", 160, allowEmpty: false);
        if (!Locale.IsMatch(identity.Locale ?? ""))
            throw new InvalidDataException("O idioma do pacote pedagógico é inválido.");
    }

    private static void ValidateText(string? value, string field, int maximum, bool allowEmpty)
    {
        if (value is null || value.Length > maximum || value.Any(char.IsControl) ||
            (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException($"O campo {field} é inválido.");
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"O pacote pedagógico contém a propriedade duplicada '{property.Name}'.");
                EnsureNoDuplicateProperties(property.Value);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureNoDuplicateProperties(item);
        }
    }
}
