using Rota.Desktop;
using System.Security.Cryptography;
using System.Text;

namespace Rota.Desktop.Tests;

public static class LearningPackageInstallerTests
{
    public static readonly (string Name, Action Body)[] Cases =
    {
        ("Learning package installer verifies and atomically installs a package", InstallsVerifiedPackage),
        ("Learning package installer rejects altered bytes without partial files", RejectsAlteredBytes),
        ("Learning package installer rejects duplicate package versions", RejectsDuplicateVersion),
        ("Learning package installer upgrades to a newer version and preserves progress", UpgradesAndPreservesProgress),
        ("Learning package installer blocks package downgrades", RejectsDowngrade),
        ("Learning package installer blocks author takeover during update", RejectsAuthorTakeover),
        ("Learning package installer preserves stable content identities", RejectsStableContentRemoval),
        ("Learning package installer keeps the previous version active after a failed update", FailedUpdateKeepsPreviousActive),
        ("Learning package installer preserves the exact signed package bytes", PreservesExactSignedBytes),
        ("Learning package install API cannot bypass update continuity", InstallCannotBypassUpdatePolicy)
    };

    private static void InstallsVerifiedPackage()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var package = Package("1.0.0");
            var packageBytes = LearningContentPackageFormat.Serialize(package);
            var manifestBytes = ManifestBytes(package);

