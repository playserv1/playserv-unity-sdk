using System.Linq;
using Playserv.Modules;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServModuleSettingsPresenter
    {
        private static readonly string[] RuntimeModuleNames =
            PlayServModuleManifest.VisibleRuntimeModules
                .Where(module => !module.IsServerModule)
                .Select(module => module.Label)
                .ToArray();

        private static readonly string[] ServerRuntimeModuleNames =
            PlayServModuleManifest.VisibleRuntimeModules
                .Where(module => module.IsServerModule)
                .Select(module => module.Label)
                .ToArray();

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
                    "Core runtime, serialization, and config stay enabled. Module toggles write scripting defines, so disabled module APIs are removed at compile time.",
                    PlayServWindowTheme.HeroBodyStyle);
            }

            GUILayout.Space(12f);

            using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.CardStyle))
            {
                var settings = context.State.ModuleSettings;
                var changed = false;
                var hasRuntimeModules = DrawRuntimeModuleGroup(
                    "Runtime",
                    RuntimeModuleNames,
                    settings,
                    ref changed);

                var hasServerRuntimeModules = DrawRuntimeModuleGroup(
                    "Server Runtime",
                    ServerRuntimeModuleNames,
                    settings,
                    ref changed,
                    hasRuntimeModules);

                if (!hasRuntimeModules && !hasServerRuntimeModules)
                    PlayServWindowChrome.DrawNotice("No optional runtime modules are installed in this SDK package.", MessageType.Info);

                if (hasRuntimeModules || hasServerRuntimeModules)
                {
                    GUILayout.Space(12f);
                    changed |= DrawRuntimeProfiles(settings);
                }

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

                    if (PlayServEditorModuleAvailability.EditorModuleStressTests)
                    {
                        GUILayout.Space(6f);
                        changed |= DrawToggleModule(
                            "Module Stress Tests",
                            "Internal delete/restore checks for optional module packaging. Excluded from SDK exports.",
                            settings.ModuleStressTests,
                            settings.SetModuleStressTests);
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

        private static bool DrawRuntimeProfiles(PlayServEditorModuleSettings settings)
        {
            var changed = false;

            GUILayout.Label("SDK profiles", PlayServWindowTheme.MiniHeadingStyle);
            GUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var profile in PlayServSdkProfiles.All)
                {
                    if (PlayServWindowChrome.DrawActionButton($"Apply {profile.Label}", PlayServWindowButtonTone.Secondary, GUILayout.Height(28f)))
                        changed |= PlayServRuntimeModuleLifecycle.ApplyProfile(settings, profile);

                    GUILayout.Space(6f);
                }

                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(2f);
            GUILayout.Label(
                "Apply Profile updates module defines, generated compatibility code, and root asmdef references before Unity reloads scripts.",
                PlayServWindowTheme.SectionSubtitleStyle);

            return changed;
        }

        private static bool DrawRuntimeModuleGroup(
            string title,
            string[] moduleNames,
            PlayServEditorModuleSettings settings,
            ref bool changed,
            bool addTopSpacing = false)
        {
            var drewAny = false;

            for (var i = 0; i < moduleNames.Length; i++)
            {
                var module = FindAvailableRuntimeModule(moduleNames[i]);
                if (module == null)
                    continue;

                if (!drewAny)
                {
                    if (addTopSpacing)
                        GUILayout.Space(14f);

                    GUILayout.Label(title, PlayServWindowTheme.MiniHeadingStyle);
                    GUILayout.Space(8f);
                }
                else
                {
                    GUILayout.Space(6f);
                }

                changed |= DrawRuntimeModule(settings, module);
                drewAny = true;
            }

            return drewAny;
        }

        private static PlayServEditorModuleAvailability.PlayServRuntimeModuleDefinition FindAvailableRuntimeModule(string moduleName)
        {
            foreach (var module in PlayServEditorModuleAvailability.AvailableRuntimeModules)
            {
                if (string.Equals(module.Name, moduleName, System.StringComparison.Ordinal))
                    return module;
            }

            return null;
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
                var canUninstall = PlayServRuntimeModuleLifecycle.CanUninstallModule(settings, module, out var uninstallBlockReason);
                bool nextEnabled;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canChange))
                    {
                        nextEnabled = EditorGUILayout.ToggleLeft(title, enabled);
                    }

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(!canUninstall))
                    {
                        if (PlayServWindowChrome.DrawActionButton("Uninstall", PlayServWindowButtonTone.Ghost, GUILayout.Width(96f), GUILayout.Height(24f)))
                        {
                            if (ConfirmUninstall(module) &&
                                !PlayServRuntimeModuleLifecycle.UninstallModule(settings, module, out var error))
                            {
                                EditorUtility.DisplayDialog(
                                    "PlayServ module uninstall failed",
                                    string.IsNullOrEmpty(error) ? "Unknown uninstall error." : error,
                                    "OK");
                            }

                            return true;
                        }
                    }
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
                else if (!canUninstall && !string.IsNullOrEmpty(uninstallBlockReason))
                {
                    GUILayout.Space(4f);
                    GUILayout.Label($"Uninstall blocked: {uninstallBlockReason}", PlayServWindowTheme.SectionSubtitleStyle);
                }

                if (canChange && nextEnabled != enabled)
                    return module.SetEnabled(settings, nextEnabled);
            }

            return false;
        }

        private static bool ConfirmUninstall(PlayServEditorModuleAvailability.PlayServRuntimeModuleDefinition module)
        {
            if (!PlayServModuleManifest.TryGet(module.Id, out var manifest))
                return false;

            var paths = string.Join("\n", manifest.AssetPaths);
            return EditorUtility.DisplayDialog(
                $"Uninstall {module.Name} module?",
                $"This will first disable {module.Name}, regenerate compatibility code, sync asmdef references, and then delete these package paths:\n\n{paths}",
                "Uninstall",
                "Cancel");
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
