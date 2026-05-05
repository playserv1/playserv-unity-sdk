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

                changed |= DrawRuntimeModule(
                    settings,
                    PlayServEditorModuleSettings.RuntimeModuleEvents,
                    "Typed publish/subscribe runtime. Cannot be disabled while dependent modules are enabled.",
                    settings.RuntimeEvents,
                    settings.SetRuntimeEvents,
                    dependencies: null,
                    dependents: new[]
                    {
                        PlayServEditorModuleSettings.RuntimeModuleData,
                        PlayServEditorModuleSettings.RuntimeModuleSpawn
                    });

                GUILayout.Space(6f);
                changed |= DrawRuntimeModule(
                    settings,
                    PlayServEditorModuleSettings.RuntimeModuleData,
                    "Shared entity query, mutation, polling, and transport subscription APIs. Depends on Events.",
                    settings.RuntimeData,
                    settings.SetRuntimeData,
                    dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleEvents },
                    dependents: null);

                GUILayout.Space(6f);
                changed |= DrawRuntimeModule(
                    settings,
                    PlayServEditorModuleSettings.RuntimeModuleRpc,
                    "RPC command DTOs, local invoker, generated RPC helpers, and Invoke* wrapper APIs.",
                    settings.RuntimeRpc,
                    settings.SetRuntimeRpc,
                    dependencies: null,
                    dependents: null);

                GUILayout.Space(6f);
                changed |= DrawRuntimeModule(
                    settings,
                    PlayServEditorModuleSettings.RuntimeModuleSpawn,
                    "NetworkObject/NetworkTransform helpers and Resources-based spawn facade. Depends on Events.",
                    settings.RuntimeSpawn,
                    settings.SetRuntimeSpawn,
                    dependencies: new[] { PlayServEditorModuleSettings.RuntimeModuleEvents },
                    dependents: null);

                GUILayout.Space(6f);
                changed |= DrawRuntimeModule(
                    settings,
                    PlayServEditorModuleSettings.RuntimeModulePulse,
                    "Realtime config placeholder module and future feature flag surface.",
                    settings.RuntimePulse,
                    settings.SetRuntimePulse,
                    dependencies: null,
                    dependents: null);

                GUILayout.Space(10f);
                PlayServWindowChrome.DrawNotice(
                    "Changing runtime modules updates Player Settings scripting defines and triggers a Unity script reload. If a module is disabled, its public SDK types are intentionally unavailable to gameplay code.",
                    MessageType.Info);

                GUILayout.Space(12f);
                GUILayout.Label("Editor tools", PlayServWindowTheme.MiniHeadingStyle);
                GUILayout.Space(8f);

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

                GUILayout.Space(6f);
                changed |= DrawToggleModule(
                    "Model Sync",
                    "Schema update checks and generated model refresh controls.",
                    settings.ModelSync,
                    settings.SetModelSync);

                GUILayout.Space(6f);
                changed |= DrawToggleModule(
                    "Events API",
                    "Typed event API generation controls. Hidden automatically when runtime Events are disabled.",
                    settings.Events,
                    settings.SetEvents);

                GUILayout.Space(6f);
                changed |= DrawToggleModule(
                    "DTO Codegen",
                    "Shared DTO generation and cleanup controls.",
                    settings.Codegen,
                    settings.SetCodegen);

                GUILayout.Space(12f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

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
            string title,
            string description,
            bool enabled,
            System.Func<bool, bool> apply,
            string[] dependencies,
            string[] dependents)
        {
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
                GUILayout.Label(description, PlayServWindowTheme.SectionSubtitleStyle);
                DrawModuleTags(settings, "Requires", dependencies);
                DrawModuleTags(settings, "Required by", dependents);

                if (!string.IsNullOrEmpty(blockReason))
                {
                    GUILayout.Space(4f);
                    GUILayout.Label(blockReason, PlayServWindowTheme.SectionSubtitleStyle);
                }

                if (canChange && nextEnabled != enabled)
                    return apply(nextEnabled);
            }

            return false;
        }

        private static void DrawModuleTags(PlayServEditorModuleSettings settings, string label, string[] moduleNames)
        {
            if (moduleNames == null || moduleNames.Length == 0)
                return;

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, PlayServWindowTheme.MiniHeadingStyle, GUILayout.Width(72f));

                for (var i = 0; i < moduleNames.Length; i++)
                {
                    var moduleName = moduleNames[i];
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
