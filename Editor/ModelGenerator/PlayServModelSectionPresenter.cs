using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.ModelGenerator.Editor;

namespace Playserv.Editor
{
    internal sealed class PlayServModelSectionPresenter : IPlayServEditorSection
    {
        public void Dispose()
        {
        }

        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldModel,
                "Schema",
                "Model Sync",
                "Check the latest schema, compare timestamps, and regenerate editor-side models from the current source of truth.");

            if (expanded)
            {
                GUILayout.Space(6f);

                string currentVersion = EditorPrefs.GetString(Const.PrefKeyJsonSchemaVersion, string.Empty);
                string currentTimestampRaw = EditorPrefs.GetString(Const.PrefKeyJsonSchemaTimestamp, string.Empty);

                string latestVersion = EditorPrefs.GetString(Const.PrefKeyJsonSchemaLatestVersion, string.Empty);
                string latestTimestampRaw = EditorPrefs.GetString(Const.PrefKeyJsonSchemaLatestTimestamp, string.Empty);

                bool hasCurrent = !string.IsNullOrEmpty(currentVersion) || !string.IsNullOrEmpty(currentTimestampRaw);
                bool hasLatest = state.ShowAvailableSchemaInfo &&
                                 (!string.IsNullOrEmpty(latestVersion) || !string.IsNullOrEmpty(latestTimestampRaw));

                bool differs = false;
                bool hasComparableTimestamps = false;
                bool latestIsNewerByTimestamp = false;

                if (hasLatest &&
                    TryParseTimestamp(currentTimestampRaw, out var currentTs) &&
                    TryParseTimestamp(latestTimestampRaw, out var latestTs))
                {
                    hasComparableTimestamps = true;
                    latestIsNewerByTimestamp = latestTs > currentTs;
                }

                if (hasLatest)
                {
                    differs = hasComparableTimestamps
                        ? latestIsNewerByTimestamp
                        : !string.IsNullOrEmpty(latestTimestampRaw) && latestTimestampRaw != currentTimestampRaw;
                }

                string statusMessage = null;
                MessageType? statusType = null;

                if (hasLatest)
                {
                    if (differs)
                    {
                        statusMessage = "A newer schema is available.";
                        statusType = MessageType.Warning;
                    }
                    else
                    {
                        statusMessage = "You have the latest schema already.";
                        statusType = MessageType.Info;
                    }
                }

                if (hasCurrent || hasLatest)
                {
                    if (statusMessage != null && statusType.HasValue)
                        PlayServWindowChrome.DrawNotice(statusMessage, statusType.Value);

                    EditorGUILayout.LabelField("Schema details", PlayServWindowTheme.MiniHeadingStyle);

                    EditorGUILayout.BeginVertical(PlayServWindowTheme.LogContainerStyle);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(" ", GUILayout.Width(14f));
                    EditorGUILayout.LabelField("Current schema", PlayServWindowTheme.SectionLabelStyle);
                    if (state.ShowAvailableSchemaInfo)
                        EditorGUILayout.LabelField("Latest available schema", PlayServWindowTheme.SectionLabelStyle);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("V", GUILayout.Width(14f));
                    EditorGUILayout.SelectableLabel(
                        string.IsNullOrEmpty(currentVersion) ? "—" : currentVersion,
                        PlayServWindowTheme.InputStyle,
                        GUILayout.Height(context.StyledFieldHeight));

                    if (state.ShowAvailableSchemaInfo)
                    {
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(latestVersion) ? "—" : latestVersion,
                            PlayServWindowTheme.InputStyle,
                            GUILayout.Height(context.StyledFieldHeight));
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("T", GUILayout.Width(14f));
                    EditorGUILayout.SelectableLabel(
                        string.IsNullOrEmpty(currentTimestampRaw) ? "—" : FormatTimestamp(currentTimestampRaw),
                        PlayServWindowTheme.InputStyle,
                        GUILayout.Height(context.StyledFieldHeight));

                    if (state.ShowAvailableSchemaInfo)
                    {
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(latestTimestampRaw) ? "—" : FormatTimestamp(latestTimestampRaw),
                            PlayServWindowTheme.InputStyle,
                            GUILayout.Height(context.StyledFieldHeight));
                    }

                    EditorGUILayout.EndHorizontal();

                    if (state.ShowAvailableSchemaInfo)
                    {
                        GUILayout.Space(6f);

                        EditorGUILayout.BeginHorizontal();

                        if (PlayServWindowChrome.DrawActionButton("Hide", PlayServWindowButtonTone.Ghost, GUILayout.Width(120f), GUILayout.Height(28f)))
                            state.ShowAvailableSchemaInfo = false;

                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(!differs))
                        {
                            if (PlayServWindowChrome.DrawActionButton("Apply New Schema", PlayServWindowButtonTone.Primary, GUILayout.Width(180f), GUILayout.Height(28f)))
                            {
                                if (EditorUtility.DisplayDialog(
                                        "Apply new schema",
                                        "This will replace the current schema with the latest downloaded one.\nContinue?",
                                        "Apply",
                                        "Cancel"))
                                {
                                    SchemaCodeGenerator.GenerateModels(true);
                                    state.ShowAvailableSchemaInfo = false;
                                    EditorPrefs.DeleteKey(Const.PrefKeyJsonSchemaLatestVersion);
                                    EditorPrefs.DeleteKey(Const.PrefKeyJsonSchemaLatestTimestamp);
                                }
                            }
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndVertical();

                    GUILayout.Space(6f);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (PlayServWindowChrome.DrawActionButton("Check Updates", PlayServWindowButtonTone.Secondary, GUILayout.Width(138f), GUILayout.Height(32f)))
                    {
                        ClearLatestSchemaInfo(state);
                        _ = CheckSchemaUpdatesAsync(context);
                    }

                    GUILayout.Space(8f);

                    if (PlayServWindowChrome.DrawActionButton("Re-generate Models", PlayServWindowButtonTone.Primary, GUILayout.Width(176f), GUILayout.Height(32f)))
                        SchemaCodeGenerator.GenerateModels(false);
                }
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldModel, state.FoldModel);
        }

        private static string FormatTimestamp(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            if (DateTimeOffset.TryParse(raw, out var dto))
                return dto.ToLocalTime().DateTime.ToString("yyyy-MM-dd HH:mm:ss");

            return raw;
        }

        private static bool TryParseTimestamp(string raw, out DateTimeOffset value)
        {
            if (string.IsNullOrEmpty(raw))
            {
                value = default;
                return false;
            }

            return DateTimeOffset.TryParse(raw, out value);
        }

        private static async Task CheckSchemaUpdatesAsync(PlayServWindowContext context)
        {
            try
            {
                var token = ResolveSchemaCredential(context);

                if (await SchemaLoader.LoadSchema(token))
                {
                    SchemaLoader.CheckNewSchema();
                    context.State.ShowAvailableSchemaInfo = true;
                }
                else
                {
                    ClearLatestSchemaInfo(context.State);
                }
            }
            catch (Exception e)
            {
                ClearLatestSchemaInfo(context.State);
                Debug.LogError($"[SchemaDownloader] Check updates failed: {e.Message}");
            }
            finally
            {
                context.Repaint();
            }
        }

        private static string ResolveSchemaCredential(PlayServWindowContext context)
        {
            var clientToken = context.ClientTokenProperty == null
                ? string.Empty
                : context.ClientTokenProperty.stringValue;

            if (!string.IsNullOrWhiteSpace(clientToken))
                return clientToken;

            var authorization = context.AuthorizationProperty == null
                ? string.Empty
                : context.AuthorizationProperty.stringValue;

            const string bearerPrefix = "Bearer ";
            return authorization != null && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? authorization.Substring(bearerPrefix.Length).Trim()
                : authorization;
        }

        private static void ClearLatestSchemaInfo(PlayServWindowState state)
        {
            state.ShowAvailableSchemaInfo = false;
            EditorPrefs.DeleteKey(Const.PrefKeyJsonSchemaLatestVersion);
            EditorPrefs.DeleteKey(Const.PrefKeyJsonSchemaLatestTimestamp);
        }
    }
}
