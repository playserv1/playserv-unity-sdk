#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServModuleSettingsPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.HeroCardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("SDK module settings", PlayServWindowTheme.HeroTitleStyle);
                    GUILayout.FlexibleSpace();

                    if (PlayServWindowChrome.DrawActionButton("Back", PlayServWindowButtonTone.Ghost, GUILayout.Width(92f), GUILayout.Height(30f)))
                    {
                        context.State.ShowModuleSettingsLayer = false;
                        context.State.MainScrollPos = Vector2.zero;
                        context.Repaint();
                    }
                }

                GUILayout.Space(6f);
                GUILayout.Label(
                    "Core runtime, transport, serialization, and config stay enabled. Runtime toggles write scripting defines, so disabled module APIs are removed at compile time.",
                    PlayServWindowTheme.HeroBodyStyle);
            }

            GUILayout.Space(12f);

            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardStyle))
            {
                GUILayout.Label("Runtime modules", PlayServWindowTheme.MiniHeadingStyle);
                GUILayout.Space(8f);

                var settings = context.State.ModuleSettings;
                var changed = false;
                var hasRuntimeModules = false;

                foreach (var module in PlayServEditorModuleAvailability.AvailableRuntimeModules)
                {
                    if (hasRuntimeModules)
                        GUILayout.Space(6f);

                    changed |= DrawRuntimeModule(settings, module);
                    hasRuntimeModules = true;
                }

                if (!hasRuntimeModules)
                    PlayServWindowChrome.DrawNotice("No optional runtime modules are installed in this SDK package.", MessageType.Info);

                if (PlayServEditorModuleAvailability.HasAnyEditorTool)
                {
                    GUILayout.Space(12f);
                    GUILayout.Label("Editor tools", PlayServWindowTheme.MiniHeadingStyle);
                    GUILayout.Space(8f);

                    if (PlayServEditorModuleAvailability.EditorDeployment)
                    {
                        using (new EditorGUI.DisabledScope(context.State.DeployRunning || context.State.VersionSyncRunning))
                        {
                            changed |= DrawToggleModule(
                                "Deployment",
                                "Release ZIP preview, version sync, and deploy controls.",
                                settings.Deployment,
                                settings.SetDeployment);
                        }

                        if (context.State.DeployRunning || context.State.VersionSyncRunning)
                            PlayServWindowChrome.DrawNotice("Deployment module cannot be hidden while deployment/version sync is running.", MessageType.Info);
                    }

                    if (PlayServEditorModuleAvailability.EditorModelSync)
                    {
                        GUILayout.Space(6f);
                        changed |= DrawToggleModule(
                            "Model Sync",
                            "Schema update checks and generated model refresh controls.",
                            settings.ModelSync,
                            settings.SetModelSync);
                    }

                    if (PlayServEditorModuleAvailability.EditorCodegen)
                    {
                        GUILayout.Space(6f);
                        changed |= DrawToggleModule(
                            "DTO Codegen",
                            "Shared DTO generation and cleanup controls.",
                            settings.Codegen,
                            settings.SetCodegen);
                    }
                }

                GUILayout.Space(12f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        PlayServWindowChrome.DrawNotice(
                            "Changing runtime modules updates Player Settings scripting defines and triggers a Unity script reload. If a module is disabled, its public SDK types are intentionally unavailable to gameplay code.",
                            MessageType.Info);
                    }

                    GUILayout.Space(12f);

                    if (PlayServWindowChrome.DrawActionButton("Reset Defaults", PlayServWindowButtonTone.Secondary, GUILayout.Width(130f), GUILayout.Height(30f)))
                    {
                        settings.ResetToDefaults();
                        changed = true;
                    }
                }

                if (changed)
                {
                    settings.Load();
                    context.Repaint();
                }
            }
        }

        private static bool DrawToggleModule(string title, string description, bool enabled, System.Func<bool, bool> apply)
        {
            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardBodyStyle))
            {
                var nextEnabled = EditorGUILayout.ToggleLeft(title, enabled);
                GUILayout.Space(2f);
                GUILayout.Label(description, PlayServWindowTheme.SectionSubtitleStyle);

                if (nextEnabled != enabled)
                    return apply(nextEnabled);
            }

            return false;
        }

        private static bool DrawRuntimeModule(
            PlayServEditorModuleSettings settings,
            PlayServEditorModuleAvailability.PlayServRuntimeModuleDefinition module)
        {
            var title = module.Name;
            var enabled = module.IsEnabled(settings);

            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardBodyStyle))
            {
                var canChange = settings.CanChangeRuntimeModule(title);
                var blockReason = settings.GetRuntimeModuleBlockReason(title);
                bool nextEnabled;

                using (new EditorGUI.DisabledScope(!canChange))
                {
                    nextEnabled = EditorGUILayout.ToggleLeft(title, enabled);
                }

                GUILayout.Space(2f);
                GUILayout.Label(module.Description, PlayServWindowTheme.SectionSubtitleStyle);
                DrawModuleTags(settings, "Requires", module.Dependencies);
                DrawModuleTags(settings, "Required by", module.Dependents);

                if (!string.IsNullOrEmpty(blockReason))
                {
                    GUILayout.Space(4f);
                    GUILayout.Label(blockReason, PlayServWindowTheme.SectionSubtitleStyle);
                }

                if (canChange && nextEnabled != enabled)
                    return module.SetEnabled(settings, nextEnabled);
            }

            return false;
        }

        private static void DrawModuleTags(PlayServEditorModuleSettings settings, string label, string[] moduleNames)
        {
            if (moduleNames == null || moduleNames.Length == 0)
                return;

            if (!HasAvailableModuleTag(moduleNames))
                return;

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, PlayServWindowTheme.MiniHeadingStyle, GUILayout.Width(72f));

                for (var i = 0; i < moduleNames.Length; i++)
                {
                    var moduleName = moduleNames[i];
                    if (!PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(moduleName))
                        continue;

                    var moduleEnabled = settings.IsRuntimeModuleEnabled(moduleName);
                    var style = ResolveModuleTagStyle(moduleEnabled, label);
                    GUILayout.Label(
                        $"{moduleName}: {(moduleEnabled ? "On" : "Off")}",
                        style,
                        GUILayout.Height(22f));

                    GUILayout.Space(4f);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private static bool HasAvailableModuleTag(string[] moduleNames)
        {
            for (var i = 0; i < moduleNames.Length; i++)
            {
                if (PlayServEditorModuleAvailability.IsRuntimeModuleAvailable(moduleNames[i]))
                    return true;
            }

            return false;
        }

        private static GUIStyle ResolveModuleTagStyle(bool moduleEnabled, string tagGroupLabel)
        {
            if (moduleEnabled && string.Equals(tagGroupLabel, "Required by", System.StringComparison.Ordinal))
                return PlayServWindowTheme.ModuleTagBlockedStyle;

            return moduleEnabled
                ? PlayServWindowTheme.ModuleTagEnabledStyle
                : PlayServWindowTheme.ModuleTagDisabledStyle;
        }
    }
}
#endif
