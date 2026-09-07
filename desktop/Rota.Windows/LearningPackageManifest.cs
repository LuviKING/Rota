using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rota.Desktop;

/// <summary>
/// Descritor verificável de um pacote pedagógico. O manifesto não executa nada:
/// ele informa quem publicou o material, para qual nível ele serve, qual versão
/// do Rota o suporta e quais bytes exatos devem ser aceitos.
/// </summary>
public sealed class LearningPackageManifest
{
    public const string ExpectedFormat = "rota-learning-package-manifest";
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("format")]
    public string Format { get; set; } = ExpectedFormat;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("package")]
    public LearningPackageArtifact Package { get; set; } = new();

    [JsonPropertyName("author")]
    public LearningPackageAuthor Author { get; set; } = new();

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "pt-BR";

    [JsonPropertyName("education_level")]
    public string EducationLevel { get; set; } = "";

    [JsonPropertyName("minimum_app_version")]
    public string MinimumAppVersion { get; set; } = "";

    [JsonPropertyName("maximum_app_version")]
    public string MaximumAppVersion { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    public LearningPackageManifest Copy() => new()
    {
        Format = Format,
        SchemaVersion = SchemaVersion,
        Package = Package.Copy(),
        Author = Author.Copy(),
        Title = Title,
        Locale = Locale,
        EducationLevel = EducationLevel,
        MinimumAppVersion = MinimumAppVersion,
        MaximumAppVersion = MaximumAppVersion,
        CreatedAtUtc = CreatedAtUtc
    };
}

public sealed class LearningPackageArtifact
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    public LearningPackageArtifact Copy() => new() { Id = Id, Version = Version, Sha256 = Sha256, Bytes = Bytes };
}

public sealed class LearningPackageAuthor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    public LearningPackageAuthor Copy() => new() { Id = Id, Name = Name };
}

public static class LearningEducationLevels
{
    public const string General = "geral";
    public const string Fundamental = "fundamental";
    public const string HighSchool = "medio";
    public const string Enem = "enem";
    public const string HigherEducation = "superior";

    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        General, Fundamental, HighSchool, Enem, HigherEducation
    };

    public static bool IsValid(string? value) => value is not null && Values.Contains(value);
}

