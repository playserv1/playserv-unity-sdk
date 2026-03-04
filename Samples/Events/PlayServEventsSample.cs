using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Playserv.Samples
{
    /// <summary>
    /// Simple publish/subscribe sample for PlayServ events API.
    /// </summary>
    public sealed class PlayServEventsSample : MonoBehaviour
    {
        private const string SamplesSceneFileName = "Samples.unity";

        [Header("Message")]
        [SerializeField] private string senderId = "player-001";
        [SerializeField] private string messageText = "Hello from events sample";

        [Header("Targets")]
        [SerializeField] private string groupName = "demo-group";
        [SerializeField] private string targetUserId = "player-002";

        [Header("Behavior")]
        [SerializeField] private bool autoSubscribe = true;
        [SerializeField] private bool showOverlay = true;

        private readonly List<string> _messages = new List<string>();
        private Vector2 _messagesScroll;
        private IDisposable _subscription;
        private string _status = "Idle";
        private bool _showInfo;

        private void OnEnable()
        {
            if (autoSubscribe)
                Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        [ContextMenu("Subscribe")]
        public void Subscribe()
        {
            if (_subscription != null)
            {
                _status = "Already subscribed";
                AddMessage(_status);
                return;
            }

            if (PlayServ.State != Playserv.Proxy.Common.PlayServState.Online)
            {
                _status = "Connect SDK first";
                AddMessage(_status);
                return;
            }

            try
            {
                _subscription = PlayServ.Subscribe<SampleChatEvent>(OnEventReceived);
                _status = "Subscribed";
                AddMessage(_status);
            }
            catch (InvalidOperationException ex)
            {
                _status = $"Subscribe failed: {ex.Message}";
                AddMessage(_status);
            }
        }

        [ContextMenu("Unsubscribe")]
        public void Unsubscribe()
        {
            _subscription?.Dispose();
            _subscription = null;
            _status = "Unsubscribed";
            AddMessage(_status);
        }

        [ContextMenu("Publish Global")]
        public void PublishGlobal()
        {
            PlayServ.Publish(BuildEvent());
            _status = "Published global event";
            AddMessage($"Sent global: {messageText}");
        }

        [ContextMenu("Publish Group")]
        public void PublishGroup()
        {
            PlayServ.PublishForGroup(groupName, BuildEvent());
            _status = $"Published group event ({groupName})";
            AddMessage($"Sent group({groupName}): {messageText}");
        }

        [ContextMenu("Publish User")]
        public void PublishUser()
        {
            PlayServ.PublishForUser(targetUserId, BuildEvent());
            _status = $"Published user event ({targetUserId})";
            AddMessage($"Sent user({targetUserId}): {messageText}");
        }

        private SampleChatEvent BuildEvent()
        {
            return new SampleChatEvent
            {
                SenderId = PlayServ.Settings.UserId,
                Text = messageText,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private void OnEventReceived(SampleChatEvent evt)
        {
            var line = $"[{evt.SenderId}] {evt.Text}";
            AddMessage(line);
            _status = $"Received event at {evt.SentAtUnixMs}";
        }

        private async Task ConnectSdkAsync()
        {
            try
            {
                var connected = await PlayServ.Connect();
                _status = connected ? "SDK connected" : "SDK connection failed";
                AddMessage(_status);
            }
            catch (Exception ex)
            {
                _status = $"Connect error: {ex.Message}";
                AddMessage(_status);
            }
        }

        private void DisconnectSdk()
        {
            PlayServ.Disconnect();
            _status = "SDK disconnected";
            AddMessage(_status);
        }

        private void BackToSamples()
        {
            var scenePath = ResolveSamplesScenePath();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (File.Exists(scenePath))
                    EditorSceneManager.OpenScene(scenePath);
                return;
            }
#endif
            SceneManager.LoadScene(Path.GetFileNameWithoutExtension(scenePath));
        }

        private static string ResolveSamplesScenePath()
        {
            var activePath = SceneManager.GetActiveScene().path;
            var activeDirectory = Path.GetDirectoryName(activePath);
            return string.IsNullOrEmpty(activeDirectory)
                ? SamplesSceneFileName
                : Path.Combine(activeDirectory, SamplesSceneFileName).Replace('\\', '/');
        }

        private void AddMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _messages.Add(line);

            if (_messages.Count > 128)
                _messages.RemoveAt(0);

            _messagesScroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            SampleGuiFontScale.Apply();

            var margin = 10f;
            var areaWidth = Mathf.Max(320f, Screen.width - margin * 2f);
            var areaHeight = Mathf.Max(220f, Screen.height - margin * 2f);

            GUILayout.BeginArea(new Rect(margin, margin, areaWidth, areaHeight), GUI.skin.box);
            GUILayout.Label("PlayServ Events Sample");
            GUILayout.Label("How to use: 1) Configure bootstrap in 0_Samples. 2) Connect SDK. 3) Subscribe. 4) Publish Global/Group/User and watch logs.");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect SDK"))
                _ = ConnectSdkAsync();
            if (GUILayout.Button("Disconnect SDK"))
                DisconnectSdk();
            if (GUILayout.Button("Back to 0_Samples"))
                BackToSamples();
            if (GUILayout.Button(_showInfo ? "Hide Info" : "Info"))
                _showInfo = !_showInfo;
            GUILayout.EndHorizontal();

            if (_showInfo)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Info");
                GUILayout.Label("Purpose: Demonstrates event-based communication via PlayServ events.");
                GUILayout.Label("How to use: Connect, subscribe, publish Global/Group/User events, then inspect received messages.");
                GUILayout.Label("Use in your game: Chat, notifications, lobby/match broadcasts, and user-targeted messages.");
                GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Subscribe"))
                Subscribe();
            if (GUILayout.Button("Unsubscribe"))
                Unsubscribe();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Publish Global"))
                PublishGlobal();
            if (GUILayout.Button("Publish Group"))
                PublishGroup();
            if (GUILayout.Button("Publish User"))
                PublishUser();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Logs ({_messages.Count}):");

            _messagesScroll = GUILayout.BeginScrollView(_messagesScroll, GUILayout.ExpandHeight(true));
            if (_messages.Count == 0)
            {
                GUILayout.Label("- No messages yet");
            }
            else
            {
                foreach (var line in _messages)
                    GUILayout.Label($"- {line}");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
