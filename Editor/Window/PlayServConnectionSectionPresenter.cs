using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServConnectionSectionPresenter
    {
        public void Draw(PlayServWindowContext context)
        {
            var state = context.State;
            var expanded = PlayServWindowChrome.BeginSectionCard(
                ref state.FoldConnection,
                "Connection",
                "WebSocket Connection",
                "Low-level socket smoke test for editor diagnostics and message tracing.");

            if (expanded)
            {
                PlayServWindowChrome.DrawNotice("Test WebSocket connection.", MessageType.Info);

                GUILayout.Space(6f);
                EditorGUILayout.LabelField("Connection Settings", PlayServWindowTheme.MiniHeadingStyle);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Endpoint", GUILayout.Width(80f));
                string newEndpoint = EditorGUILayout.TextField(state.WebSocketEndpoint, PlayServWindowTheme.InputStyle);
                if (newEndpoint != state.WebSocketEndpoint)
                {
                    state.WebSocketEndpoint = newEndpoint;
                    EditorPrefs.SetString(Const.PrefKeyWebSocketEndpoint, state.WebSocketEndpoint);
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6f);

                bool isConnected = state.WebSocketTransport != null && state.WebSocketTransport.IsConnected;
                bool isConnecting = state.WebSocketTransport != null && state.WebSocketTransport.IsConnecting;

                EditorGUILayout.BeginHorizontal();

                using (new EditorGUI.DisabledScope(isConnected || isConnecting))
                {
                    if (PlayServWindowChrome.DrawActionButton(isConnecting ? "Connecting..." : "Connect", PlayServWindowButtonTone.Primary, GUILayout.Height(30f)))
                        _ = context.ConnectionController.ConnectAsync();
                }

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    if (PlayServWindowChrome.DrawActionButton("Disconnect", PlayServWindowButtonTone.Secondary, GUILayout.Height(30f)))
                        context.ConnectionController.Disconnect();
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6f);

                EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle);
                string statusText = isConnected ? "Connected" : isConnecting ? "Connecting..." : "Disconnected";
                var statusColor = isConnected ? Color.green : isConnecting ? Color.yellow : Color.gray;

                var previousColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField("● " + statusText, PlayServWindowTheme.StatusValueStyle);
                GUI.color = previousColor;

                GUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(!isConnected))
                {
                    EditorGUILayout.LabelField("Send Test Message", PlayServWindowTheme.MiniHeadingStyle);
                    state.TestMessage = EditorGUILayout.TextArea(state.TestMessage, PlayServWindowTheme.TextAreaStyle, GUILayout.Height(64f));

                    if (PlayServWindowChrome.DrawActionButton("Send Message", PlayServWindowButtonTone.Primary, GUILayout.Width(132f), GUILayout.Height(28f)))
                        _ = context.ConnectionController.SendTestMessageAsync();
                }

                GUILayout.Space(6f);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection Log", PlayServWindowTheme.MiniHeadingStyle);
                if (PlayServWindowChrome.DrawActionButton("Clear", PlayServWindowButtonTone.Ghost, GUILayout.Width(74f), GUILayout.Height(24f)))
                    context.ConnectionController.ClearLogs();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginVertical(PlayServWindowTheme.LogContainerStyle);
                state.ConnectionScrollPos = EditorGUILayout.BeginScrollView(state.ConnectionScrollPos, GUILayout.Height(200f));

                if (state.WebSocketTransport != null && state.WebSocketTransport.LogMessages.Count > 0)
                {
                    for (var i = 0; i < state.WebSocketTransport.LogMessages.Count; i++)
                    {
                        EditorGUILayout.SelectableLabel(
                            state.WebSocketTransport.LogMessages[i],
                            PlayServWindowTheme.LogLineStyle,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No logs yet...", PlayServWindowTheme.EmptyStateStyle);
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }

            PlayServWindowChrome.EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldConnection, state.FoldConnection);
        }
    }
}
