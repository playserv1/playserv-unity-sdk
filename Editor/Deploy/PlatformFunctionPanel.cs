using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal sealed class PlatformFunctionPanel : IDisposable
    {
        private readonly PlatformFunctionConnection _connection = new PlatformFunctionConnection();
        internal Func<PlayServConfig> Config;
        private DeploymentTarget _target;
        private readonly ServerImageOperation _operation = new ServerImageOperation();
        private string _folder = PlatformFunctionEditorStore.Folder;
        private string _slug;
        private int _kind;
        private PlatformFunctionPackage _preview;
        private PlatformFunctionSession _session;
        private PlatformDeploymentReference _last = PlatformFunctionEditorStore.LastDeployment;
        private string _status = "Connect and preview the source package before deploying.";
        private bool _disposed;
        private Vector2 _scroll;
        private Action _repaint;
        private LayoutState _layout;
        public bool Running => _operation.Running;

        public PlatformFunctionPanel()
        {
            _slug = Path.GetFileName(_folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            EditorApplication.update += CheckCredentials;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        public void Draw(Action repaint)
        {
            _repaint = repaint;
            CheckCredentials();
            if (Event.current.type == EventType.Layout || _layout == null) _layout = new LayoutState(this);
            var view = _layout;
            using (new EditorGUI.DisabledScope(view.Running || Running))
            {
                EditorGUILayout.LabelField("Dashboard", view.Target.Api, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!view.Target.CanConnect))
                    if (GUILayout.Button("Connect") && CanAct(view) && _target.CanConnect) _ = RunAsync(ConnectAsync);
                if (!view.Target.CanConnect) EditorGUILayout.HelpBox("Set Dashboard Address and Server Token in PlayServ Config before connecting.", MessageType.Info);
                if (view.SessionText != null) EditorGUILayout.HelpBox(view.SessionText, MessageType.Info);

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
                if (view.Children.Length > 0)
                {
                    var child = EditorGUILayout.Popup("Functions", 0, view.ChildOptions);
                    if (child > 0 && CanAct(view)) SelectFolder(view.Children[child - 1]);
                }
                _slug = EditorGUILayout.TextField("Function slug", _slug);
                _kind = EditorGUILayout.Popup("Function type", _kind, new[] { "Choose type…", "cloud_function", "game_server" });
                if (GUILayout.Button("Preview package") && CanAct(view)) _ = RunAsync(PreviewAsync);
                if (view.Preview != null)
                {
                    EditorGUILayout.LabelField("Package files (" + view.Files.Length + ")");
                    using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(140)))
                    {
                        _scroll = scroll.scrollPosition;
                        foreach (var file in view.Files) EditorGUILayout.LabelField(file);
                    }
                    if (view.AmbiguousKind) EditorGUILayout.HelpBox("Handler type is ambiguous. Choose cloud_function or game_server explicitly.", MessageType.Warning);
                }
                using (new EditorGUI.DisabledScope(view.SessionText == null || view.Preview == null || _kind == 0 || string.IsNullOrWhiteSpace(_slug)))
                    if (GUILayout.Button("Deploy function") && CanAct(view, true) && _preview != null && ReferenceEquals(view.Preview, _preview)) _ = RunAsync(DeployAsync);
                if (view.Last != null)
                {
                    EditorGUILayout.LabelField("Last deployment", view.LastId);
                    EditorGUILayout.LabelField("Deployment target", view.LastTarget);
                    using (new EditorGUI.DisabledScope(view.SessionText == null))
                        if (GUILayout.Button("Resume status") && CanAct(view, true) && ReferenceEquals(view.Last, _last)) _ = RunAsync(ct => PollAsync(_last, ct));
                }
            }
            if (view.Running && GUILayout.Button("Stop waiting") && Running && !_operation.Cancelled && view.Target.Equals(_target)) { _operation.Cancel(); _status = "Stopped locally. An accepted deployment may still be running."; }
            EditorGUILayout.HelpBox(view.Status, MessageType.Info);
            EditorGUILayout.LabelField("Stopping local work does not cancel an accepted server deployment.", EditorStyles.wordWrappedMiniLabel);
        }

        private bool CanAct(LayoutState view, bool connected = false)
        {
            CheckCredentials();
            return !_disposed && !Running && view.Target.Equals(_target) &&
                (!connected || (_session != null && ReferenceEquals(view.Client, _connection.Client)));
        }

        private sealed class LayoutState
        {
            internal readonly DeploymentTarget Target;
            internal readonly PlatformFunctionClient Client;
            internal readonly PlatformFunctionPackage Preview;
            internal readonly PlatformDeploymentReference Last;
            internal readonly bool Running, AmbiguousKind;
            internal readonly string SessionText, Status, LastId, LastTarget;
            internal readonly string[] Files, Children, ChildOptions;

            internal LayoutState(PlatformFunctionPanel panel)
            {
                Target = panel._target; Client = panel._connection.Client; Preview = panel._preview; Last = panel._last;
                Running = panel.Running; AmbiguousKind = panel._kind == 0; Status = panel._status;
                if (panel._session != null)
                    SessionText = "Target: " + panel._session.Project + " / " + panel._session.Environment + "\n" + Target.Api;
                Files = Preview?.Files ?? Array.Empty<string>();
                Children = Array.Empty<string>();
                if (Preview == null && Directory.Exists(panel._folder))
                {
                    try { Children = Directory.GetDirectories(panel._folder).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => p, StringComparer.Ordinal).ToArray(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                ChildOptions = new[] { "Select a child function folder…" }.Concat(Children.Select(Path.GetFileName)).ToArray();
                if (Last != null) { LastId = Last.Id; LastTarget = Last.Project + " / " + Last.Environment; }
            }
        }

        private void SelectFolder(string folder)
        {
            _folder = folder; PlatformFunctionEditorStore.Folder = folder; _preview = null; _kind = 0;
            _slug = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        private void CheckCredentials()
        {
            if (_disposed) return;
            var target = DeploymentTarget.Read(Config?.Invoke());
            if (target.Equals(_target)) return;
            _target = target;
            _operation.Cancel(); _connection.Dispose(); _session = null; _preview = null;
            _status = "Deployment settings changed. Connect to verify the target."; _repaint?.Invoke();
        }
        private async Task<Action> ConnectAsync(CancellationToken ct)
        {
            _session = null;
            _connection.Create(_target);
            PlatformFunctionSession session;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            { deadline.CancelAfter(TimeSpan.FromSeconds(30)); session = await _connection.Client.ConnectAsync(deadline.Token); }
            return () => { _session = session; _status = "Connected. Review the target and source package before deploying."; };
        }
        private async Task<Action> PreviewAsync(CancellationToken ct)
        {
            _preview = null;
            var preview = await Task.Run(() => PlatformFunctionPackage.Preview(_folder), ct);
            return () => { _preview = preview;
            _kind = preview.SuggestedKind == "cloud_function" ? 1 : preview.SuggestedKind == "game_server" ? 2 : 0;
            _status = "Package preview ready. Upload will reject any source changes since this preview."; };
        }
        private async Task<Action> DeployAsync(CancellationToken ct)
        {
            var client = _connection.Client;
            var archive = await Task.Run(() => _preview.BuildArchive(), ct);
            CheckCredentials(); ct.ThrowIfCancellationRequested();
            if (!_connection.Matches(_target.Api, _target.Key)) throw new InvalidOperationException("Target changed. Reconnect before deploying.");
            _status = "Uploading once. If the response is lost, check server deployments before attempting another upload."; _repaint?.Invoke();
            var last = await client.UploadAsync(_slug, _kind == 1 ? "cloud_function" : "game_server", archive, ct);
            CheckCredentials(); ct.ThrowIfCancellationRequested();
            _last = last; PlatformFunctionEditorStore.LastDeployment = last;
            return await PollAsync(last, ct);
        }
        private async Task<Action> PollAsync(PlatformDeploymentReference deployment, CancellationToken ct)
        {
            await _connection.Client.PollAsync(deployment, phase => { if (ct.IsCancellationRequested || _disposed) return; _status = "Deployment " + deployment.Id + ": " + phase; _repaint?.Invoke(); }, ct);
            return () => _status = "Deployment " + deployment.Id + " deployed successfully.";
        }
        private async Task RunAsync(Func<CancellationToken, Task<Action>> action)
        {
            if (Running || _disposed) return;
            var client = _connection.Client;
            await _operation.TryRunAsync(async ct =>
            {
                var apply = await action(ct);
                CheckCredentials();
                return apply;
            }, error =>
            {
                CheckCredentials();
                if (_operation.Cancelled) return;
                var message = (_connection.Client ?? client)?.Redact(error.Message) ?? "The operation could not be completed. Check the Dashboard Address, source folder and Server Token.";
                _status = "Failed: " + message;
            });
            if (!_disposed) _repaint?.Invoke();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; EditorApplication.update -= CheckCredentials;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            _operation.Dispose(); _connection.Dispose(); _repaint = null;
        }
    }
}
