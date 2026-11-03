using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Playserv.DebugTerminal.Tests
{
    public sealed class DebugTerminalPrefabReferenceTests
    {
        [Test]
        public void SamplePrefabs_ReferenceRuntimeComponentsByTheirPreservedGuids()
        {
            var package = PackageInfo.FindForAssembly(typeof(PlayServDebugTerminal).Assembly);
            Assert.NotNull(package);

            var root = package.resolvedPath;
            var terminalGuid = ReadGuid(Path.Combine(root, "Runtime/Terminal/PlayServDebugTerminal.cs.meta"));
            var bootstrapGuid = ReadGuid(Path.Combine(root, "Runtime/Terminal/PlayServDebugTerminalBootstrap.cs.meta"));
            var terminalPrefab = File.ReadAllText(Path.Combine(root, "Samples~/DebugTerminal/Prefabs/PlayServ Debug Terminal.prefab"));
            var connectionPrefab = File.ReadAllText(Path.Combine(root, "Samples~/DebugTerminal/Prefabs/PlayServ Connection.prefab"));

            StringAssert.Contains($"guid: {terminalGuid}", terminalPrefab);
            StringAssert.Contains($"guid: {bootstrapGuid}", connectionPrefab);
        }

        [Test]
        public void SampleScene_ReferencesBothPrefabAssets()
        {
            var package = PackageInfo.FindForAssembly(typeof(PlayServDebugTerminal).Assembly);
            var root = package.resolvedPath;
            var terminalPrefabGuid = ReadGuid(Path.Combine(root, "Samples~/DebugTerminal/Prefabs/PlayServ Debug Terminal.prefab.meta"));
            var connectionPrefabGuid = ReadGuid(Path.Combine(root, "Samples~/DebugTerminal/Prefabs/PlayServ Connection.prefab.meta"));
            var scene = File.ReadAllText(Path.Combine(root, "Samples~/DebugTerminal/DebugTerminal.unity"));

            StringAssert.Contains($"guid: {terminalPrefabGuid}", scene);
            StringAssert.Contains($"guid: {connectionPrefabGuid}", scene);
        }

        private static string ReadGuid(string metaPath)
        {
            var match = Regex.Match(File.ReadAllText(metaPath), "^guid: ([a-f0-9]+)\\r?$", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, metaPath);
            return match.Groups[1].Value;
        }
    }
}
