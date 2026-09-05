using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rota.Desktop.LocalAI;

public sealed record EnemCatalogContent(string Id, string Name);

public sealed record EnemCatalogSubject(
    string Id,
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<EnemCatalogContent> Contents);

public sealed record EnemCatalogArea(
    string Id,
    string Name,
    IReadOnlyList<EnemCatalogSubject> Subjects);

public sealed class EnemCatalogService : IAiEnemCatalogProvider
{
    public const string CurrentCatalogVersion = "inep-enem-2026-r1";
    public const string CurrentOfficialSourceUrl =
        "https://www.gov.br/inep/pt-br/centrais-de-conteudo/acervo-linha-editorial/publicacoes-institucionais/avaliacoes-e-exames-da-educacao-basica/matrizes-de-referencia-enem";
    private const string OfficialBasis =
        "Matriz de Referência do Enem (Inep, publicação institucional de 2026); matérias são uma curadoria do Rota sobre os objetos de conhecimento oficiais.";
    private static readonly Regex EnemWord = new(@"(^|[^a-z0-9])enem([^a-z0-9]|$)", RegexOptions.Compiled);

    public static EnemCatalogService Default { get; } = new(CreateAreas(), CreateWriting());

    private readonly IReadOnlyList<EnemCatalogArea> _areas;
    private readonly EnemCatalogSubject _writing;

    public EnemCatalogService(
        IReadOnlyList<EnemCatalogArea> areas,
        EnemCatalogSubject writing)
    {
        _areas = areas ?? throw new ArgumentNullException(nameof(areas));
        _writing = writing ?? throw new ArgumentNullException(nameof(writing));
        ValidateCatalog(_areas, _writing);
    }

    public string CatalogVersion => CurrentCatalogVersion;
    public string OfficialSourceUrl => CurrentOfficialSourceUrl;
    public IReadOnlyList<EnemCatalogArea> Areas => _areas;
    public EnemCatalogSubject Writing => _writing;

    public AiEnemCatalogContext? CreateContext(AiAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        AiContractValidator.ValidateInput(input);
        var requestText = string.Join(' ', new[]
        {
            input.ObjectiveOrExam,
            input.Goal,
            input.Notes,
            input.FreeText,
            string.Join(' ', input.StrongSubjects),
            string.Join(' ', input.WeakSubjects)
        });
        if (!EnemWord.IsMatch(Normalize(requestText))) return null;

        var weak = MatchSubjects(input.WeakSubjects);
        var strong = MatchSubjects(input.StrongSubjects);
        var mentioned = MatchSubjects(new[] { requestText });

        var context = new AiEnemCatalogContext
        {
            CatalogVersion = CurrentCatalogVersion,
            Basis = OfficialBasis,
            Areas = _areas.Select(area => new AiEnemAreaContext
            {
                Id = area.Id,
                Name = area.Name,
                Subjects = area.Subjects
                    .OrderBy(subject => EmphasisRank(subject.Id, weak, strong, mentioned))
                    .ThenBy(subject => subject.Name, StringComparer.Ordinal)
                    .Select(subject => ToContext(subject, Emphasis(subject.Id, weak, strong, mentioned)))
                    .ToList()
            }).ToList(),
            Writing = ToContext(_writing, Emphasis(_writing.Id, weak, strong, mentioned))
        };
        AiContractValidator.ValidateEnemCatalogContext(context);
        return context;
    }

    private HashSet<string> MatchSubjects(IEnumerable<string> values)
    {
        var text = Normalize(string.Join(' ', values));
        var matches = new HashSet<string>(StringComparer.Ordinal);
        if (text.Length == 0) return matches;
        foreach (var subject in _areas.SelectMany(area => area.Subjects).Append(_writing))
        {
            if (subject.Aliases.Append(subject.Name).Any(alias => ContainsTerm(text, Normalize(alias))))
                matches.Add(subject.Id);
        }
        return matches;
    }

    private static AiEnemSubjectContext ToContext(EnemCatalogSubject subject, string emphasis) => new()
    {
        Id = subject.Id,
        Name = subject.Name,
        UserEmphasis = emphasis,
        Contents = subject.Contents.Select(content => new AiEnemContentContext
        {
            Id = content.Id,
            Name = content.Name
        }).ToList()
    };

