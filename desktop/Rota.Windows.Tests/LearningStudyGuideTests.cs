using Rota.Desktop;

namespace Rota.Desktop.Tests;

public static class LearningStudyGuideTests
{
    public static readonly (string Name, Action Body)[] Cases = { ("Learning study guide links theory questions and prerequisites", LinksLearningResources) };

    private static void LinksLearningResources()
    {
        var package = Package();
        var guide = LearningStudyGuideService.Create(package, "conteudo-2");
        if (guide.Prerequisites.Single().Id != "conteudo-1" || guide.TheoryMaterial is null || guide.PracticeQuestions.Single().Id != "questao-1")
            throw new InvalidOperationException("study guide did not compose package resources");
    }

    private static LearningContentPackage Package() => new()
    {
        Package = new LearningPackageIdentity { Id = "matematica", Version = "1.0.0", Title = "Matemática", Locale = "pt-BR" },
        Catalog = new LearningCatalog { Subjects = new() { new() { Id = "mat", Name = "Matemática" } }, Courses = new() { new() { Id = "curso", SubjectId = "mat", Name = "Curso", Position = 1 } }, Modules = new() { new() { Id = "modulo", CourseId = "curso", Name = "Módulo", Position = 1 } }, Skills = new() { new() { Id = "skill", SubjectId = "mat", Name = "Habilidade" } }, Lessons = new() { new() { Id = "aula", ModuleId = "modulo", Name = "Aula", Position = 1, SkillIds = new() { "skill" } } }, Contents = new() { new() { Id = "conteudo-1", LessonId = "aula", Title = "Base", Position = 1 }, new() { Id = "conteudo-2", LessonId = "aula", Title = "Avançado", Position = 2 } }, Prerequisites = new() { new() { ContentId = "conteudo-2", RequiredContentId = "conteudo-1" } } },
        TheoryMaterials = new() { new() { Id = "teoria-2", ContentId = "conteudo-2", Title = "Teoria", LearningGoal = "Entender o conteúdo.", Sections = new() { new() { Kind = LearningTheorySectionKinds.Explanation, Title = "Explicação", Body = "Texto explicativo.", Position = 1 } } } },
        PracticeQuestions = new() { new() { Id = "questao-1", ContentId = "conteudo-2", Prompt = "Questão?", Options = new() { new() { Id = "a", Text = "A" }, new() { Id = "b", Text = "B" } }, CorrectOptionId = "a", Explanation = "Explicação.", Difficulty = LearningPracticeDifficulties.Basic } }
    };
}
