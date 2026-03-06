using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Playserv.Samples
{
    /// <summary>
    /// Example of shared data subscription using in-memory registry + internal polling.
    /// </summary>
    public sealed class PlayServDataSubscriptionSample : MonoBehaviour
    {
        private const string SamplesSceneFileName = "Samples.unity";
        private const string SubscriptionPollingInfo = "3s";

        [Header("Target")]
        [SerializeField] private string playerId = "player-001";

        [Header("Mutations")]
        [SerializeField] private string renameTo = "RenamedPlayer";
        [SerializeField] private int levelToSet = 10;

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;

        private readonly List<string> _history = new List<string>();
        private ISharedEntity<SamplePlayerDto>? _player;
        private IDisposable? _playerDisposable;
        private Vector2 _historyScroll;
        private string _status = "Not subscribed";
        private SamplePlayerDto? _snapshot;
        private bool _showInfo;

        [ContextMenu("Bind")]
        public void Bind()
        {
            _ = BindAsync();
        }

        [ContextMenu("Unbind")]
        public void Unbind()
        {
            UnbindInternal();
        }

        [ContextMenu("Rename")]
        public void Rename()
        {
            if (_player == null)
                return;

            _player.Update(dto => dto.Nickname = renameTo);
            _status = $"Nickname update requested: {renameTo}";
            AddLog(_status);
        }

        [ContextMenu("Add Level")]
        public void AddLevel()
        {
            if (_player == null)
                return;

            _player.Update(dto => dto.Level++);
            _status = "Add level requested";
            AddLog(_status);
        }

        [ContextMenu("Set Level Async")]
        public void SetLevelAsync()
        {
            _ = SetLevelInternalAsync();
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            _ = RefreshInternalAsync();
        }

        private async Task BindAsync()
        {
            if (_player != null)
            {
                _status = "Already bound";
                AddLog(_status);
                return;
            }

            try
            {
                _player = await PlayServ.SelectEntity<Player, SamplePlayerDto>(
                    playerId,
                    entity => new SamplePlayerDto
                    {
                        Nickname = entity?.Nickname ?? string.Empty,
                        Level = entity?.Level ?? 1
                    });

                _player.Changed += OnPlayerChanged;
                _player.Error += OnPlayerError;
                _player.Terminated += OnPlayerTerminated;

                if (_player is IDisposable disposable)
                    _playerDisposable = disposable;

                _snapshot = _player.Value;
                _status = $"Bound to Player(id={playerId}) (poll {SubscriptionPollingInfo})";
                AddLog(_status);
                AddLog("Subscription backend: in-memory registry + DataGetRequest polling.");
            }
            catch (Exception ex)
            {
                _status = $"Bind error: {ex.Message}";
                AddLog(_status);
            }
        }

        private async Task SetLevelInternalAsync()
        {
            if (_player == null)
                return;

            try
            {
                await _player.UpdateAsync(dto => dto.Level = levelToSet);
                _status = $"Set level requested: {levelToSet}";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Set level error: {ex.Message}";
                AddLog(_status);
            }
        }

        private async Task RefreshInternalAsync()
        {
            if (_player == null)
                return;

            try
            {
                await _player.RefreshAsync();
                _status = "Refresh requested";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Refresh error: {ex.Message}";
                AddLog(_status);
            }
        }

        private async Task ConnectSdkAsync()
        {
            try
            {
                var connected = await PlayServ.Connect();
                _status = connected ? "SDK connected" : "SDK connection failed";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Connect error: {ex.Message}";
                AddLog(_status);
            }
        }

        private void DisconnectSdk()
        {
            PlayServ.Disconnect();
            _status = "SDK disconnected";
            AddLog(_status);
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

        private void OnPlayerChanged(SamplePlayerDto dto)
        {
            _snapshot = dto;
            _status = "Player changed";
            AddLog($"Player updated: key={playerId}, nickname={dto.Nickname}, level={dto.Level}");
        }

        private void OnPlayerError(DataSubscriptionException ex)
        {
            _status = $"Subscription error [{ex.ErrorCode}]: {ex.Message}";
            AddLog(_status);
        }

        private void OnPlayerTerminated()
        {
            _status = "Subscription terminated by server";
            AddLog(_status);
            UnbindInternal();
        }

        private void UnbindInternal()
        {
            if (_player != null)
            {
                _player.Changed -= OnPlayerChanged;
                _player.Error -= OnPlayerError;
                _player.Terminated -= OnPlayerTerminated;
            }

            _playerDisposable?.Dispose();
            _playerDisposable = null;
            _player = null;
            _snapshot = null;
            _status = "Unbound";
            AddLog(_status);
        }

        private void OnDestroy()
        {
            UnbindInternal();
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

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
            GUILayout.Label("PlayServ DataSubscription Sample");
            GUILayout.Label("How to use: connect SDK, click Bind, then run Rename/Add Level/Set Level and watch updates in logs.");
            GUILayout.Label($"Subscription polling: Running every {SubscriptionPollingInfo} (internal).");
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
                GUILayout.Label("Purpose: Shows shared subscription with automatic query generation and polling-based updates.");
                GUILayout.Label("Flow: Bind -> SDK registers subscription in-memory -> polls DataGetRequest every 3s -> emits Changed only on real diff.");
                GUILayout.Label("Dispose behavior: Unbind removes subscription from registry and stops polling.");
                GUILayout.Label("Use in your game: HUD/profile sync, simple reactive state, low-risk replacement while server subscriptions are disabled.");
                GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Bind"))
                _ = BindAsync();
            if (GUILayout.Button("Unbind"))
                UnbindInternal();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rename"))
                Rename();
            if (GUILayout.Button("Add Level"))
                AddLevel();
            if (GUILayout.Button("Set Level Async"))
                _ = SetLevelInternalAsync();
            if (GUILayout.Button("Refresh"))
                _ = RefreshInternalAsync();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Current value:");
            if (_snapshot == null)
            {
                GUILayout.Label("- <null>");
            }
            else
            {
                GUILayout.Label($"- Key: {playerId}");
                GUILayout.Label($"- Nickname: {_snapshot.Nickname}");
                GUILayout.Label($"- Level: {_snapshot.Level}");
            }

            GUILayout.Space(6f);
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
