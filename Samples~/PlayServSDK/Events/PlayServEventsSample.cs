using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playserv.Events;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Subscription and unsupported-publish behavior for the PlayServ events API.
    /// </summary>
    public sealed class PlayServEventsSample : MonoBehaviour
    {

        [Header("Message")]
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
        private bool _isGroupJoined;

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
                _subscription = PlayServEvents.Subscribe<SampleChatEvent>(OnEventReceived);
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

        [ContextMenu("Join Group")]
        public void JoinGroup()
        {
            _ = JoinGroupAsync();
        }

        [ContextMenu("Leave Group")]
        public void LeaveGroup()
        {
            _ = LeaveGroupAsync();
        }

        [ContextMenu("Publish Global")]
        public void PublishGlobal()
        {
            ReportUnsupportedPublish(() => PlayServEvents.Publish(BuildEvent()));
        }

        [ContextMenu("Publish Group")]
        public void PublishGroup()
        {
            ReportUnsupportedPublish(() => PlayServEvents.PublishForGroup(groupName, BuildEvent()));
        }

        [ContextMenu("Publish User")]
        public void PublishUser()
        {
            ReportUnsupportedPublish(() => PlayServEvents.PublishForUser(targetUserId, BuildEvent()));
        }

        private void ReportUnsupportedPublish(Action publish)
        {
            try
            {
                publish();
            }
            catch (PlayServEventPublishingException exception)
            {
                _status = exception.UnifiedError.SourceCode;
                AddMessage(exception.Message);
            }
        }

        private SampleChatEvent BuildEvent()
        {
            return new SampleChatEvent
            {
                SenderId = PlayServAuth.PlayerId,
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
            _isGroupJoined = false;
            _status = "SDK disconnected";
            AddMessage(_status);
        }

        private async Task JoinGroupAsync()
        {
            if (PlayServ.State != Playserv.Proxy.Common.PlayServState.Online)
            {
                _status = "Connect SDK first";
                AddMessage(_status);
                return;
            }

            if (_isGroupJoined)
            {
                _status = $"Already joined group '{groupName}'";
                AddMessage(_status);
                return;
            }

            try
            {
                var joined = await PlayServEvents.SubscribeGroupAsync(groupName);
                _isGroupJoined = joined;
                _status = joined
                    ? $"Joined group '{groupName}'"
                    : $"Join group returned false for '{groupName}'";
                AddMessage(_status);

                if (_subscription == null)
                    AddMessage("Note: group membership alone is not enough. Subscribe to SampleChatEvent to receive group events.");
            }
            catch (Playserv.Events.PlayServGroupSubscriptionException ex)
            {
                _status = $"Group subscription refused: {ex.UnifiedError.SourceCode} ({ex.UnifiedError.TransportCode:D5}).";
                AddMessage(_status);
            }
            catch (Exception ex)
            {
                _status = $"Join group failed: {ex.Message}";
                AddMessage(_status);
            }
        }

        private async Task LeaveGroupAsync()
        {
            if (PlayServ.State != Playserv.Proxy.Common.PlayServState.Online)
            {
                _isGroupJoined = false;
                _status = "SDK is offline. Local group state cleared.";
                AddMessage(_status);
                return;
            }

            if (!_isGroupJoined)
            {
                _status = $"Group '{groupName}' is not joined";
                AddMessage(_status);
                return;
            }

            try
            {
                var left = await PlayServEvents.UnsubscribeGroupAsync(groupName);
                if (left)
                    _isGroupJoined = false;

                _status = left
                    ? $"Left group '{groupName}'"
                    : $"Leave group returned false for '{groupName}'";
                AddMessage(_status);
            }
            catch (Exception ex)
            {
                _status = $"Leave group failed: {ex.Message}";
                AddMessage(_status);
            }
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
            GUILayout.Label("How to use: 1) Connect SDK. 2) Subscribe to SampleChatEvent. 3) Join Group for group routing. 4) Publish Global/Group/User and watch logs.");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"Event subscription: {(_subscription != null ? "Active" : "Inactive")}");
            GUILayout.Label($"Group '{groupName}': {(_isGroupJoined ? "Joined" : "Not joined")}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect SDK"))
                _ = ConnectSdkAsync();
            if (GUILayout.Button("Disconnect SDK"))
                DisconnectSdk();
            if (GUILayout.Button(_showInfo ? "Hide Info" : "Info"))
                _showInfo = !_showInfo;
            GUILayout.EndHorizontal();

            if (_showInfo)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Info");
                GUILayout.Label("Purpose: Demonstrates event-based communication via PlayServ events.");
                GUILayout.Label("Server behavior: group events require both event-type subscription and explicit group join.");
                GUILayout.Label("How to use: Connect, Subscribe, Join Group, then publish Group events and inspect received messages.");
                GUILayout.Label("Use in your game: chat rooms, lobbies, clans, team channels, and user-targeted notifications.");
                GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Subscribe"))
                Subscribe();
            if (GUILayout.Button("Unsubscribe"))
                Unsubscribe();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Join Group"))
                JoinGroup();
            if (GUILayout.Button("Leave Group"))
                LeaveGroup();
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
