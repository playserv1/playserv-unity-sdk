using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Playserv.Editor.Migration;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServApiMigrationTests
    {
        [Test]
        public void AnalyzeSource_FindsModuleApisWithoutChangingCommentsStringsOrOtherTypes()
        {
            const string source = @"
using Playserv.Wrapper;

internal sealed class Example
{
    public void Run()
    {
        PlayServ.Invoke(""Rooms"", ""Join"", new object());
        var stream = PlayServ.Subscribe<string>();
        var spawned = PlayServ.Spawn(""Tank"", default);
        var scope = PlayServ.CurrentSpawnScope;
        Other.PlayServ.Invoke(""Unrelated"", ""Call"", null);
        var text = ""PlayServ.Invoke should remain text"";
        // PlayServ.Spawn should remain a comment.
    }
}
";

            var changes = PlayServApiMigrationEngine.AnalyzeSource(
                "Assets/Example.cs",
                source);

            Assert.That(changes.Count, Is.EqualTo(4));
            Assert.That(
                changes.Select(change => change.ReplacementExpression),
                Is.EquivalentTo(new[]
                {
                    "PlayServRpc.Invoke",
                    "PlayServEvents.Subscribe<string>",
                    "PlayServSpawn.Spawn",
                    "PlayServSpawn.CurrentScope"
                }));
        }

        [Test]
        public void AnalyzeSource_HandlesLegacyAliasAndFullyQualifiedType()
        {
            const string source = @"
using LegacySdk = global::Playserv.Wrapper.PlayServ;

internal sealed class Example
{
    public void Run()
    {
        LegacySdk.Invoke(""Rooms"", ""Join"", new object());
        var stream = global::Playserv.Wrapper.PlayServ.Subscribe<int>();
    }
}
";

            var changes = PlayServApiMigrationEngine.AnalyzeSource(
                "Assets/Example.cs",
                source);

            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(
                changes[0].ReplacementExpression,
                Is.EqualTo("global::Playserv.Wrapper.PlayServRpc.Invoke"));
            Assert.That(
                changes[1].ReplacementExpression,
                Is.EqualTo("global::Playserv.Wrapper.PlayServEvents.Subscribe<int>"));
        }

        [Test]
        public void AnalyzeSource_SkipsAmbiguousLocalPlayServType()
        {
            const string source = @"
using Playserv.Wrapper;

internal static class PlayServ
{
    public static void Invoke() { }
}

internal sealed class Example
{
    public void Run() => PlayServ.Invoke();
}
";

            var changes = PlayServApiMigrationEngine.AnalyzeSource(
                "Assets/Example.cs",
                source);

            Assert.That(changes, Is.Empty);
        }

        [Test]
        public void ScanProject_SkipsGeneratedAndEmbeddedSdkScripts()
        {
            var projectRoot = CreateProjectRoot();
            var source = @"
using Playserv.Wrapper;
internal sealed class Example
{
    public void Run() => PlayServ.Invoke(""Rooms"", ""Join"", null);
}
";

            try
            {
                WriteUtf8(Path.Combine(projectRoot, "Assets", "Gameplay.cs"), source);
                WriteUtf8(
                    Path.Combine(
                        projectRoot,
                        "Assets",
                        "PlayServ",
                        "Generated",
                        "Legacy.cs"),
                    source);
                WriteUtf8(
                    Path.Combine(
                        projectRoot,
                        "Assets",
                        "Shared",
                        "Generated",
                        "Legacy.cs"),
                    source);
                WriteUtf8(
                    Path.Combine(
                        projectRoot,
                        "Assets",
                        "playserv-unity-sdk",
                        "Legacy.cs"),
                    source);

                var scan = PlayServApiMigrationEngine.ScanProject(projectRoot);

                Assert.That(scan.ScannedFileCount, Is.EqualTo(1));
                Assert.That(scan.ChangeCount, Is.EqualTo(1));
                Assert.That(scan.Files[0].AssetPath, Is.EqualTo("Assets/Gameplay.cs"));
            }
            finally
            {
                DeleteProjectRoot(projectRoot);
            }
        }

        [Test]
        public void Apply_UpdatesSelectedChangesAndCreatesBackupAndReport()
        {
            var projectRoot = CreateProjectRoot();
            var scriptPath = Path.Combine(projectRoot, "Assets", "Scripts", "Example.cs");
            const string source = @"
using Playserv.Wrapper;

internal sealed class Example
{
    public void Run()
    {
        PlayServ.Invoke(""Rooms"", ""Join"", new object());
        PlayServ.Spawn(""Tank"", default);
    }
}
";

            try
            {
                WriteUtf8(scriptPath, source);
                var scan = PlayServApiMigrationEngine.ScanProject(projectRoot);
                Assert.That(scan.ChangeCount, Is.EqualTo(2));

                var spawnChange = scan.Files[0].Changes.Single(
                    change => change.OriginalExpression.IndexOf(
                        "Spawn",
                        StringComparison.Ordinal) >= 0);
                spawnChange.IsSelected = false;

                var result = PlayServApiMigrationEngine.Apply(
                    scan,
                    new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));

                Assert.That(result.HasErrors, Is.False);
                Assert.That(result.AppliedChangeCount, Is.EqualTo(1));
                Assert.That(result.UpdatedFileCount, Is.EqualTo(1));
                StringAssert.Contains("PlayServRpc.Invoke", File.ReadAllText(scriptPath));
                StringAssert.Contains("PlayServ.Spawn", File.ReadAllText(scriptPath));
                Assert.That(File.Exists(result.Files[0].BackupPath), Is.True);
                Assert.That(File.ReadAllText(result.Files[0].BackupPath), Is.EqualTo(source));
                Assert.That(File.Exists(result.ReportPath), Is.True);
                StringAssert.Contains("Changes applied: 1", File.ReadAllText(result.ReportPath));
                StringAssert.Contains(
                    "`PlayServ.Invoke` -> `PlayServRpc.Invoke`",
                    File.ReadAllText(result.ReportPath));
            }
            finally
            {
                DeleteProjectRoot(projectRoot);
            }
        }

        [Test]
        public void Apply_SkipsFileChangedAfterPreview()
        {
            var projectRoot = CreateProjectRoot();
            var scriptPath = Path.Combine(projectRoot, "Assets", "Example.cs");
            const string source = @"
using Playserv.Wrapper;
internal sealed class Example
{
    public void Run() => PlayServ.Publish(""event"");
}
";

            try
            {
                WriteUtf8(scriptPath, source);
                var scan = PlayServApiMigrationEngine.ScanProject(projectRoot);
                Assert.That(scan.ChangeCount, Is.EqualTo(1));

                File.AppendAllText(scriptPath, Environment.NewLine + "// edited");
                var result = PlayServApiMigrationEngine.Apply(
                    scan,
                    new DateTime(2026, 7, 24, 10, 5, 0, DateTimeKind.Utc));

                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.AppliedChangeCount, Is.Zero);
                Assert.That(result.Files[0].Error, Does.Contain("changed after preview"));
                StringAssert.Contains("PlayServ.Publish", File.ReadAllText(scriptPath));
            }
            finally
            {
                DeleteProjectRoot(projectRoot);
            }
        }

        private static string CreateProjectRoot()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "PlayServApiMigrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            return root;
        }

        private static void WriteUtf8(string path, string contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                path,
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void DeleteProjectRoot(string projectRoot)
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }
}
