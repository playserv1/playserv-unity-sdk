using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlatformFunctionPanel : IDisposable
    {
        private readonly PlatformFunctionConnection _connection = new PlatformFunctionConnection();
        private string _api = PlatformFunctionEditorStore.Api;
        private string _key = PlatformFunctionEditorStore.LocalKey;
        private string _folder = PlatformFunctionEditorStore.Folder;
        private string _slug;
        private int _kind;
        private PlatformFunctionPackage _preview;
        private PlatformFunctionSession _session;
        private PlatformDeploymentReference _last = PlatformFunctionEditorStore.LastDeployment;
        private CancellationTokenSource _cancel;
        private string _status = "Connect and preview the source package before deploying.";
        private bool _disposed;
        private Vector2 _scroll;
        private Action _repaint;
        public bool Running => _cancel != null;

        public PlatformFunctionPanel()
        {
            _slug = Path.GetFileName(_folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            EditorApplication.update += CheckCredentials;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        public void Draw(Action repaint)
        {
            _repaint = repaint;
            using (new EditorGUI.DisabledScope(Running))
            {
                _api = EditorGUILayout.TextField("Platform API", _api);
                if (!string.IsNullOrWhiteSpace(PlatformFunctionEditorStore.EnvironmentKey))
                    EditorGUILayout.LabelField("Server key", "Using PLAYSERV_API_KEY");
                else
                {
                    _key = EditorGUILayout.PasswordField("Server key", _key);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Save key locally")) PlatformFunctionEditorStore.LocalKey = _key;
                        if (GUILayout.Button("Clear key")) { PlatformFunctionEditorStore.LocalKey = ""; _key = ""; }
                    }
                }
                CheckCredentials();
                if (GUILayout.Button("Connect")) _ = RunAsync(ConnectAsync);
                if (_session != null)
                    EditorGUILayout.HelpBox("Target: " + _session.Project + " / " + _session.Environment + "\n" + _api, MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    var folder = EditorGUILayout.TextField("Function folder", _folder);
                    if (GUILayout.Button("Browse", GUILayout.Width(70)))
                    {
                        var chosen = EditorUtility.OpenFolderPanel("Select function source folder", _folder, "");
                        if (!string.IsNullOrEmpty(chosen)) folder = chosen;
                    }
                    if (folder != _folder) SelectFolder(folder);
                }
                if (_preview == null && Directory.Exists(_folder))
                {
                    string[] children;
                    try { children = Directory.GetDirectories(_folder).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => p, StringComparer.Ordinal).ToArray(); }
                    catch (IOException) { children = Array.Empty<string>(); }
                    catch (UnauthorizedAccessException) { children = Array.Empty<string>(); }
                    if (children.Length > 0)
                    {
                        var options = new[] { "Select a child function folder…" }.Concat(children.Select(Path.GetFileName)).ToArray();
                        var child = EditorGUILayout.Popup("Functions", 0, options);
                        if (child > 0) SelectFolder(children[child - 1]);
                    }
                }
                _slug = EditorGUILayout.TextField("Function slug", _slug);
                _kind = EditorGUILayout.Popup("Function type", _kind, new[] { "Choose type…", "cloud_function", "game_server" });
                if (GUILayout.Button("Preview package")) _ = RunAsync(PreviewAsync);
                if (_preview != null)
                {
                    EditorGUILayout.LabelField("Package files (" + _preview.Files.Length + ")");
                    _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(140));
                    foreach (var file in _preview.Files) EditorGUILayout.LabelField(file);
                    EditorGUILayout.EndScrollView();
                    if (_kind == 0) EditorGUILayout.HelpBox("Handler type is ambiguous. Choose cloud_function or game_server explicitly.", MessageType.Warning);
                }
                using (new EditorGUI.DisabledScope(_session == null || _preview == null || _kind == 0 || string.IsNullOrWhiteSpace(_slug)))
                    if (GUILayout.Button("Deploy function")) _ = RunAsync(DeployAsync);
                if (_last != null)
                {
                    EditorGUILayout.LabelField("Last deployment", _last.Id);
                    EditorGUILayout.LabelField("Deployment target", _last.Project + " / " + _last.Environment);
                    using (new EditorGUI.DisabledScope(_session == null))
                        if (GUILayout.Button("Resume status")) _ = RunAsync(ct => PollAsync(_last, ct));
                }
            }
            if (Running && GUILayout.Button("Stop waiting")) _cancel.Cancel();
            EditorGUILayout.HelpBox(_status, MessageType.Info);
            EditorGUILayout.LabelField("Stopping local work does not cancel an accepted server deployment.", EditorStyles.wordWrappedMiniLabel);
        }

        private void SelectFolder(string folder)
        {
            _folder = folder; PlatformFunctionEditorStore.Folder = folder; _preview = null; _kind = 0;
            _slug = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        private void CheckCredentials()
        {
            if (_connection.InvalidateIfChanged(_api, PlatformFunctionEditorStore.ResolveKey(_key)))
            {
                _cancel?.Cancel(); _session = null;
                _status = "API or key changed. Reconnect to verify the target."; _repaint?.Invoke();
            }
        }
        private async Task ConnectAsync(CancellationToken ct)
        {
            _session = null;
            _connection.Create(_api, PlatformFunctionEditorStore.ResolveKey(_key));
            PlatformFunctionEditorStore.Api = _api;
            _session = await _connection.Client.ConnectAsync(ct);
            _status = "Connected. Review the target and source package before deploying.";
        }
        private async Task PreviewAsync(CancellationToken ct)
        {
            _preview = null;
            var preview = await Task.Run(() => PlatformFunctionPackage.Preview(_folder), ct);
            ct.ThrowIfCancellationRequested(); _preview = preview;
            _kind = preview.SuggestedKind == "cloud_function" ? 1 : preview.SuggestedKind == "game_server" ? 2 : 0;
            _status = "Package preview ready. Upload will reject any source changes since this preview.";
        }
        private async Task DeployAsync(CancellationToken ct)
        {
            var client = _connection.Client;
            var archive = await Task.Run(() => _preview.BuildArchive(), ct);
            ct.ThrowIfCancellationRequested();
            if (!_connection.Matches(_api, PlatformFunctionEditorStore.ResolveKey(_key))) throw new InvalidOperationException("Target changed. Reconnect before deploying.");
            _status = "Uploading once. If the response is lost, check server deployments before attempting another upload."; _repaint?.Invoke();
            _last = await client.UploadAsync(_slug, _kind == 1 ? "cloud_function" : "game_server", archive, ct);
            PlatformFunctionEditorStore.LastDeployment = _last;
            await PollAsync(_last, ct);
        }
        private async Task PollAsync(PlatformDeploymentReference deployment, CancellationToken ct)
        {
            await _connection.Client.PollAsync(deployment, phase => { _status = "Deployment " + deployment.Id + ": " + phase; _repaint?.Invoke(); }, ct);
            _status = "Deployment " + deployment.Id + " deployed successfully.";
        }
        private async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (Running || _disposed) return;
            var cancel = _cancel = new CancellationTokenSource();
            var client = _connection.Client;
            try { await action(cancel.Token); }
            catch (OperationCanceledException) { _status = "Local operation stopped. An accepted deployment may still be running; use Resume status."; }
            catch (Exception e)
            {
                var message = (_connection.Client ?? client)?.Redact(e.Message) ?? "The operation could not be completed. Check the API, source folder and key.";
                _status = "Failed: " + message;
            }
            finally { cancel.Dispose(); _cancel = null; if (!_disposed) _repaint?.Invoke(); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; EditorApplication.update -= CheckCredentials;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            _cancel?.Cancel(); _connection.Dispose(); _repaint = null;
        }
    }
}
