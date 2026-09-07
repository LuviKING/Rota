namespace Rota.Desktop;

/// <summary>Compõe uma sessão de aprendizagem determinística para um conteúdo.</summary>
public static class LearningStudyGuideService
{
    public static LearningStudyGuide Create(LearningContentPackage package, string contentId)
    {
        ArgumentNullException.ThrowIfNull(package);
        LearningContentPackageFormat.Validate(package);
        var content = package.Catalog.Contents.SingleOrDefault(item => item.Id == contentId)
            ?? throw new InvalidDataException("O conteúdo solicitado não pertence ao pacote pedagógico.");
        var prerequisites = package.Catalog.Prerequisites
            .Where(item => item.ContentId == contentId)
            .Select(item => package.Catalog.Contents.Single(required => required.Id == item.RequiredContentId).Copy())
            .OrderBy(item => item.Position)
            .ToList();
        var material = package.TheoryMaterials.SingleOrDefault(item => item.ContentId == contentId)?.Copy();
        var questions = package.PracticeQuestions.Where(item => item.ContentId == contentId).Select(item => item.Copy()).ToList();
        return new LearningStudyGuide(content.Copy(), prerequisites, material, questions);
    }
}

public sealed record LearningStudyGuide(
    LearningContent Content,
    IReadOnlyList<LearningContent> Prerequisites,
    LearningTheoryMaterial? TheoryMaterial,
    IReadOnlyList<LearningPracticeQuestion> PracticeQuestions);
