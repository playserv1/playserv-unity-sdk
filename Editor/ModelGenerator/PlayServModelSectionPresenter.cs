using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServModelSectionPresenter : IPlayServEditorSection
    {
        private bool _commandRunning;
        private string _status = string.Empty;
        private MessageType _statusType = MessageType.Info;
        private PlayServSchemaToolResponse _response;

        public void Dispose()
        {
        }

        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var installed = PlayServSchemaToolRunner.IsInstalled;
            var configured = PlayServSchemaToolRunner.IsConfigured;
            var watching = PlayServSchemaToolRunner.IsWatchRunning;
            var subtitle = installed
                ? $"External analyzer | {(configured ? "Configured" : "Not configured")} | " +
                  $"{(watching ? "Watch running" : "Watch stopped")}"
                : "External analyzer | Companion package is not installed";
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldModel,
                "Schema",
                "Schema Tool",
                subtitle);

            if (expanded)
            {
                GUILayout.Space(6f);
                if (!installed)
                    DrawInstall(context);
                else
                    DrawInstalled(context, configured, watching);
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldModel, state.FoldModel);
        }

        private void DrawInstall(PlayServWindowContext context)
        {
            var packageId = PlayServSchemaToolPackage.PackageId;
            var packageError = PlayServCompanionPackageManager.GetLastError(packageId);
            if (!string.IsNullOrWhiteSpace(packageError))
                PlayServWindowChrome.DrawNotice(packageError, MessageType.Warning);

            using (new EditorGUI.DisabledScope(
                       PlayServCompanionPackageManager.IsBusy || _commandRunning))
            {
                var label = PlayServCompanionPackageManager.IsBusyFor(packageId)
                    ? "Installing..."
                    : "Install Schema Tool";
                if (PlayServWindowChrome.DrawActionButton(
                        label,
                        PlayServWindowButtonTone.Primary,
                        GUILayout.Width(190f),
                        GUILayout.Height(32f)) &&
                    !PlayServCompanionPackageManager.Install(
                        PlayServSchemaToolPackage.Definition,
                        out var error))
                {
                    SetStatus(error, MessageType.Warning, context);
                }
            }
        }

        private void DrawInstalled(
            PlayServWindowContext context,
            bool configured,
            bool watching)
        {
            DrawMetrics(configured, watching);

            if (!string.IsNullOrWhiteSpace(_status))
            {
                GUILayout.Space(8f);
                PlayServWindowChrome.DrawNotice(_status, _statusType);
            }

            GUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(
                       _commandRunning || PlayServCompanionPackageManager.IsBusy))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton(
                            configured ? "Reinitialize" : "Initialize",
                            PlayServWindowButtonTone.Secondary,
                            GUILayout.Width(120f),
                            GUILayout.Height(32f)))
                    {
                        _ = ExecuteAsync("init", context);
                    }

                    GUILayout.Space(6f);
                    using (new EditorGUI.DisabledScope(!configured))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Analyze",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(108f),
                                GUILayout.Height(32f)))
                        {
                            _ = ExecuteAsync("analyze", context);
                        }

                        GUILayout.Space(6f);
                        if (PlayServWindowChrome.DrawActionButton(
                                "Generate",
                                PlayServWindowButtonTone.Primary,
                                GUILayout.Width(112f),
                                GUILayout.Height(32f)))
                        {
                            _ = ExecuteAsync("generate", context);
                        }

                        GUILayout.Space(6f);
                        if (PlayServWindowChrome.DrawActionButton(
                                "Validate",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(108f),
                                GUILayout.Height(32f)))
                        {
                            _ = ExecuteAsync("validate", context);
                        }
                    }
                }

                GUILayout.Space(8f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!configured))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                watching ? "Stop Watch" : "Start Watch",
                                watching
                                    ? PlayServWindowButtonTone.Secondary
                                    : PlayServWindowButtonTone.Primary,
                                GUILayout.Width(132f),
                                GUILayout.Height(32f)))
                        {
                            ToggleWatch(watching, context);
                        }
                    }

                    GUILayout.Space(6f);
                    if (PlayServWindowChrome.DrawActionButton(
                            "Create IDE Launcher",
                            PlayServWindowButtonTone.Secondary,
                            GUILayout.Width(170f),
                            GUILayout.Height(32f)))
                    {
                        CreateLauncher(context);
                    }

                    GUILayout.Space(6f);
                    using (new EditorGUI.DisabledScope(!configured))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Open Config",
                                PlayServWindowButtonTone.Ghost,
                                GUILayout.Width(120f),
                                GUILayout.Height(32f)))
                        {
                            EditorUtility.RevealInFinder(
                                PlayServSchemaToolRunner.ConfigurationPath);
                        }
                    }
                }

                GUILayout.Space(8f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(watching))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Remove Package",
                                PlayServWindowButtonTone.Ghost,
                                GUILayout.Width(140f),
                                GUILayout.Height(28f)) &&
                            !PlayServCompanionPackageManager.Remove(
                                PlayServSchemaToolPackage.Definition,
                                null,
                                out var error))
                        {
                            SetStatus(error, MessageType.Warning, context);
                        }
                    }
                }
            }
        }

        private void DrawMetrics(bool configured, bool watching)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                PlayServWindowChrome.DrawOverviewCard(
                    "TOOL",
                    _response == null || string.IsNullOrWhiteSpace(_response.version)
                        ? "Installed"
                        : _response.version,
                    "External process");
                GUILayout.Space(8f);
                PlayServWindowChrome.DrawOverviewCard(
                    "SCHEMAS",
                    _response == null ? "—" : _response.schemaCount.ToString(),
                    configured ? "Project configured" : "Run Initialize");
                GUILayout.Space(8f);
                PlayServWindowChrome.DrawOverviewCard(
                    "WATCH",
                    watching ? "Running" : "Stopped",
                    "File changes");
            }
        }

        private async Task ExecuteAsync(string command, PlayServWindowContext context)
        {
            _commandRunning = true;
            SetStatus($"Running {command}...", MessageType.Info, context);
            try
            {
                var result = await PlayServSchemaToolRunner.RunAsync(command);
                _response = result.Response;
                SetStatus(
                    string.IsNullOrWhiteSpace(result.Message)
                        ? result.Success ? "Schema Tool completed." : "Schema Tool failed."
                        : result.Message,
                    result.Success ? MessageType.Info : MessageType.Warning,
                    context);
                if (result.Success &&
                    (command == "generate" || command == "init"))
                {
                    AssetDatabase.Refresh();
                }
            }
            finally
            {
                _commandRunning = false;
                context.Repaint();
            }
        }

        private void ToggleWatch(bool watching, PlayServWindowContext context)
        {
            var success = watching
                ? PlayServSchemaToolRunner.StopWatch(out var error)
                : PlayServSchemaToolRunner.StartWatch(out error);
            SetStatus(
                success
                    ? watching ? "Schema watcher stopped." : "Schema watcher started."
                    : error,
                success ? MessageType.Info : MessageType.Warning,
                context);
        }

        private void CreateLauncher(PlayServWindowContext context)
        {
            var success = PlayServSchemaToolRunner.CreateIdeLaunchers(
                out var launcherPath,
                out var error);
            SetStatus(
                success
                    ? $"IDE launcher created at {RelativeToProject(launcherPath)}."
                    : error,
                success ? MessageType.Info : MessageType.Warning,
                context);
        }

        private void SetStatus(
            string message,
            MessageType type,
            PlayServWindowContext context)
        {
            _status = message ?? string.Empty;
            _statusType = type;
            context.Repaint();
        }

        private static string RelativeToProject(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return path.StartsWith(
                    PlayServSchemaToolRunner.ProjectRoot,
                    System.StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(PlayServSchemaToolRunner.ProjectRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : path;
            }
            catch
            {
                return path;
            }
        }
    }
}
