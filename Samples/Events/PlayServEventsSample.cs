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

            _subscription = PlayServ.Subscribe<SampleChatEvent>(OnEventReceived);
            _status = "Subscribed";
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
        }

        [ContextMenu("Publish Group")]
        public void PublishGroup()
        {
            PlayServ.PublishForGroup(groupName, BuildEvent());
            _status = $"Published group event ({groupName})";
        }

        [ContextMenu("Publish User")]
        public void PublishUser()
        {
            PlayServ.PublishForUser(targetUserId, BuildEvent());
            _status = $"Published user event ({targetUserId})";
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
            _messages.Add(line);

            if (_messages.Count > 8)
                _messages.RemoveAt(0);

            _status = $"Received event at {evt.SentAtUnixMs}";
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            GUILayout.BeginArea(new Rect(10f, 200f, 520f, 240f), GUI.skin.box);
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
            GUILayout.Label("Recent messages:");
            foreach (var line in _messages)
                GUILayout.Label($"- {line}");
            GUILayout.EndArea();
        }
    }
}