public static class LearningPackageManifestFormat
{
    public const int MaximumManifestBytes = 64 * 1024;
    private static readonly Regex SemanticVersion = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.Compiled);
    private static readonly Regex Locale = new(@"^[a-z]{2,3}-[A-Z]{2}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        WriteIndented = true
    };

    public static LearningPackageManifest Create(
        LearningContentPackage package,
        LearningPackageAuthor author,
        string educationLevel,
        Version minimumAppVersion,
        Version? maximumAppVersion = null,
        DateTimeOffset? createdAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(minimumAppVersion);
        var bytes = LearningContentPackageFormat.Serialize(package);
        var manifest = new LearningPackageManifest
        {
            Package = new LearningPackageArtifact
            {
                Id = package.Package.Id,
                Version = package.Package.Version,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Bytes = bytes.Length
            },
            Author = author.Copy(),
            Title = package.Package.Title,
            Locale = package.Package.Locale,
            EducationLevel = educationLevel,
            MinimumAppVersion = ToThreePartVersion(minimumAppVersion),
            MaximumAppVersion = maximumAppVersion is null ? "" : ToThreePartVersion(maximumAppVersion),
            CreatedAtUtc = (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
        Validate(manifest);
        return manifest;
    }

    public static byte[] Serialize(LearningPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var safeCopy = manifest.Copy();
        Validate(safeCopy);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(safeCopy, JsonOptions);
        if (bytes.Length > MaximumManifestBytes)
            throw new InvalidDataException("O manifesto pedagógico excede o limite seguro de 64 KB.");
        return bytes;
    }

    public static LearningPackageManifest Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is 0 or > MaximumManifestBytes)
            throw new InvalidDataException("O tamanho do manifesto pedagógico é inválido.");

        try
        {
            var raw = utf8.ToArray();
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("O manifesto pedagógico precisa ser um objeto JSON.");
            EnsureNoDuplicateProperties(document.RootElement);

            var manifest = JsonSerializer.Deserialize<LearningPackageManifest>(raw, JsonOptions)
                ?? throw new InvalidDataException("O manifesto pedagógico está vazio.");
            Validate(manifest);
            return manifest.Copy();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("O manifesto pedagógico possui JSON inválido.", exception);
        }
    }

    public static LearningContentPackage VerifyPackage(
        LearningPackageManifest manifest,
        ReadOnlySpan<byte> packageBytes,
        Version applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(applicationVersion);
        Validate(manifest);
        if (!IsCompatibleWith(manifest, applicationVersion))
            throw new InvalidDataException("O pacote pedagógico não é compatível com esta versão do Rota.");
        if (packageBytes.Length != manifest.Package.Bytes)
            throw new InvalidDataException("O tamanho do pacote pedagógico não corresponde ao manifesto.");

        var actualHash = SHA256.HashData(packageBytes);
        var expectedHash = Convert.FromHexString(manifest.Package.Sha256);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException("A assinatura SHA-256 do pacote pedagógico não corresponde ao manifesto.");

        var package = LearningContentPackageFormat.Parse(packageBytes);
        if (package.Package.Id != manifest.Package.Id || package.Package.Version != manifest.Package.Version ||
            package.Package.Title != manifest.Title || package.Package.Locale != manifest.Locale)
        {
            throw new InvalidDataException("A identidade do pacote pedagógico não corresponde ao manifesto.");
        }
        return package;
    }

    public static bool IsCompatibleWith(LearningPackageManifest manifest, Version applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(applicationVersion);
        Validate(manifest);
        var current = ParseThreePartVersion(applicationVersion);
        var minimum = ParseThreePartVersion(manifest.MinimumAppVersion);
        if (current < minimum) return false;
        return manifest.MaximumAppVersion.Length == 0 || current <= ParseThreePartVersion(manifest.MaximumAppVersion);
    }

    public static void Validate(LearningPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Format != LearningPackageManifest.ExpectedFormat)
            throw new InvalidDataException("O formato do manifesto pedagógico não é reconhecido.");
        if (manifest.SchemaVersion != LearningPackageManifest.CurrentSchemaVersion)
            throw new InvalidDataException($"A versão do manifesto pedagógico não é suportada: {manifest.SchemaVersion}.");
        if (manifest.Package is null || manifest.Author is null)
            throw new InvalidDataException("O manifesto pedagógico está incompleto.");

        ValidateArtifact(manifest.Package);
        if (!LearningCatalogIds.IsValid(manifest.Author.Id))
            throw new InvalidDataException("O identificador do autor do pacote é inválido.");
        ValidateText(manifest.Author.Name, "nome do autor", 160, allowEmpty: false);
        ValidateText(manifest.Title, "título do pacote", 160, allowEmpty: false);
        if (!Locale.IsMatch(manifest.Locale ?? ""))
            throw new InvalidDataException("O idioma do manifesto pedagógico é inválido.");
        if (!LearningEducationLevels.IsValid(manifest.EducationLevel))
            throw new InvalidDataException("O nível escolar do manifesto pedagógico é inválido.");

        var minimum = ParseThreePartVersion(manifest.MinimumAppVersion);
        if (manifest.MaximumAppVersion.Length > 0 && ParseThreePartVersion(manifest.MaximumAppVersion) < minimum)
            throw new InvalidDataException("A versão máxima do aplicativo não pode ser menor que a mínima.");
        if (manifest.CreatedAtUtc == DateTimeOffset.MinValue || manifest.CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("A data de criação do manifesto precisa estar em UTC.");
    }

    private static void ValidateArtifact(LearningPackageArtifact artifact)
    {
        if (!LearningCatalogIds.IsValid(artifact.Id))
            throw new InvalidDataException("O identificador do pacote no manifesto é inválido.");
        _ = ParseThreePartVersion(artifact.Version);
        if (artifact.Sha256 is not { Length: 64 } || artifact.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
            artifact.Sha256 != artifact.Sha256.ToLowerInvariant())
        {
            throw new InvalidDataException("O SHA-256 do pacote pedagógico é inválido.");
        }
        if (artifact.Bytes is < 1 or > LearningContentPackageFormat.MaximumPackageBytes)
            throw new InvalidDataException("O tamanho declarado do pacote pedagógico é inválido.");
    }

    private static Version ParseThreePartVersion(string? value)
    {
        if (!SemanticVersion.IsMatch(value ?? "") || !Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new InvalidDataException("A versão precisa usar o formato maior.menor.revisão.");
        return version;
    }

    private static Version ParseThreePartVersion(Version value) =>
        ParseThreePartVersion(ToThreePartVersion(value));

    private static string ToThreePartVersion(Version version)
    {
        if (version.Major < 0 || version.Minor < 0 || version.Build < 0 || version.Revision >= 0)
            throw new ArgumentException("A versão do aplicativo precisa ter exatamente três partes.", nameof(version));
        return $"{version.Major}.{version.Minor}.{version.Build}";
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
                    throw new InvalidDataException($"O manifesto pedagógico contém a propriedade duplicada '{property.Name}'.");
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
