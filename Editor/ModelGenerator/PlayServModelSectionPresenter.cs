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
                "Generate schemas from local C# contracts or download the server schema and generate Unity models.");

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
            GUILayout.Label("Server schema", PlayServWindowTheme.MiniHeadingStyle);
            GUILayout.Label(
                "PlayServ Schema API  →  downloaded JSON Schema  →  generated Unity C# models",
                PlayServWindowTheme.SectionSubtitleStyle);
            GUILayout.Space(8f);

            var current =
                PlayServServerSchemaWorkflow.ReadCurrent(out var currentError);
            var latest =
                PlayServServerSchemaWorkflow.ReadLatest(out var latestError);
            var comparison =
                PlayServServerSchemaWorkflow.Compare(current, latest);

            using (new EditorGUILayout.VerticalScope(
                       PlayServWindowTheme.LogContainerStyle))
            {
                DrawSchemaInfo("Current", current);
                GUILayout.Space(4f);
                DrawSchemaInfo("Downloaded", latest);
            }

            if (!string.IsNullOrWhiteSpace(currentError))
                PlayServWindowChrome.DrawNotice(currentError, MessageType.Warning);
            if (!string.IsNullOrWhiteSpace(latestError))
                PlayServWindowChrome.DrawNotice(latestError, MessageType.Warning);

            DrawComparisonNotice(comparison);
            if (!string.IsNullOrWhiteSpace(_serverStatus))
            {
                GUILayout.Space(6f);
                PlayServWindowChrome.DrawNotice(_serverStatus, _serverStatusType);
            }

            var canAccessServer = TryResolveServerAccess(
                context,
                out var clientToken,
                out var accessError);
            if (!canAccessServer)
            {
                GUILayout.Space(6f);
                PlayServWindowChrome.DrawNotice(accessError, MessageType.Warning);
            }

            var latestIsValid =
                latest.Exists && string.IsNullOrWhiteSpace(latestError);
            var currentIsValid =
                current.Exists && string.IsNullOrWhiteSpace(currentError);
            var generationUsesLatest = latestIsValid;
            var canGenerate = latestIsValid || currentIsValid;
            var generateLabel = generationUsesLatest
                ? comparison == PlayServServerSchemaComparison.UpToDate
                    ? "Regenerate C# Models"
                    : "Apply & Generate C#"
                : "Regenerate Current C#";

            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(
                       _serverCommandRunning ||
                       _localCommandRunning ||
                       PlayServCompanionPackageManager.IsBusy))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canAccessServer))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                _serverCommandRunning
                                    ? "Downloading..."
                                    : "Download Latest",
                                PlayServWindowButtonTone.Secondary,
                                GUILayout.Width(154f),
                                GUILayout.Height(32f)))
                        {
                            _ = DownloadServerSchemaAsync(
                                clientToken,
                                context);
                        }
                    }

                    GUILayout.Space(6f);
                    using (new EditorGUI.DisabledScope(!canGenerate))
                    {
                        if (PlayServWindowChrome.DrawActionButton(
                                generateLabel,
                                PlayServWindowButtonTone.Primary,
                                GUILayout.Width(184f),
                                GUILayout.Height(32f)))
                        {
                            ApplyServerSchema(
                                generationUsesLatest,
                                comparison,
                                context);
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

        private static void DrawComparisonNotice(
            PlayServServerSchemaComparison comparison)
        {
            switch (comparison)
            {
                case PlayServServerSchemaComparison.NoCurrentSchema:
                    GUILayout.Space(6f);
                    PlayServWindowChrome.DrawNotice(
                        "A server schema is downloaded and ready to generate the initial C# models.",
                        MessageType.Warning);
                    break;
                case PlayServServerSchemaComparison.UpToDate:
                    GUILayout.Space(6f);
                    PlayServWindowChrome.DrawNotice(
                        "Current models use the downloaded server schema.",
                        MessageType.Info);
                    break;
                case PlayServServerSchemaComparison.Different:
                    GUILayout.Space(6f);
                    PlayServWindowChrome.DrawNotice(
                        "The downloaded server schema differs from the current schema. Review and apply it to regenerate C#.",
                        MessageType.Warning);
                    break;
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

        private async Task DownloadServerSchemaAsync(
            string clientToken,
            PlayServWindowContext context)
        {
            _serverCommandRunning = true;
            SetServerStatus(
                "Downloading the latest server schema...",
                MessageType.Info,
                context);
            try
            {
                var downloaded = await SchemaLoader.LoadSchema(clientToken);
                if (!downloaded)
                {
                    SetServerStatus(
                        "Server schema download failed. See the Unity Console for details.",
                        MessageType.Warning,
                        context);
                    return;
                }

                SchemaCodeGenerator.CheckNewVersionJsonSchema();
                var latest =
                    PlayServServerSchemaWorkflow.ReadLatest(out var error);
                SetServerStatus(
                    string.IsNullOrWhiteSpace(error)
                        ? $"Downloaded server schema v{latest.DisplayVersion}. Review it before generating C#."
                        : error,
                    string.IsNullOrWhiteSpace(error)
                        ? MessageType.Info
                        : MessageType.Warning,
                    context);
            }
            catch (Exception exception)
            {
                SetServerStatus(
                    exception.GetBaseException().Message,
                    MessageType.Warning,
                    context);
            }
            finally
            {
                _serverCommandRunning = false;
                context.Repaint();
            }
        }

        private void ApplyServerSchema(
            bool useLatestSchema,
            PlayServServerSchemaComparison comparison,
            PlayServWindowContext context)
        {
            if (useLatestSchema &&
                comparison != PlayServServerSchemaComparison.UpToDate &&
                !EditorUtility.DisplayDialog(
                    "Apply server schema",
                    "The downloaded server schema will become the current schema and Unity C# models will be regenerated. Continue?",
                    "Apply and Generate",
                    "Cancel"))
            {
                return;
            }

            _serverCommandRunning = true;
            try
            {
                var result =
                    SchemaCodeGenerator.GenerateModelsWithResult(useLatestSchema);
                SetServerStatus(
                    result.Success
                        ? $"Generated {result.GeneratedFileCount} C# model files from " +
                          (useLatestSchema
                              ? "the downloaded server schema."
                              : "the current server schema.")
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

        private static bool TryResolveServerAccess(
            PlayServWindowContext context,
            out string clientToken,
            out string error)
        {
            clientToken = string.Empty;
            error = string.Empty;

            try
            {
                PlayServSettings settings;
                if (context.Config != null)
                {
                    settings =
                        PlayServSettingsResolver.ResolveEditorSettings(
                            context.Config);
                }
                else if (!PlayServPackageDefaultsProvider.TryLoadSettings(
                             out settings))
                {
                    error =
                        "Create PlayServ Config before downloading a server schema.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(settings.SchemaApiServerAddress))
                {
                    error =
                        "Schema API Server is not configured for the selected environment.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(settings.ClientToken))
                {
                    error =
                        "Set a public Client Token (pk_*) in PlayServ Config before downloading a server schema.";
                    return false;
                }

                clientToken = settings.ClientToken.Trim();
                if (!clientToken.StartsWith("pk_", StringComparison.Ordinal) ||
                    clientToken.IndexOf('\r') >= 0 ||
                    clientToken.IndexOf('\n') >= 0)
                {
                    clientToken = string.Empty;
                    error =
                        "Server schema download accepts only a public pk_* Client Token.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                return false;
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