            var installed = installer.Install(packageBytes, manifestBytes);
            Require(File.Exists(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.PackageFileName)), "package file missing");
            Require(File.Exists(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.ManifestFileName)), "manifest file missing");
            Require(installed.AuthorName == "Equipe Rota", "author identity missing");
            Require(!Directory.EnumerateDirectories(root, ".install-*", SearchOption.TopDirectoryOnly).Any(), "staging directory leaked");
        });
    }

    private static void RejectsAlteredBytes()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var package = Package("1.0.0");
            var packageBytes = LearningContentPackageFormat.Serialize(package);
            var manifestBytes = ManifestBytes(package);
            packageBytes[^1] ^= 1;

            Throws(() => installer.Install(packageBytes, manifestBytes), "SHA-256");
            Require(!Directory.Exists(Path.Combine(root, "packages")), "invalid package was installed");
            Require(!Directory.EnumerateDirectories(root, ".install-*", SearchOption.TopDirectoryOnly).Any(), "invalid install leaked staging");
        });
    }

    private static void RejectsDuplicateVersion()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var package = Package("1.0.0");
            var packageBytes = LearningContentPackageFormat.Serialize(package);
            var manifestBytes = ManifestBytes(package);
            _ = installer.Install(packageBytes, manifestBytes);

            Throws(() => installer.InstallOrUpdate(packageBytes, manifestBytes), "já está instalada");
        });
    }

    private static void UpgradesAndPreservesProgress()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var original = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(original), ManifestBytes(original));

            var progress = new LearningProgressStore(Path.Combine(root, "progress.json"));
            progress.MarkContentCompleted("mat-conteudo");

            var update = Package("1.1.0", includeExtraContent: true);
            var updateBytes = LearningContentPackageFormat.Serialize(update);
            var updateManifest = ManifestBytes(update);
            var preview = installer.Inspect(updateBytes, updateManifest);
            Require(preview.Kind == LearningPackageChangeKind.Update, "update was not identified as an update");
            Require(preview.PreviousVersion == "1.0.0" && preview.NewVersion == "1.1.0", "update versions were not reported correctly");

            var result = installer.InstallOrUpdate(updateBytes, updateManifest);
            Require(result.Change.Kind == LearningPackageChangeKind.Update, "update result kind is incorrect");
            Require(Directory.Exists(Path.Combine(root, "packages", "enem-matematica", "1.0.0")), "previous version was removed");
            Require(Directory.Exists(Path.Combine(root, "packages", "enem-matematica", "1.1.0")), "new version was not activated");
            Require(progress.Snapshot().CompletedContentIds.Contains("mat-conteudo"), "learning progress changed during package update");

            var active = new LearningContentLibrary(root, new Version(0, 5, 0)).Load().Packages.Single();
            Require(active.Package.Package.Version == "1.1.0", "newest version did not become active");
            Require(active.Package.Catalog.Contents.Any(item => item.Id == "mat-conteudo-extra"), "new content was not exposed");
            Require(!Directory.EnumerateDirectories(root, ".install-*", SearchOption.TopDirectoryOnly).Any(), "update staging directory leaked");
        });
    }

    private static void RejectsDowngrade()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var current = Package("2.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(current), ManifestBytes(current));
            var older = Package("1.9.0");

            Throws(() => installer.InstallOrUpdate(LearningContentPackageFormat.Serialize(older), ManifestBytes(older)), "downgrade");
            Require(new LearningContentLibrary(root, new Version(0, 5, 0)).Load().Packages.Single().Package.Package.Version == "2.0.0", "downgrade changed the active version");
        });
    }

    private static void RejectsAuthorTakeover()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var current = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(current), ManifestBytes(current));
            var update = Package("1.1.0");

            Throws(() => installer.InstallOrUpdate(
                LearningContentPackageFormat.Serialize(update),
                ManifestBytes(update, "outra-equipe", "Outra Equipe")), "autor");
            Require(!Directory.Exists(Path.Combine(root, "packages", "enem-matematica", "1.1.0")), "untrusted author update was activated");
        });
    }

    private static void RejectsStableContentRemoval()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var current = Package("1.0.0", includeExtraContent: true);
            _ = installer.Install(LearningContentPackageFormat.Serialize(current), ManifestBytes(current));
            var update = Package("1.1.0", includeExtraContent: false);

            Throws(() => installer.InstallOrUpdate(LearningContentPackageFormat.Serialize(update), ManifestBytes(update)), "removeria um conteúdo");
            Require(new LearningContentLibrary(root, new Version(0, 5, 0)).Load().Packages.Single().Package.Package.Version == "1.0.0", "identity-breaking update changed the active package");
        });
    }

    private static void FailedUpdateKeepsPreviousActive()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var current = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(current), ManifestBytes(current));

            var update = Package("1.1.0");
            var updateBytes = LearningContentPackageFormat.Serialize(update);
            var updateManifest = ManifestBytes(update);
            updateBytes[^1] ^= 1;
            Throws(() => installer.InstallOrUpdate(updateBytes, updateManifest), "SHA-256");

            var snapshot = new LearningContentLibrary(root, new Version(0, 5, 0)).Load();
            Require(snapshot.Packages.Single().Package.Package.Version == "1.0.0", "failed update displaced the previous package");
            Require(!Directory.Exists(Path.Combine(root, "packages", "enem-matematica", "1.1.0")), "failed update left an activated directory");
            Require(!Directory.EnumerateDirectories(root, ".install-*", SearchOption.TopDirectoryOnly).Any(), "failed update leaked staging");
        });
    }

    private static void PreservesExactSignedBytes()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var package = Package("1.0.0");
            var canonical = LearningContentPackageFormat.Serialize(package);
            var signedBytes = Encoding.UTF8.GetBytes("\n  " + Encoding.UTF8.GetString(canonical) + "\n");
            var manifest = LearningPackageManifestFormat.Create(
                package,
                new LearningPackageAuthor { Id = "rota-team", Name = "Equipe Rota" },
                LearningEducationLevels.Enem,
                new Version(0, 5, 0));
            manifest.Package.Bytes = signedBytes.Length;
            manifest.Package.Sha256 = Convert.ToHexString(SHA256.HashData(signedBytes)).ToLowerInvariant();
            var manifestBytes = LearningPackageManifestFormat.Serialize(manifest);

            var installed = installer.Install(signedBytes, manifestBytes);
            var persisted = File.ReadAllBytes(Path.Combine(installed.DirectoryPath, LearningPackageInstaller.PackageFileName));
            Require(persisted.SequenceEqual(signedBytes), "installer rewrote the exact signed package bytes");
        });
    }

    private static void InstallCannotBypassUpdatePolicy()
    {
        WithRoot(root =>
        {
            var installer = new LearningPackageInstaller(root, new Version(0, 5, 0));
            var current = Package("1.0.0");
            _ = installer.Install(LearningContentPackageFormat.Serialize(current), ManifestBytes(current));
            var update = Package("1.1.0");

            Throws(() => installer.Install(LearningContentPackageFormat.Serialize(update), ManifestBytes(update)), "atualização de pacote");
            Require(!Directory.Exists(Path.Combine(root, "packages", "enem-matematica", "1.1.0")), "install API bypassed update policy");
        });
    }

    private static byte[] ManifestBytes(LearningContentPackage package, string authorId = "rota-team", string authorName = "Equipe Rota")
    {
        var manifest = LearningPackageManifestFormat.Create(
            package,
            new LearningPackageAuthor { Id = authorId, Name = authorName },
            LearningEducationLevels.Enem,
            new Version(0, 5, 0));
        return LearningPackageManifestFormat.Serialize(manifest);
    }

    private static LearningContentPackage Package(string version, bool includeExtraContent = false)
    {
        var package = new LearningContentPackage
        {
            Package = new LearningPackageIdentity { Id = "enem-matematica", Version = version, Title = "Matemática", Locale = "pt-BR" },
            Catalog = new LearningCatalog
            {
                Subjects = new() { new() { Id = "matematica", Name = "Matemática" } },
                Courses = new() { new() { Id = "mat-basico", SubjectId = "matematica", Name = "Básico", Position = 1 } },
                Modules = new() { new() { Id = "mat-equacoes", CourseId = "mat-basico", Name = "Equações", Position = 1 } },
                Skills = new() { new() { Id = "mat-skill", SubjectId = "matematica", Name = "Equações simples" } },
                Lessons = new() { new() { Id = "mat-aula", ModuleId = "mat-equacoes", Name = "Aula", Position = 1, SkillIds = new() { "mat-skill" } } },
                Contents = new() { new() { Id = "mat-conteudo", LessonId = "mat-aula", Title = "Conteúdo", Position = 1 } }
            }
        };
        if (includeExtraContent)
            package.Catalog.Contents.Add(new LearningContent { Id = "mat-conteudo-extra", LessonId = "mat-aula", Title = "Conteúdo extra", Position = 2 });
        return package;
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "rota-package-installer-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            action(root);
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void Throws(Action action, string fragment)
    {
        try { action(); }
        catch (InvalidDataException exception) when (exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected InvalidDataException containing '{fragment}'.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