    private static string Emphasis(
        string subjectId,
        HashSet<string> weak,
        HashSet<string> strong,
        HashSet<string> mentioned) =>
        weak.Contains(subjectId) ? "weak" :
        strong.Contains(subjectId) ? "strong" :
        mentioned.Contains(subjectId) ? "mentioned" : "coverage";

    private static int EmphasisRank(
        string subjectId,
        HashSet<string> weak,
        HashSet<string> strong,
        HashSet<string> mentioned) =>
        Emphasis(subjectId, weak, strong, mentioned) switch
        {
            "weak" => 0,
            "mentioned" => 1,
            "strong" => 2,
            _ => 3
        };

    private static bool ContainsTerm(string text, string term) =>
        term.Length > 1 && $" {text} ".Contains($" {term} ", StringComparison.Ordinal);

    internal static string Normalize(string value)
    {
        var decomposed = (value ?? "").Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            var normalized = char.ToLowerInvariant(character);
            if (char.IsLetterOrDigit(normalized))
            {
                builder.Append(normalized);
                previousSpace = false;
            }
            else if (!previousSpace && builder.Length > 0)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static void ValidateCatalog(
        IReadOnlyList<EnemCatalogArea> areas,
        EnemCatalogSubject writing)
    {
        if (areas.Count != 4)
            throw new AiContractValidationException("O catálogo ENEM precisa conter exatamente as quatro áreas objetivas oficiais.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in areas)
        {
            ValidateIdentifier(area.Id, "área");
            ValidateName(area.Name, "área");
            if (!ids.Add(area.Id) || !names.Add(area.Name) || area.Subjects.Count == 0)
                throw new AiContractValidationException("O catálogo ENEM contém área duplicada ou vazia.");
            foreach (var subject in area.Subjects) ValidateSubject(subject, ids, names);
        }
        ValidateSubject(writing, ids, names);
        if (writing.Id != "redacao")
            throw new AiContractValidationException("A redação precisa permanecer separada das quatro áreas objetivas.");
    }

    private static void ValidateSubject(
        EnemCatalogSubject subject,
        HashSet<string> ids,
        HashSet<string> names)
    {
        ValidateIdentifier(subject.Id, "matéria");
        ValidateName(subject.Name, "matéria");
        if (!ids.Add(subject.Id) || !names.Add(subject.Name) || subject.Contents.Count is < 3 or > 5)
            throw new AiContractValidationException("O catálogo ENEM contém matéria duplicada ou com cobertura inválida.");
        if (subject.Aliases.Count == 0 || subject.Aliases.Any(string.IsNullOrWhiteSpace))
            throw new AiContractValidationException("Toda matéria do catálogo ENEM precisa de aliases válidos.");
        foreach (var content in subject.Contents)
        {
            ValidateIdentifier(content.Id, "conteúdo");
            ValidateName(content.Name, "conteúdo");
            if (!ids.Add(content.Id))
                throw new AiContractValidationException("O catálogo ENEM contém identificadores duplicados.");
        }
    }

    private static void ValidateIdentifier(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(character =>
                !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new AiContractValidationException($"O identificador de {field} do catálogo ENEM é inválido.");
    }

    private static void ValidateName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl))
            throw new AiContractValidationException($"O nome de {field} do catálogo ENEM é inválido.");
    }

