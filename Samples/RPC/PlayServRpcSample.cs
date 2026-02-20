using System;
using System.Collections.Generic;
using Playserv.Test.RPC;
using Playserv.Wrapper;
using Playserv.Proxy.Common;
using UnityEngine;

#nullable enable

namespace Playserv.Samples
{
    /// <summary>
    /// RPC sample based on TestCode NotificationService:
    /// local in-process invoke via LocalRpcInvoker and transport invoke via rpc.InvokeRpc.
    /// </summary>
    public sealed class PlayServRpcSample : MonoBehaviour
    {
        private const string NotificationServiceName = nameof(NotificationService);
        private const string BroadcastMethodName = nameof(NotificationService.BroadcastToAll);


        private readonly string messageText = "WyJIZWxsbyJd";

        [Header("Execution")]
        [SerializeField] private bool enableLocalInvokerOnEnable = false;
        [SerializeField] private bool autoSubscribeOnEnable = true;
        [SerializeField] private bool showOverlay = true;
        
        private IDisposable? _notificationSubscription;
        private readonly List<string> _history = new List<string>();
        private Vector2 _historyScroll;
        private bool _pendingAutoSubscribe;
        private string _status = "Idle";

        private void OnEnable()
        {
            PlayServ.OnTransportError += OnTransportError;

            if (autoSubscribeOnEnable)
            {
                if (PlayServ.State == PlayServState.Online)
                    SubscribeNotifications();
                else
                    _pendingAutoSubscribe = true;
            }
        }

        private void Update()
        {
            if (!_pendingAutoSubscribe)
                return;

            if (PlayServ.State != PlayServState.Online)
                return;

            _pendingAutoSubscribe = false;
            SubscribeNotifications();
        }

        private void OnDisable()
        {
            PlayServ.OnTransportError -= OnTransportError;
            UnsubscribeNotifications();
            _pendingAutoSubscribe = false;
        }

        [ContextMenu("Invoke RPC")]
        public void InvokeRpc()
        {
            try
            {
                PlayServ.Invoke(
                    NotificationServiceName,
                    BroadcastMethodName,
                    messageText);
                
                Debug.Log($"Invoke RPC: {messageText}");
                
                AddHistory($"RPC invoke sent through transport: {messageText}");
            }
            catch (Exception ex)
            {
                _status = $"RPC invoke failed: {ex.Message}";
                AddHistory(_status);
            }
        }

        [ContextMenu("Subscribe Notifications")]
        public void SubscribeNotifications()
        {
            if (_notificationSubscription != null)
            {
                _status = "Notification subscription already active";
                AddHistory(_status);
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                _status = "Subscribe requires connected SDK";
                AddHistory(_status);
                return;
            }

            _notificationSubscription = PlayServ.Subscribe<NotificationEvent>(OnNotificationReceived);
            _status = "Subscribed to NotificationEvent";
            AddHistory(_status);
        }

        [ContextMenu("Unsubscribe Notifications")]
        public void UnsubscribeNotifications()
        {
            _notificationSubscription?.Dispose();
            _notificationSubscription = null;
        }

        private void OnNotificationReceived(NotificationEvent evt)
        {
            var eventType = string.IsNullOrWhiteSpace(evt.EventType) ? "NotificationEvent" : evt.EventType;
            var message = string.IsNullOrWhiteSpace(evt.Message) ? "<empty>" : evt.Message;
            _status = $"Received server event: {eventType}";
            AddHistory($"<- {eventType}: {message}");
        }

        private void OnTransportError(TransportError error)
        {
            var details = error == null ? "Unknown transport error" : error.ToString();
            _status = $"Transport error: {details}";
            AddHistory(_status);
        }

        private void AddHistory(string line)
        {
            _history.Add(line);
            if (_history.Count > 8)
                _history.RemoveAt(0);

            _historyScroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            var areaWidth = Mathf.Clamp(Screen.width - 20f, 320f, 620f);
            var areaHeight = Mathf.Clamp(Screen.height - 430f, 180f, 260f);
            var areaY = Mathf.Clamp(420f, 10f, Screen.height - areaHeight - 10f);

            GUILayout.BeginArea(new Rect(10f, areaY, areaWidth, areaHeight), GUI.skin.box);
            GUILayout.Label("PlayServ RPC Sample");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Notification subscription: {(_notificationSubscription != null ? "Active" : "Inactive")}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Invoke RPC"))
                InvokeRpc();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Subscribe"))
                SubscribeNotifications();
            if (GUILayout.Button("Unsubscribe"))
                UnsubscribeNotifications();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"History ({_history.Count}):");
            _historyScroll = GUILayout.BeginScrollView(_historyScroll, GUILayout.ExpandHeight(true));
            if (_history.Count == 0)
            {
                GUILayout.Label("- No calls yet");
            }
            else
            {
                foreach (var line in _history)
                    GUILayout.Label($"- {line}");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}

#nullable restore
