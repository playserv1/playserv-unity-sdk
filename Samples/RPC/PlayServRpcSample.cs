using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Test.RPC;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

#nullable enable

namespace Playserv.Samples
{
    /// <summary>
    /// RPC sample based on TestCode NotificationService:
    /// transport invoke via rpc.InvokeRpc and response tracking via NotificationEvent.
    /// </summary>
    public sealed class PlayServRpcSample : MonoBehaviour
    {
        private const string SamplesSceneFileName = "Samples.unity";
        private const string NotificationServiceName = nameof(NotificationService);
        private const string BroadcastMethodName = nameof(NotificationService.BroadcastToAll);

        [Header("Message")]
        [SerializeField] private string messageText = "Hello from RPC sample";

        [Header("Execution")]
        [SerializeField] private bool enableLocalInvokerOnEnable = false;
        [SerializeField] private bool autoSubscribeOnEnable = true;
        [SerializeField] private bool showOverlay = true;

        private IDisposable? _notificationSubscription;
        private readonly List<string> _history = new List<string>();
        private Vector2 _historyScroll;
        private bool _pendingAutoSubscribe;
        private string _status = "Idle";
        private bool _showInfo;

        private void OnEnable()
        {
            PlayServ.OnTransportError += OnTransportError;
            PlayServ.OnRpcInvokeResponse += OnRpcInvokeResponse;

            if (enableLocalInvokerOnEnable)
                AddHistory("Local invoker toggle is deprecated in this sample; transport RPC is used.");

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
            PlayServ.OnRpcInvokeResponse -= OnRpcInvokeResponse;
            UnsubscribeNotifications();
            _pendingAutoSubscribe = false;
        }

        [ContextMenu("Invoke RPC (Expression)")]
        public void InvokeRpcExpression()
        {
            try
            {
                PlayServ.Invoke<NotificationService>(x => x.BroadcastToAll(messageText));
                _status = "RPC expression invoke sent";
                AddHistory($"-> expr {NotificationServiceName}.{BroadcastMethodName}(message={messageText})");
            }
            catch (Exception ex)
            {
                _status = $"RPC invoke failed: {ex.Message}";
                AddHistory(_status);
            }
        }

        [ContextMenu("Invoke RPC (Args)")]
        public void InvokeRpcArgs()
        {
            try
            {
                PlayServ.InvokeArgs(NotificationServiceName, BroadcastMethodName, messageText);
                _status = "RPC args invoke sent";
                AddHistory($"-> args {NotificationServiceName}.{BroadcastMethodName}([message={messageText}])");
            }
            catch (Exception ex)
            {
                _status = $"RPC args invoke failed: {ex.Message}";
                AddHistory(_status);
            }
        }

        [ContextMenu("Invoke RPC (Named)")]
        public void InvokeRpcNamed()
        {
            try
            {
                PlayServ.InvokeNamed(
                    NotificationServiceName,
                    BroadcastMethodName,
                    new Dictionary<string, object>
                    {
                        ["message"] = messageText
                    });

                _status = "RPC named invoke sent";
                AddHistory($"-> named {NotificationServiceName}.{BroadcastMethodName}({{ message = {messageText} }})");
            }
            catch (Exception ex)
            {
                _status = $"RPC named invoke failed: {ex.Message}";
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
            AddHistory("Unsubscribed NotificationEvent.");
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

        private void OnRpcInvokeResponse(InvokeRpcResponse response)
        {
            var requestInfo = response.Request == null
                ? "n/a"
                : $"{response.Request.ServiceName}.{response.Request.MethodName}";
            var message = string.IsNullOrWhiteSpace(response.Message) ? "<empty>" : response.Message;
            var result = string.IsNullOrWhiteSpace(response.Result) ? "<empty>" : response.Result;

            _status = $"RPC response: {response.Status}";
            AddHistory($"<- InvokeRpcResponse status={response.Status}, request={requestInfo}, message={message}, result={result}");
        }

        private async Task ConnectSdkAsync()
        {
            try
            {
                var connected = await PlayServ.Connect();
                _status = connected ? "SDK connected" : "SDK connection failed";
                AddHistory(_status);
            }
            catch (Exception ex)
            {
                _status = $"Connect error: {ex.Message}";
                AddHistory(_status);
            }
        }

        private void DisconnectSdk()
        {
            PlayServ.Disconnect();
            _status = "SDK disconnected";
            AddHistory(_status);
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

        private void AddHistory(string line)
        {
            _history.Add(line);
            if (_history.Count > 128)
                _history.RemoveAt(0);

            _historyScroll.y = float.MaxValue;
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
            GUILayout.Label("PlayServ RPC Sample");
            GUILayout.Label("How to use: connect SDK, subscribe NotificationEvent, then invoke RPC via expression, args or named payload and watch logs.");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"Notification subscription: {(_notificationSubscription != null ? "Active" : "Inactive")}");
            GUILayout.Label($"Message: {messageText}");

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
                GUILayout.Label("Purpose: Demonstrates RPC invocation through transport and event-based response handling.");
                GUILayout.Label("Invoke Expression uses PlayServ.Invoke<TService>(x => x.Method(...)).");
                GUILayout.Label("Invoke Args uses PlayServ.InvokeArgs(service, method, args...) as the fast positional path.");
                GUILayout.Label("Invoke Named uses PlayServ.InvokeNamed(service, method, payload) as the fast named path.");
                GUILayout.Label("Use in your game: gameplay actions, backend workflows, and follow-up notifications.");
                GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Invoke Expr"))
                InvokeRpcExpression();
            if (GUILayout.Button("Invoke Args"))
                InvokeRpcArgs();
            if (GUILayout.Button("Invoke Named"))
                InvokeRpcNamed();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Subscribe"))
                SubscribeNotifications();
            if (GUILayout.Button("Unsubscribe"))
                UnsubscribeNotifications();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Logs ({_history.Count}):");
            _historyScroll = GUILayout.BeginScrollView(_historyScroll, GUILayout.ExpandHeight(true));
            if (_history.Count == 0)
            {
                GUILayout.Label("- No logs yet");
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
