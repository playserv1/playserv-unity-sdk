using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Playserv.Editor.Tests
{
    public class PlatformFunctionPackageTests
    {
        private string _root;
        [SetUp] public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Temp", "playserv-functions-" + Guid.NewGuid().ToString("N"))).FullName;
        [TearDown] public void TearDown() => Directory.Delete(_root, true);
        private void Write(string path, string text)
        {
            var absolute = Path.Combine(_root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, text);
        }

        [TestCase("IPlatformFunction", "cloud_function")]
        [TestCase("PlatformGameServer", "game_server")]
        public void DetectsHandlerFromBaseType(string type, string expected)
        {
            Write("Handler.cs", "class Handler : Some.Namespace." + type + " {}");
            Assert.That(PlatformFunctionPackage.Preview(_root).SuggestedKind, Is.EqualTo(expected));
        }

        [TestCase("global::IPlatformFunction")]
        [TestCase("Namespace./* type */ IPlatformFunction")]
        public void QualifiedHandlerSyntaxIgnoresTrivia(string baseType)
        {
            Write("Handler.cs", "class Handler : " + baseType + " {}");
            Assert.That(PlatformFunctionPackage.Preview(_root).SuggestedKind, Is.EqualTo("cloud_function"));
        }

        [Test] public void CommentsStringsAndMixedHandlersDoNotGiveMisleadingKind()
        {
            Write("Handler.cs", "// class X : IPlatformFunction {}\nclass Y { string s = \"PlatformGameServer\"; }");
            Assert.That(PlatformFunctionPackage.Preview(_root).SuggestedKind, Is.Null);
            Write("Cloud.cs", "class Cloud : IPlatformFunction {}");
            Write("Room.cs", "class Room : PlatformGameServer {}");
            Assert.That(PlatformFunctionPackage.Preview(_root).SuggestedKind, Is.Null);
        }

        [Test] public void PreviewIncludesNestedFilesAndManifestExcludesBuildAndUnityMetadata()
        {
            Write("Handler.cs", "class Handler : IPlatformFunction {}");
            Write("nested/дані.json", "{}"); Write("platform.json", "{}");
            foreach (var path in new[] { "bin/x.dll", "obj/a.cs", ".git/config", "nested/a.meta", "nested/bin/b.cs" }) Write(path, "ignored");
            Assert.That(PlatformFunctionPackage.Preview(_root).Files, Is.EqualTo(new[] { "Handler.cs", "nested/дані.json", "platform.json" }));
        }

        [TestCase("change")]
        [TestCase("add")]
        [TestCase("delete")]
        public void ArchiveRejectsChangesAfterPreview(string change)
        {
            Write("Handler.cs", "class Handler : IPlatformFunction {}");
            var preview = PlatformFunctionPackage.Preview(_root);
            if (change == "change") Write("Handler.cs", "class Other : IPlatformFunction {}");
            else if (change == "add") Write("new.json", "{}");
            else File.Delete(Path.Combine(_root, "Handler.cs"));
            Assert.Throws<InvalidOperationException>(() => preview.BuildArchive());
        }

        [Test] public void ArchiveIsGzipWithTarHeaderAndExactSourceBytes()
        {
            const string source = "class Handler : IPlatformFunction {}";
            Write("Handler.cs", source);
            var archive = PlatformFunctionPackage.Preview(_root).BuildArchive();
            using (var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress))
            using (var tar = new MemoryStream())
            {
                gzip.CopyTo(tar);
                var bytes = tar.ToArray();
                Assert.That(Encoding.ASCII.GetString(bytes, 257, 5), Is.EqualTo("ustar"));
                Assert.That(Encoding.ASCII.GetString(bytes, 0, 10), Is.EqualTo("Handler.cs"));
                Assert.That(Encoding.UTF8.GetString(bytes, 512, Encoding.UTF8.GetByteCount(source)), Is.EqualTo(source));
                Assert.That(bytes.Length % 512, Is.Zero);
            }
        }

        [Test] public void EmptyFolderCannotBePreviewed() => Assert.Throws<InvalidOperationException>(() => PlatformFunctionPackage.Preview(_root));

        [Test] public void WorktreeGitFileIsExcluded()
        {
            Write("Handler.cs", "class Handler : IPlatformFunction {}");
            Write(".git", "gitdir: sensitive-host-path");
            Assert.That(PlatformFunctionPackage.Preview(_root).Files, Is.EqualTo(new[] { "Handler.cs" }));
        }

        [Test] public void UnicodeAndLongPathsUsePaxExtendedHeader()
        {
            Write("Handler.cs", "class Handler : IPlatformFunction {}");
            var name = "дані/" + new string('a', 110) + ".json";
            Write(name, "exact payload");
            var bytes = PlatformFunctionPackage.Preview(_root).BuildArchive();
            using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
            using (var tar = new MemoryStream())
            {
                gzip.CopyTo(tar);
                Assert.That(Encoding.UTF8.GetString(tar.ToArray()), Does.Contain("path=" + name + "\n"));
            }
        }
    }
}
