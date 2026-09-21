using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Playserv.Deploy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Editor.Tests
{
    public class PlatformFunctionEditorTests
    {
        [Test] public void LastDeploymentPersistsOnlyApiTargetAndId()
        {
            var previous = PlatformFunctionEditorStore.LastDeployment;
            try
            {
                PlatformFunctionEditorStore.LastDeployment = new PlatformDeploymentReference { Api = "https://platform.example", Project = "test", Environment = "dev", Id = "deployment-123" };
                var restored = PlatformFunctionEditorStore.LastDeployment;
                Assert.That(restored.Api, Is.EqualTo("https://platform.example")); Assert.That(restored.Project, Is.EqualTo("test"));
                Assert.That(restored.Environment, Is.EqualTo("dev")); Assert.That(restored.Id, Is.EqualTo("deployment-123"));
                Assert.That(typeof(PlatformDeploymentReference).GetFields().Select(f => f.Name), Is.EquivalentTo(new[] { "Api", "Project", "Environment", "Id" }));
            }
            finally { PlatformFunctionEditorStore.LastDeployment = previous; }
        }

        [Test] public void LegacyRpcZipStillPreservesRelativeSourcePaths()
        {
            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "playserv-rpc-zip-" + Guid.NewGuid().ToString("N"))).FullName;
            string zip = null;
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "nested"));
                var file = Path.Combine(root, "nested", "Rpc.cs"); File.WriteAllText(file, "legacy RPC source");
                zip = new DeploymentZipBuilder().CreateRelativeZipArchive(root, new[] { file });
                using (var archive = ZipFile.OpenRead(zip))
                {
                    Assert.That(archive.Entries.Select(e => e.FullName), Is.EqualTo(new[] { "nested/Rpc.cs" }));
                    using (var reader = new StreamReader(archive.Entries[0].Open())) Assert.That(reader.ReadToEnd(), Is.EqualTo("legacy RPC source"));
                }
            }
            finally { if (zip != null) File.Delete(zip); Directory.Delete(root, true); }
        }

        [UnityTest] public IEnumerator BothDeploymentModesRenderInRealEditorWindow()
        {
            var window = ScriptableObject.CreateInstance<PlatformDeploymentTestWindow>();
            try
            {
                window.ShowUtility(); window.position = new Rect(20, 20, 720, 850);
                foreach (var mode in new[] { 0, 1 })
                {
                    window.Mode = mode; window.Rendered = false;
                    for (var i = 0; i < 30 && !window.Rendered; i++) { window.Repaint(); yield return null; }
                    Assert.That(window.Rendered, Is.True, "Deployment mode " + mode + " should render in IMGUI.");
                }
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }
    }

    internal sealed class PlatformDeploymentTestWindow : EditorWindow
    {
        public int Mode;
        public bool Rendered;
        private PlayServDeploymentSectionPresenter _presenter;
        private PlayServConnectionController _connection;
        private PlayServWindowContext _context;
        private void OnGUI()
        {
            if (_presenter == null)
            {
                var state = new PlayServWindowState { FoldDeployment = true };
                _connection = new PlayServConnectionController(state, Repaint);
                _context = new PlayServWindowContext(this, state, _connection, "", 25, () => { }, () => { }, () => { }, Repaint);
                _presenter = new PlayServDeploymentSectionPresenter(_ => null);
            }
            PlayServWindowTheme.Ensure();
            typeof(PlayServDeploymentSectionPresenter).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_presenter, Mode);
            _presenter.Draw(_context);
            if (Event.current.type == EventType.Repaint) Rendered = true;
        }
        private void OnDisable() { _presenter?.Dispose(); _presenter = null; _connection?.Dispose(); _connection = null; }
    }
}
