using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class ServerImagePanel : IDisposable
    {
        private readonly PlatformFunctionConnection _connection = new PlatformFunctionConnection();
        private readonly ServerImageOperation _operation = new ServerImageOperation();
        private readonly Queue<string> _log = new Queue<string>();
        private readonly ServerImageDraft _draft = new ServerImageDraft(PlatformFunctionEditorStore.Api, PlatformFunctionEditorStore.LocalKey, ServerImageEditorStore.Server);
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
        internal bool Running => _operation.Running;

        internal ServerImagePanel()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal void Draw(Action repaint)
        {
            _repaint = repaint;
            using (new EditorGUI.DisabledScope(Running))
            {
                _draft.Api = EditorGUILayout.TextField(new GUIContent("Platform API", "HTTPS origin; no /api path. Uses the same local credentials as Platform Functions."), _draft.Api);
                if (!string.IsNullOrWhiteSpace(PlatformFunctionEditorStore.EnvironmentKey)) EditorGUILayout.LabelField("Server key", "Using PLAYSERV_API_KEY");
                else
                {
                    _draft.Key = EditorGUILayout.PasswordField("Server key", _draft.Key);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Save key locally")) PlatformFunctionEditorStore.LocalKey = _draft.Key;
                        if (GUILayout.Button("Clear key")) { PlatformFunctionEditorStore.LocalKey = ""; _draft.Key = ""; }
                    }
                }
                Update();
                if (GUILayout.Button("Connect / refresh servers")) Start("Connecting…", ConnectAsync);
                if (_session != null) EditorGUILayout.HelpBox("Project: " + _session.Project + "   Environment: " + _session.Environment + "\nImages are shared by all environments of this project.", MessageType.Info);
                var servers = _draft.Servers ?? Array.Empty<string>();
                var choices = new[] { "Select game server…" }.Concat(servers).ToArray();
                var index = Array.IndexOf(servers, _draft.Server) + 1;
                var previousServer = _draft.Server;
                using (new EditorGUI.DisabledScope(_draft.Servers == null))
                    _draft.SelectServer(EditorGUILayout.Popup("Game server", Math.Max(0, index), choices));
                if (previousServer != _draft.Server) { ServerImageEditorStore.Server = _draft.Server; InvalidateBuild(); }
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
                using (new EditorGUI.DisabledScope(_publisher == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Check Docker")) Start("Checking Docker…", async ct => { await _publisher.CheckDockerAsync(ct); return () => _status = "Docker Linux daemon is ready. Build checks linux/amd64 support."; });
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_draft.Server) || string.IsNullOrWhiteSpace(_tag)))
                        if (GUILayout.Button("Build image")) { _built = null; Start("Building linux/amd64…", BuildAsync); }
                }
                if (_built != null)
                {
                    EditorGUILayout.HelpBox("Ready to publish\n" + _session.Project + " / " + _session.Environment + "\n" + _draft.Server + ":" + _tag + "\nlinux/amd64\n" + _built.Id, MessageType.Info);
                    if (GUILayout.Button("Publish image")) Start("Publishing image…", PublishAsync);
                }
                if (_last != null)
                {
                    EditorGUILayout.LabelField("Last publication", _last.Server + ":" + _last.Tag);
                    using (new EditorGUI.DisabledScope(_publisher == null))
                        if (GUILayout.Button("Check publication")) Start("Checking publication…", async ct =>
                        {
                            var verified = await _publisher.CheckPublicationAsync(_last, ct);
                            return () => _status = verified ? "Published: tag, manifest digest and architecture verified." : "Tag exists with compatible architecture. Its digest cannot be verified because the push response was lost.";
                        });
                }
            }
            if (Running && GUILayout.Button("Cancel")) { _operation.Cancel(); _status = "Cancelled locally. A started push may have reached the registry; use Check publication."; }
            EditorGUILayout.HelpBox(_status, MessageType.Info);
            EditorGUILayout.LabelField("Publishing an image does not change a pool or start a game server.", EditorStyles.wordWrappedMiniLabel);
            string[] lines; lock (_log) lines = _log.ToArray();
            if (lines.Length > 0)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(120));
                foreach (var line in lines) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
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
            await _operation.TryRunAsync(work, error => _status = _connection.Client?.Redact(error.Message) ?? "Cannot connect. Check the API origin and server key.");
            if (!_disposed) { RememberPublication(); _repaint?.Invoke(); }
        }
        private async Task<Action> ConnectAsync(CancellationToken ct)
        {
            _session = null; _publisher = null; _built = null; _draft.BeginConnection();
            _connection.Create(_draft.Api, PlatformFunctionEditorStore.ResolveKey(_draft.Key));
            PlatformFunctionEditorStore.Api = _draft.Api;
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
            _draft.RefreshCredentials(PlatformFunctionEditorStore.Api, PlatformFunctionEditorStore.LocalKey);
            if (_connection.InvalidateIfChanged(_draft.Api, PlatformFunctionEditorStore.ResolveKey(_draft.Key)))
            {
                _operation.Cancel(); _session = null; _publisher = null; _built = null; _draft.BeginConnection();
                _status = "API or key changed. Reconnect and review the target.";
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
