using System;
using System.Collections.Generic;
using Playserv.RPC;
using Playserv.Wrapper;
using UnityEngine;

#nullable enable

namespace Playserv.Samples
{
    /// <summary>
    /// Demo RPC service used by local in-process invoker sample.
    /// </summary>
    [Rpc]
    public sealed class SampleNotificationRpcService
    {
        /// <summary>
        /// Example RPC method.
        /// </summary>
        /// <param name="message">Notification message.</param>
        public void BroadcastToAll(string message)
        {
            Debug.Log($"[SampleNotificationRpcService] BroadcastToAll: {message}");
        }
    }

    /// <summary>
    /// RPC sample that can execute PlayServ.Invoke locally without websocket.
    /// </summary>
    public sealed class PlayServRpcSample : MonoBehaviour
    {
        [Header("Message")]
        [SerializeField] private string messageText = "Hello from RPC sample";

        [Header("Execution")]
        [SerializeField] private bool enableLocalInvokerOnEnable = true;
        [SerializeField] private bool showOverlay = true;

        private LocalRpcInvoker? _localInvoker;
        private readonly List<string> _history = new List<string>();
        private string _status = "Idle";

        private void OnEnable()
        {
            if (enableLocalInvokerOnEnable)
                EnableLocalInvoker();
        }

        private void OnDisable()
        {
            if (_localInvoker != null)
                DisableLocalInvoker();
        }

        [ContextMenu("Enable Local RPC Invoker")]
        public void EnableLocalInvoker()
        {
            if (_localInvoker != null)
            {
                _status = "Local invoker is already enabled";
                return;
            }

            _localInvoker = new LocalRpcInvoker()
                .RegisterService(new SampleNotificationRpcService());
            PlayServ.SetRpcInvoker(_localInvoker);
            _status = "Local invoker enabled";
            AddHistory(_status);
        }

        [ContextMenu("Disable Local RPC Invoker")]
        public void DisableLocalInvoker()
        {
            PlayServ.SetRpcInvoker(null);
            _localInvoker = null;
            _status = "Local invoker disabled";
            AddHistory(_status);
        }

        [ContextMenu("Invoke RPC")]
        public void InvokeRpc()
        {
            try
            {
                PlayServ.Invoke<SampleNotificationRpcService>(x => x.BroadcastToAll(messageText));

                _status = _localInvoker != null
                    ? "RPC invoked locally (in-process)"
                    : "RPC invoke sent through transport";
                AddHistory($"{_status}: {messageText}");
            }
            catch (Exception ex)
            {
                _status = $"RPC invoke failed: {ex.Message}";
                AddHistory(_status);
            }
        }

        private void AddHistory(string line)
        {
            _history.Add(line);
            if (_history.Count > 8)
                _history.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            GUILayout.BeginArea(new Rect(10f, 420f, 620f, 220f), GUI.skin.box);
            GUILayout.Label("PlayServ RPC Sample");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"Local invoker: {(_localInvoker != null ? "Enabled" : "Disabled")}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable Local"))
                EnableLocalInvoker();
            if (GUILayout.Button("Disable Local"))
                DisableLocalInvoker();
            if (GUILayout.Button("Invoke RPC"))
                InvokeRpc();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("History:");
            foreach (var line in _history)
                GUILayout.Label($"- {line}");
            GUILayout.EndArea();
        }
    }
}

#nullable restore