    private static IReadOnlyList<EnemCatalogArea> CreateAreas() => new[]
    {
        new EnemCatalogArea("linguagens", "Linguagens, Códigos e suas Tecnologias", new[]
        {
            Subject("lingua-portuguesa", "Língua Portuguesa", new[] { "português", "portugues", "gramática", "gramatica" },
                Content("lp-generos", "Gêneros textuais e sequências discursivas"),
                Content("lp-linguistica", "Aspectos linguísticos e relações lógico-semânticas"),
                Content("lp-argumentacao", "Argumentação, progressão e organização textual"),
                Content("lp-variacao", "Variação linguística e usos sociais da língua")),
            Subject("literatura", "Literatura", new[] { "literatura", "texto literário", "texto literario" },
                Content("lit-formacao", "Literatura, formação nacional e processo social"),
                Content("lit-generos", "Gêneros literários e construção do texto"),
                Content("lit-patrimonio", "Patrimônio literário, artes e outros saberes")),
            Subject("lingua-estrangeira", "Língua Estrangeira", new[] { "inglês", "ingles", "espanhol", "língua estrangeira", "lingua estrangeira" },
                Content("le-compreensao", "Compreensão de textos em língua estrangeira"),
                Content("le-vocabulario", "Vocabulário, contexto e função comunicativa"),
                Content("le-cultura", "Produção cultural e diversidade linguística")),
            Subject("artes", "Artes", new[] { "arte", "artes", "música", "musica", "teatro", "dança", "danca" },
                Content("art-linguagens", "Linguagens das artes visuais, música, teatro e dança"),
                Content("art-contexto", "Produção artística, contexto e recepção"),
                Content("art-diversidade", "Diversidade, identidade e patrimônio cultural")),
            Subject("educacao-fisica", "Educação Física", new[] { "educação física", "educacao fisica", "esporte", "práticas corporais", "praticas corporais" },
                Content("ef-corpo", "Corpo, identidade e expressão cultural"),
                Content("ef-saude", "Exercício físico, saúde e autonomia"),
                Content("ef-praticas", "Esporte, dança, lutas, jogos e brincadeiras")),
            Subject("tecnologias-comunicacao", "Tecnologias da Comunicação", new[] { "tecnologia", "comunicação", "comunicacao", "mídia", "midia", "tic" },
                Content("tic-generos-digitais", "Gêneros digitais e sistemas de informação"),
                Content("tic-impactos", "Impactos sociais das tecnologias da comunicação"),
                Content("tic-uso-critico", "Uso crítico das mídias e linguagens"))
        }),
        new EnemCatalogArea("ciencias-humanas", "Ciências Humanas e suas Tecnologias", new[]
        {
            Subject("historia", "História", new[] { "história", "historia" },
                Content("hist-cultura", "Diversidade cultural, conflitos e vida em sociedade"),
                Content("hist-estado", "Estado, cidadania, democracia e movimentos sociais"),
                Content("hist-trabalho", "Trabalho, economia e transformações sociais"),
                Content("hist-brasil", "Formação histórica e territorial do Brasil")),
            Subject("geografia", "Geografia", new[] { "geografia", "geopolítica", "geopolitica", "cartografia" },
                Content("geo-cartografia", "Representações cartográficas e organização do espaço"),
                Content("geo-populacao", "População, território e fluxos migratórios"),
                Content("geo-producao", "Produção, urbanização e redes geográficas"),
                Content("geo-ambiente", "Sociedade, natureza e questões socioambientais")),
            Subject("filosofia", "Filosofia", new[] { "filosofia", "ética", "etica" },
                Content("fil-pensamento", "Pensamento político e formação da cidadania"),
                Content("fil-etica", "Ética, justiça e direitos humanos"),
                Content("fil-conhecimento", "Conhecimento, argumentação e crítica")),
            Subject("sociologia", "Sociologia", new[] { "sociologia", "sociedade", "movimentos sociais" },
                Content("soc-cultura", "Cultura, identidade e diversidade social"),
                Content("soc-trabalho", "Trabalho, produção e desigualdades"),
                Content("soc-politica", "Poder, Estado, cidadania e movimentos sociais"))
        }),
        new EnemCatalogArea("ciencias-natureza", "Ciências da Natureza e suas Tecnologias", new[]
        {
            Subject("fisica", "Física", new[] { "física", "fisica" },
                Content("fis-mecanica", "Movimento, equilíbrio e leis físicas"),
                Content("fis-energia", "Energia, trabalho, potência e termodinâmica"),
                Content("fis-eletromagnetismo", "Fenômenos elétricos e magnéticos"),
                Content("fis-ondas", "Oscilações, ondas, óptica e radiação")),
            Subject("quimica", "Química", new[] { "química", "quimica" },
                Content("qui-materiais", "Materiais, propriedades e transformações químicas"),
                Content("qui-calculos", "Estequiometria, soluções e cálculos químicos"),
                Content("qui-energia", "Energia, eletroquímica e termoquímica"),
                Content("qui-ambiente", "Química ambiental, recursos e tecnologias")),
            Subject("biologia", "Biologia", new[] { "biologia", "ecologia", "genética", "genetica" },
                Content("bio-celula", "Células, metabolismo e organização dos seres vivos"),
                Content("bio-genetica", "Hereditariedade, genética e biotecnologia"),
                Content("bio-evolucao", "Identidade, evolução e diversidade da vida"),
                Content("bio-ecologia", "Ecologia, ambiente e saúde humana"))
        }),
        new EnemCatalogArea("area-matematica", "Matemática e suas Tecnologias", new[]
        {
            Subject("matematica", "Matemática", new[] { "matemática", "matematica" },
                Content("mat-numeros", "Números, operações, razão e proporcionalidade"),
                Content("mat-geometria", "Geometria plana, espacial e analítica"),
                Content("mat-grandezas", "Grandezas, medidas, escalas e unidades"),
                Content("mat-algebra", "Álgebra, funções, equações e gráficos"),
                Content("mat-estatistica", "Estatística, probabilidade e análise de dados"))
        })
    };

