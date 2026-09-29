using System;
using System.IO;
using System.Threading.Tasks;
using Playserv.ModelGenerator.Editor;
using Playserv.Wrapper;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServModelSectionPresenter : IPlayServEditorSection
    {
        private bool _localCommandRunning;
        private bool _serverCommandRunning;
        private bool _showAdvanced;
        private string _localStatus = string.Empty;
        private MessageType _localStatusType = MessageType.Info;
        private string _serverStatus = string.Empty;
        private MessageType _serverStatusType = MessageType.Info;
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
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldModel,
                "Schema",
                "Schema Workflows",
                "Generate schemas from local C# contracts or import a local JSON schema to generate Unity models.");

            if (expanded)
            {
                DrawLocalContracts(context, installed, configured, watching);
                GUILayout.Space(18f);
                DrawServerSchema(context);
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldModel, state.FoldModel);
        }

        private void DrawLocalContracts(
            PlayServWindowContext context,
            bool installed,
            bool configured,
            bool watching)
        {
            GUILayout.Label("Local contracts", PlayServWindowTheme.MiniHeadingStyle);
            GUILayout.Label(
                "C# with [PlayServSchema]  →  JSON Schema and configured local outputs",
                PlayServWindowTheme.SectionSubtitleStyle);
            GUILayout.Space(8f);

            if (!installed)
            {
                DrawInstall(context);
                return;
            }

            var contractSummary = _response == null
                ? configured ? "Ready to analyze" : "Configuration required"
                : $"{_response.schemaCount} contracts found";
            GUILayout.Label(
                watching ? $"{contractSummary} | Watch running" : contractSummary,
                PlayServWindowTheme.SectionLabelStyle);

            if (!string.IsNullOrWhiteSpace(_localStatus))
            {
                GUILayout.Space(6f);
                PlayServWindowChrome.DrawNotice(_localStatus, _localStatusType);
            }

            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(
                       _localCommandRunning ||
                       _serverCommandRunning ||
                       PlayServCompanionPackageManager.IsBusy))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!configured)
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Initialize Local Schema",
                                PlayServWindowButtonTone.Primary,
                                GUILayout.Width(180f),
                                GUILayout.Height(32f)))
                        {
                            _ = ExecuteLocalAsync("init", context);
                        }
                    }
                    else
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Generate Local Outputs",
                                PlayServWindowButtonTone.Primary,
                                GUILayout.Width(184f),
                                GUILayout.Height(32f)))
                        {
                            _ = ExecuteLocalAsync("generate", context);
                        }

                        GUILayout.Space(6f);
                        if (PlayServWindowChrome.DrawActionButton(
                                "Analyze",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(104f),
                                GUILayout.Height(32f)))
                        {
                            _ = ExecuteLocalAsync("analyze", context);
                        }
                    }

                    GUILayout.FlexibleSpace();
                    if (PlayServWindowChrome.DrawActionButton(
                            _showAdvanced ? "Hide Advanced" : "Advanced",
                            PlayServWindowButtonTone.Ghost,
                            GUILayout.Width(126f),
                            GUILayout.Height(32f)))
                    {
                        _showAdvanced = !_showAdvanced;
                    }
                }

                if (_showAdvanced)
                    DrawAdvancedLocalControls(context, configured, watching);
            }
        }

        private void DrawInstall(PlayServWindowContext context)
        {
            var packageId = PlayServSchemaToolPackage.PackageId;
            var packageError =
                PlayServCompanionPackageManager.GetLastError(packageId);
            if (!string.IsNullOrWhiteSpace(packageError))
                PlayServWindowChrome.DrawNotice(packageError, MessageType.Warning);

            using (new EditorGUI.DisabledScope(
                       PlayServCompanionPackageManager.IsBusy ||
                       _localCommandRunning ||
                       _serverCommandRunning))
            {
                var label = PlayServCompanionPackageManager.IsBusyFor(packageId)
                    ? "Installing..."
                    : "Install Local Schema Tool";
                if (PlayServWindowChrome.DrawActionButton(
                        label,
                        PlayServWindowButtonTone.Primary,
                        GUILayout.Width(190f),
                        GUILayout.Height(32f)) &&
                    !PlayServCompanionPackageManager.Install(
                        PlayServSchemaToolPackage.Definition,
                        out var error))
                {
                    SetLocalStatus(error, MessageType.Warning, context);
                }
            }
        }

        private void DrawAdvancedLocalControls(
            PlayServWindowContext context,
            bool configured,
            bool watching)
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(
                       PlayServWindowTheme.LogContainerStyle))
            {
                GUILayout.Label(
                    "Local tool controls",
                    PlayServWindowTheme.MiniHeadingStyle);
                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!configured))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Validate Generated",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(152f),
                                GUILayout.Height(30f)))
                        {
                            _ = ExecuteLocalAsync("validate", context);
                        }

                        GUILayout.Space(6f);
                        if (PlayServWindowChrome.DrawActionButton(
                                "Push to PlayServ",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(142f),
                                GUILayout.Height(30f)) &&
                            EditorUtility.DisplayDialog(
                                "Push code-first schema?",
                                "This sends every attributed C# schema as one atomic bundle " +
                                "to the configured PlayServ project and environment. " +
                                "PLAYSERV_SERVER_KEY must be present in the Unity process environment.",
                                "Push",
                                "Cancel"))
                        {
                            _ = ExecuteLocalAsync("push", context);
                        }

                        GUILayout.Space(6f);
                        if (PlayServWindowChrome.DrawActionButton(
                                watching ? "Stop Watch" : "Start Watch",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(120f),
                                GUILayout.Height(30f)))
                        {
                            ToggleWatch(watching, context);
                        }
                    }

                    GUILayout.Space(6f);
                    if (PlayServWindowChrome.DrawActionButton(
                            "Create IDE Launcher",
                            PlayServWindowButtonTone.Secondary,
                            GUILayout.Width(158f),
                            GUILayout.Height(30f)))
                    {
                        CreateLauncher(context);
                    }
                }

                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!configured))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Open Configuration",
                                PlayServWindowButtonTone.Ghost,
                                GUILayout.Width(152f),
                                GUILayout.Height(28f)))
                        {
                            EditorUtility.RevealInFinder(
                                PlayServSchemaToolRunner.ConfigurationPath);
                        }
                    }

                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(watching))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Remove Local Tool",
                                PlayServWindowButtonTone.Ghost,
                                GUILayout.Width(142f),
                                GUILayout.Height(28f)) &&
                            !PlayServCompanionPackageManager.Remove(
                                PlayServSchemaToolPackage.Definition,
                                null,
                                out var error))
                        {
                            SetLocalStatus(error, MessageType.Warning, context);
                        }
                    }
                }
            }
        }

        private void DrawServerSchema(PlayServWindowContext context)
        {
            GUILayout.Label("Local JSON schema", PlayServWindowTheme.MiniHeadingStyle);
            GUILayout.Label(
                "Local JSON Schema  →  generated Unity C# models",
                PlayServWindowTheme.SectionSubtitleStyle);
            GUILayout.Space(8f);

            var current =
                PlayServServerSchemaWorkflow.ReadCurrent(out var currentError);
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.LogContainerStyle))
                DrawSchemaInfo("Current", current);

            if (!string.IsNullOrWhiteSpace(currentError))
                PlayServWindowChrome.DrawNotice(currentError, MessageType.Warning);

            if (!string.IsNullOrWhiteSpace(_serverStatus))
            {
                GUILayout.Space(6f);
                PlayServWindowChrome.DrawNotice(_serverStatus, _serverStatusType);
            }

            var canGenerate = current.Exists && string.IsNullOrWhiteSpace(currentError);

            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(
                       _serverCommandRunning ||
                       _localCommandRunning ||
                       PlayServCompanionPackageManager.IsBusy))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Import JSON & Generate", PlayServWindowButtonTone.Secondary,
                            GUILayout.Width(184f), GUILayout.Height(32f)))
                    {
                        var path = EditorUtility.OpenFilePanel("Select schema JSON", "", "json");
                        if (!string.IsNullOrEmpty(path))
                        {
                            var result = SchemaCodeGenerator.GenerateFromSchemaFile(path, acceptAsCurrent: true);
                            SetServerStatus(result.Success ? $"Generated {result.GeneratedFileCount} C# model files."
                                : result.Error, result.Success ? MessageType.Info : MessageType.Warning, context);
                        }
                    }
                    using (new EditorGUI.DisabledScope(!canGenerate))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                "Regenerate Current C#",
                                PlayServWindowButtonTone.Primary,
                                GUILayout.Width(184f),
                                GUILayout.Height(32f)))
                        {
                            RegenerateCurrentSchema(context);
                        }
                    }
                }
            }
        }

        private static void DrawSchemaInfo(
            string label,
            PlayServSchemaDocumentInfo info)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    label,
                    PlayServWindowTheme.SectionLabelStyle,
                    GUILayout.Width(82f));
                if (info == null || !info.Exists)
                {
                    GUILayout.Label(
                        "Not available",
                        PlayServWindowTheme.MetricCaptionStyle);
                    return;
                }

                GUILayout.Label(
                    $"v{info.DisplayVersion}  |  {info.DefinitionCount} definitions  |  " +
                    info.DisplayTimestamp,
                    PlayServWindowTheme.MetricCaptionStyle);
            }
        }

        private async Task ExecuteLocalAsync(
            string command,
            PlayServWindowContext context)
        {
            _localCommandRunning = true;
            SetLocalStatus($"Running {command}...", MessageType.Info, context);
            try
            {
                var result = await PlayServSchemaToolRunner.RunAsync(command);
                _response = result.Response;
                SetLocalStatus(
                    string.IsNullOrWhiteSpace(result.Message)
                        ? result.Success
                            ? "Local Schema Tool completed."
                            : "Local Schema Tool failed."
                        : result.Message,
                    result.Success ? MessageType.Info : MessageType.Warning,
                    context);
                if (result.Success &&
                    (command == "generate" || command == "init"))
                {
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception exception)
            {
                SetLocalStatus(
                    exception.GetBaseException().Message,
                    MessageType.Warning,
                    context);
            }
            finally
            {
                _localCommandRunning = false;
                context.Repaint();
            }
        }

        private void RegenerateCurrentSchema(PlayServWindowContext context)
        {
            _serverCommandRunning = true;
            try
            {
                var result =
                    SchemaCodeGenerator.GenerateModelsWithResult(useLatestSchema: false);
                SetServerStatus(
                    result.Success
                        ? $"Generated {result.GeneratedFileCount} C# model files from the current schema."
                        : result.Error,
                    result.Success ? MessageType.Info : MessageType.Warning,
                    context);
            }
            finally
            {
                _serverCommandRunning = false;
                context.Repaint();
            }
        }

        private void ToggleWatch(
            bool watching,
            PlayServWindowContext context)
        {
            var success = watching
                ? PlayServSchemaToolRunner.StopWatch(out var error)
                : PlayServSchemaToolRunner.StartWatch(out error);
            SetLocalStatus(
                success
                    ? watching
                        ? "Schema watcher stopped."
                        : "Schema watcher started."
                    : error,
                success ? MessageType.Info : MessageType.Warning,
                context);
        }

        private void CreateLauncher(PlayServWindowContext context)
        {
            var success = PlayServSchemaToolRunner.CreateIdeLaunchers(
                out var launcherPath,
                out var error);
            SetLocalStatus(
                success
                    ? $"IDE launcher created at {RelativeToProject(launcherPath)}."
                    : error,
                success ? MessageType.Info : MessageType.Warning,
                context);
        }

        private void SetLocalStatus(
            string message,
            MessageType type,
            PlayServWindowContext context)
        {
            _localStatus = message ?? string.Empty;
            _localStatusType = type;
            context.Repaint();
        }

        private void SetServerStatus(
            string message,
            MessageType type,
            PlayServWindowContext context)
        {
            _serverStatus = message ?? string.Empty;
            _serverStatusType = type;
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
                    StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(PlayServSchemaToolRunner.ProjectRoot.Length)
                        .TrimStart(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)
                    : path;
            }
            catch
            {
                return path;
            }
        }
    }
}
