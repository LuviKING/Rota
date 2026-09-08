using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherStylePreferenceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 21, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher style preference is an explicit local subject choice", SavesExplicitLocalChoice),
        ("Teacher style preferences stay isolated by package and subject", KeepsSubjectsIsolated),
        ("Teacher style preference store recovers a valid backup", RecoversBackup),
        ("Teacher style preferences reject unsupported styles", RejectsUnsupportedStyle)
    };

    private static void SavesExplicitLocalChoice()
    {
        WithStore((store, _) =>
        {
            var subject = Subject("matematica");
            var service = new AiTeacherStylePreferenceService(store, () => Timestamp);
            Require(service.GetAsync(subject).GetAwaiter().GetResult() is null);

            var selected = service.SetAsync(subject, AiTeacherExplanationStyle.Visual).GetAwaiter().GetResult();
            Require(selected == AiTeacherExplanationStyle.Visual);
            Require(service.GetAsync(subject).GetAwaiter().GetResult() == AiTeacherExplanationStyle.Visual);

            var loaded = store.TryLoadAsync("pacote-matematica", "matematica").GetAwaiter().GetResult();
            Require(loaded is not null);
            Require(loaded!.Style == AiTeacherExplanationStyle.Visual);
            Require(loaded.UpdatedAtUtc == Timestamp);
            Require(loaded.PackageId == "pacote-matematica" && loaded.SubjectId == "matematica");
            Require(AiTeacherSubjectStylePreferenceFactory.Matches(loaded, subject));
        });
    }

    private static void KeepsSubjectsIsolated()
    {
        WithStore((store, _) =>
        {
            var service = new AiTeacherStylePreferenceService(store, () => Timestamp);
            var math = Subject("matematica");
            var physics = Subject("fisica", "Física");
            service.SetAsync(math, AiTeacherExplanationStyle.Simple).GetAwaiter().GetResult();
            service.SetAsync(physics, AiTeacherExplanationStyle.Detailed).GetAwaiter().GetResult();

            Require(service.GetAsync(math).GetAwaiter().GetResult() == AiTeacherExplanationStyle.Simple);
            Require(service.GetAsync(physics).GetAwaiter().GetResult() == AiTeacherExplanationStyle.Detailed);
            Require(store.TryLoadAsync("outro-pacote", "matematica").GetAwaiter().GetResult() is null);
        });
    }

    private static void RecoversBackup()
    {
        WithStore((store, directory) =>
        {
            var subject = Subject("matematica");
            var first = AiTeacherSubjectStylePreferenceFactory.Create(subject, AiTeacherExplanationStyle.Simple, Timestamp);
            var second = AiTeacherSubjectStylePreferenceFactory.Create(subject, AiTeacherExplanationStyle.Visual, Timestamp.AddMinutes(1));
            store.SaveAsync(first).GetAwaiter().GetResult();
            store.SaveAsync(second).GetAwaiter().GetResult();

            var path = Path.Combine(directory, "pacote-matematica--matematica.json");
            Require(File.Exists(path + ".bak"));
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"SchemaVersion\":1}");
            var recovered = store.TryLoadAsync("pacote-matematica", "matematica").GetAwaiter().GetResult();
            Require(recovered is not null && recovered.Style == AiTeacherExplanationStyle.Simple);
            Require(Directory.EnumerateFiles(directory).Any(item => Path.GetFileName(item)
                .StartsWith("pacote-matematica--matematica.json.corrupt-", StringComparison.Ordinal)));
        });
    }

    private static void RejectsUnsupportedStyle()
    {
        WithStore((store, _) =>
        {
            var subject = Subject("matematica");
            var service = new AiTeacherStylePreferenceService(store, () => Timestamp);
            Expect<AiContractValidationException>(() => service.SetAsync(subject, (AiTeacherExplanationStyle)99)
                .GetAwaiter().GetResult());
            Expect<AiContractValidationException>(() => store.TryLoadAsync("invalido!", "matematica")
                .GetAwaiter().GetResult());
        });
    }

    private static AiTeacherSubjectBinding Subject(string subjectId, string subjectName = "Matemática") => new()
    {
        PackageId = "pacote-matematica",
        PackageVersion = "1.0.0",
        SubjectId = subjectId,
        SubjectName = subjectName,
        ContentId = "razao",
        ContentTitle = "Razão"
    };

    private static void WithStore(Action<AiTeacherStylePreferenceStore, string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RotaTeacherStylePreferenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new AiTeacherStylePreferenceStore(directory, () => Timestamp);
            body(store, directory);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher style preference assertion failed.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
