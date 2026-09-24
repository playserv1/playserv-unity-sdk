using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    internal sealed class ServerImagePanel : IDisposable
    {
        private readonly PlatformFunctionConnection _connection = new PlatformFunctionConnection();
        private readonly ServerImageOperation _operation = new ServerImageOperation();
        private readonly Queue<string> _log = new Queue<string>();
        private readonly ServerImageDraft _draft = new ServerImageDraft("", "", ServerImageEditorStore.Server);
        internal Func<PlayServConfig> Config;
        private DeploymentTarget _target;
        private string _folder = ServerImageEditorStore.Folder;
        private string _dockerfile = ServerImageEditorStore.Dockerfile;
        private string _tag = ServerImageEditorStore.Tag;
        private PlatformFunctionSession _session;
        private ServerImagePublisher _publisher;
        private BuiltServerImage _built;
        private ServerImagePublication _last = ServerImageEditorStore.LastPublication;
        private string _status = "Connect, select a game server, then build and review its image.";
        private string _savedPublication;
        private bool _disposed;
        private Vector2 _scroll;
        private Action _repaint;
        private LayoutState _layout;
        internal bool Running => _operation.Running;

        internal ServerImagePanel()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal void Draw(Action repaint)
        {
            _repaint = repaint;
            Update();
            if (Event.current.type == EventType.Layout || _layout == null) _layout = new LayoutState(this);
            var view = _layout;
            using (new EditorGUI.DisabledScope(view.Running || Running))
            {
                EditorGUILayout.LabelField("Dashboard", view.Target.Api, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!view.Target.CanConnect))
                    if (GUILayout.Button("Connect / refresh servers") && CanAct(view) && _target.CanConnect) Start("Connecting…", ConnectAsync);
                if (!view.Target.CanConnect) EditorGUILayout.HelpBox("Set Dashboard Address and Server Token in PlayServ Config before connecting.", MessageType.Info);
                if (view.SessionText != null) EditorGUILayout.HelpBox(view.SessionText, MessageType.Info);
                using (new EditorGUI.DisabledScope(!view.ServersReady))
                {
                    var index = EditorGUILayout.Popup("Game server", view.ServerIndex, view.ServerChoices);
                    if (index != view.ServerIndex && CanAct(view, true))
                    { _draft.SelectServer(index); ServerImageEditorStore.Server = _draft.Server; InvalidateBuild(); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    var folder = EditorGUILayout.TextField("Build context", _folder);
                    if (GUILayout.Button("Browse", GUILayout.Width(65)))
                    { var value = EditorUtility.OpenFolderPanel("Select server Docker build context", _folder, ""); if (!string.IsNullOrEmpty(value)) folder = value; }
                    if (folder != _folder) { _folder = folder; ServerImageEditorStore.Folder = folder; InvalidateBuild(); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    var file = EditorGUILayout.TextField("Dockerfile", _dockerfile);
                    if (GUILayout.Button("Browse", GUILayout.Width(65)))
                    { var value = EditorUtility.OpenFilePanel("Select Dockerfile inside build context", _folder, ""); if (!string.IsNullOrEmpty(value)) file = value; }
                    if (file != _dockerfile) { _dockerfile = file; ServerImageEditorStore.Dockerfile = file; InvalidateBuild(); }
                }
                var tag = EditorGUILayout.TextField(new GUIContent("Image tag", "Required, unique version, such as the reviewed commit SHA. Existing tags are not overwritten."), _tag);
                if (tag != _tag) { _tag = tag; ServerImageEditorStore.Tag = tag; InvalidateBuild(); }
                using (new EditorGUI.DisabledScope(view.Publisher == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Check Docker") && CanAct(view, true)) Start("Checking Docker…", async ct => { await _publisher.CheckDockerAsync(ct); return () => _status = "Docker Linux daemon is ready. Build checks linux/amd64 support."; });
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_draft.Server) || string.IsNullOrWhiteSpace(_tag)))
                        if (GUILayout.Button("Build image") && CanAct(view, true)) { _built = null; Start("Building linux/amd64…", BuildAsync); }
                }
                if (view.Built != null)
                {
                    EditorGUILayout.HelpBox(view.BuildText, MessageType.Info);
                    if (GUILayout.Button("Publish image") && CanAct(view, true) && ReferenceEquals(view.Built, _built)) Start("Publishing image…", PublishAsync);
                }
                if (view.Last != null)
                {
                    EditorGUILayout.LabelField("Last publication", view.LastText);
                    using (new EditorGUI.DisabledScope(view.Publisher == null))
                        if (GUILayout.Button("Check publication") && CanAct(view, true) && ReferenceEquals(view.Last, _last)) Start("Checking publication…", async ct =>
                        {
                            var verified = await _publisher.CheckPublicationAsync(_last, ct);
                            return () => _status = verified ? "Published: tag, manifest digest and architecture verified." : "Tag exists with compatible architecture. Its digest cannot be verified because the push response was lost.";
                        });
                }
            }
            if (view.Running && GUILayout.Button("Cancel") && Running && !_operation.Cancelled && view.Target.Equals(_target)) { _operation.Cancel(); _status = "Cancelled locally. A started push may have reached the registry; use Check publication."; }
            EditorGUILayout.HelpBox(view.Status, MessageType.Info);
            EditorGUILayout.LabelField("Publishing an image does not change a pool or start a game server.", EditorStyles.wordWrappedMiniLabel);
            if (view.Lines.Length > 0)
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(120)))
                {
                    _scroll = scroll.scrollPosition;
                    foreach (var line in view.Lines) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private bool CanAct(LayoutState view, bool connected = false)
        {
            Update();
            return !_disposed && !Running && view.Target.Equals(_target) &&
                (!connected || (_publisher != null && ReferenceEquals(view.Publisher, _publisher)));
        }

        private sealed class LayoutState
        {
            internal readonly DeploymentTarget Target;
            internal readonly ServerImagePublisher Publisher;
            internal readonly BuiltServerImage Built;
            internal readonly ServerImagePublication Last;
            internal readonly bool Running, ServersReady;
            internal readonly string SessionText, BuildText, LastText, Status;
            internal readonly string[] Lines, ServerChoices;
            internal readonly int ServerIndex;

            internal LayoutState(ServerImagePanel panel)
            {
                Target = panel._target; Publisher = panel._publisher; Built = panel._built; Last = panel._last;
                Running = panel.Running; Status = panel._status;
                var servers = panel._draft.Servers ?? Array.Empty<string>();
                ServersReady = panel._draft.Servers != null;
                ServerChoices = new[] { "Select game server…" }.Concat(servers).ToArray();
                ServerIndex = Math.Max(0, Array.IndexOf(servers, panel._draft.Server) + 1);
                if (panel._session != null)
                    SessionText = "Project: " + panel._session.Project + "   Environment: " + panel._session.Environment + "\nImages are shared by all environments of this project.";
                if (Built != null)
                    BuildText = "Ready to publish\n" + panel._session.Project + " / " + panel._session.Environment + "\n" + panel._draft.Server + ":" + panel._tag + "\nlinux/amd64\n" + Built.Id;
                if (Last != null) LastText = Last.Server + ":" + Last.Tag;
                lock (panel._log) Lines = panel._log.ToArray();
            }
        }

        private void InvalidateBuild() { _built = null; }
        private void Start(string status, Func<CancellationToken, Task<Action>> work)
        {
            if (Running || _disposed) return;
            _status = status; lock (_log) _log.Clear();
            _ = RunAsync(work);
        }
        private async Task RunAsync(Func<CancellationToken, Task<Action>> work)
        {
            await _operation.TryRunAsync(async ct =>
            {
                var apply = await work(ct); Update(); return apply;
            }, error =>
            {
                Update();
                if (!_operation.Cancelled) _status = _connection.Client?.Redact(error.Message) ?? "Cannot connect. Check Dashboard Address and Server Token.";
            });
            if (!_disposed) { RememberPublication(); _repaint?.Invoke(); }
        }
        private async Task<Action> ConnectAsync(CancellationToken ct)
        {
            _session = null; _publisher = null; _built = null; _draft.BeginConnection();
            _connection.Create(_target);
            var client = _connection.Client;
            PlatformFunctionSession session;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            { deadline.CancelAfter(TimeSpan.FromSeconds(30)); session = await client.ConnectAsync(deadline.Token); }
            var servers = await client.ListGameServersAsync(ct);
            return () =>
            {
                _session = session; _draft.CompleteConnection(servers); ServerImageEditorStore.Server = _draft.Server;
                _publisher = new ServerImagePublisher(client, new ServerImageProcess(client.Redact, AddLog));
                _status = servers.Length == 0 ? "No game_server is registered in this project/environment. Register it in PlayServ first." : "Connected. Select the server, build context and version tag.";
            };
        }
        private async Task<Action> BuildAsync(CancellationToken ct)
        {
            var built = await _publisher.BuildAsync(_folder, _dockerfile, ct);
            return () => { _built = built; _status = "Build verified. Review the target and image ID before publishing."; };
        }
        private async Task<Action> PublishAsync(CancellationToken ct)
        {
            await _publisher.PublishAsync(_built, _draft.Server, _tag, ct);
            return () => _status = "Published: tag, manifest digest and architecture verified.";
        }
        private void RememberPublication()
        {
            var publication = _publisher?.LastPublication;
            if (publication == null) return;
            var value = Newtonsoft.Json.JsonConvert.SerializeObject(publication);
            if (value == _savedPublication) return;
            _savedPublication = value; _last = publication; ServerImageEditorStore.LastPublication = publication;
        }
        private void AddLog(string message)
        {
            if (_disposed || _operation.Cancelled) return;
            lock (_log) { while (_log.Count >= 150) _log.Dequeue(); _log.Enqueue(message); }
        }
        private void Update()
        {
            if (_disposed) return;
            RememberPublication();
            var target = DeploymentTarget.Read(Config?.Invoke());
            if (!target.Equals(_target))
            {
                _target = target; _draft.Api = target.Api; _draft.Key = target.Key;
                _operation.Cancel(); _connection.Dispose(); _session = null; _publisher = null; _built = null; _draft.BeginConnection();
                _status = "Deployment settings changed. Connect and review the target.";
            }
            if (Running) _repaint?.Invoke();
        }
        public void Dispose()
        {
            if (_disposed) return;
            RememberPublication(); _disposed = true;
            EditorApplication.update -= Update; AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            _operation.Dispose(); _connection.Dispose();
        }
    }
}