    private static EnemCatalogSubject CreateWriting() =>
        Subject("redacao", "Redação", new[] { "redação", "redacao", "texto dissertativo", "dissertação", "dissertacao" },
            Content("red-formal", "Domínio da escrita formal da língua portuguesa"),
            Content("red-tema", "Compreensão do tema e texto dissertativo-argumentativo"),
            Content("red-argumentos", "Seleção e organização de argumentos"),
            Content("red-coesao", "Coesão e mecanismos linguísticos da argumentação"),
            Content("red-intervencao", "Proposta de intervenção e direitos humanos"));

    private static EnemCatalogSubject Subject(
        string id,
        string name,
        IReadOnlyList<string> aliases,
        params EnemCatalogContent[] contents) => new(id, name, aliases, contents);

    private static EnemCatalogContent Content(string id, string name) => new(id, name);
}

public static class EnemProposalValidator
{
    public static void Validate(AiProposal proposal, AiEnemCatalogContext context)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        AiContractValidator.ValidateEnemCatalogContext(context);
        var subjects = context.Areas.SelectMany(area => area.Subjects).Append(context.Writing)
            .ToDictionary(subject => EnemCatalogService.Normalize(subject.Name), StringComparer.Ordinal);

        if (proposal.Kind == AiProposalKind.StudyPlan)
        {
            PlanPackage package;
            try
            {
                package = StudyPlanImporter.Parse(proposal.StudyPlan?.StudyPlanJson);
            }
            catch (ArgumentException ex)
            {
                throw new AiContractValidationException("O plano ENEM não contém um StudyPlan válido.", ex);
            }
            if (!$" {EnemCatalogService.Normalize(package.ObjectiveName)} ".Contains(" enem ", StringComparison.Ordinal))
                throw new AiContractValidationException("O objetivo do plano gerado não corresponde ao ENEM.");
            foreach (var session in package.Sessions)
            {
                var subjectKey = EnemCatalogService.Normalize(session.Subject);
                if (!subjects.TryGetValue(subjectKey, out var subject))
                    throw new AiContractValidationException($"A matéria '{session.Subject}' não pertence ao catálogo ENEM enviado ao modelo.");
                var topicKey = EnemCatalogService.Normalize(session.Topic);
                if (!subject.Contents.Any(content => EnemCatalogService.Normalize(content.Name) == topicKey))
                    throw new AiContractValidationException($"O conteúdo '{session.Topic}' não pertence à matéria '{subject.Name}' no catálogo ENEM enviado ao modelo.");
            }
            return;
        }

        foreach (var operation in proposal.Changes?.Operations ?? new List<AiPlanOperation>())
        {
            if (operation.Type is not (AiPlanOperationType.AddSession or AiPlanOperationType.ChangeSubjectPriority)) continue;
            if (!subjects.ContainsKey(EnemCatalogService.Normalize(operation.Subject)))
                throw new AiContractValidationException($"A matéria '{operation.Subject}' não pertence ao catálogo ENEM enviado ao modelo.");
        }
    }
}
