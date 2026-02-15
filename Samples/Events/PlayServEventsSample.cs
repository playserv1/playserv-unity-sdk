using System;
using System.Collections.Generic;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Simple publish/subscribe sample for PlayServ events API.
    /// </summary>
    public sealed class PlayServEventsSample : MonoBehaviour
    {
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
                SenderId = senderId,
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

        private void AddMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _messages.Add(line);

            if (_messages.Count > 24)
                _messages.RemoveAt(0);

            _messagesScroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            var areaWidth = Mathf.Clamp(Screen.width - 20f, 320f, 520f);
            var areaHeight = Mathf.Clamp(Screen.height - 210f, 170f, 320f);
            var areaY = Mathf.Clamp(200f, 10f, Screen.height - areaHeight - 10f);

            GUILayout.BeginArea(new Rect(10f, areaY, areaWidth, areaHeight), GUI.skin.box);
            GUILayout.Label("PlayServ Events Sample");
            GUILayout.Label($"State: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

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
            GUILayout.Label($"Recent messages ({_messages.Count}):");

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
