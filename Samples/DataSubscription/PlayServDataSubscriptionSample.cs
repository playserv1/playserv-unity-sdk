using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Responses;
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
    /// Example of SelectEntity + mutation + refresh flow.
    /// </summary>
    public sealed class PlayServDataSubscriptionSample : MonoBehaviour
    {
        private const string SamplesSceneFileName = "Samples.unity";

        [Header("Target")]
        [SerializeField] private string playerId = "player-001";

        [Header("Mutations")]
        [SerializeField] private string renameTo = "RenamedPlayer";
        [SerializeField] private int levelToSet = 10;

        [Header("DataGet (By Key)")]
        [SerializeField] private string dataGetKey = "SHOP-001";
        [SerializeField] private string dataGetQuery = "Shop(id:$id) { RegularItems { Id Name Category Price { Amount Currency } } }";
        [SerializeField] private string dataGetId = "SHOP-001";

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;

        private readonly List<string> _history = new List<string>();
        private ISharedEntity<SamplePlayerDto> _player;
        private IDisposable _playerDisposable;
        private IDisposable _dataGetPolling;
        private Vector2 _historyScroll;
        private string _status = "Not subscribed";
        private SamplePlayerDto _snapshot;
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

            _player.Update(dto => dto.Name = renameTo);
            _status = $"Rename requested: {renameTo}";
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

        [ContextMenu("DataGet Once")]
        public void DataGetOnce()
        {
            _ = DataGetOnceAsync();
        }

        [ContextMenu("Start DataGet Polling")]
        public void StartDataGetPolling()
        {
            if (_dataGetPolling != null)
            {
                _status = "DataGet polling already running";
                AddLog(_status);
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                _status = "DataGet polling requires connected SDK";
                AddLog(_status);
                return;
            }

            _dataGetPolling = PlayServ.StartDataByKeyPolling(
                dataGetKey,
                dataGetQuery,
                BuildDataGetVariables(),
                OnDataGetPollingResponse,
                OnDataGetPollingError);

            _status = $"DataGet polling started (4s). key={dataGetKey}";
            AddLog(_status);
        }

        [ContextMenu("Stop DataGet Polling")]
        public void StopDataGetPolling()
        {
            if (_dataGetPolling == null)
                return;

            _dataGetPolling.Dispose();
            _dataGetPolling = null;
            _status = "DataGet polling stopped";
            AddLog(_status);
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
                _player = await PlayServ.SelectEntity<SamplePlayerEntity, SamplePlayerDto>(
                    playerId,
                    entity => new SamplePlayerDto
                    {
                        Id = entity?.Id ?? string.Empty,
                        Name = entity?.Name ?? string.Empty,
                        Level = entity?.Level ?? 0
                    });

                _player.Changed += OnPlayerChanged;
                _player.Error += OnPlayerError;
                _player.Terminated += OnPlayerTerminated;

                if (_player is IDisposable disposable)
                    _playerDisposable = disposable;

                _snapshot = _player.Value;
                _status = $"Bound to player: {playerId}";
                AddLog(_status);
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

        private async Task DataGetOnceAsync()
        {
            if (PlayServ.State != PlayServState.Online)
            {
                _status = "DataGet requires connected SDK";
                AddLog(_status);
                return;
            }

            try
            {
                var response = await PlayServ.GetDataByKeyAsync(
                    dataGetKey,
                    dataGetQuery,
                    BuildDataGetVariables());

                HandleDataGetResponse(response, "DataGet once");
            }
            catch (Exception ex)
            {
                _status = $"DataGet once error: {ex.Message}";
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
            StopDataGetPolling();
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
            AddLog($"Player updated: id={dto.Id}, name={dto.Name}, level={dto.Level}");
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

        private void OnDataGetPollingResponse(DataGetResponse response)
        {
            HandleDataGetResponse(response, "DataGet poll");
        }

        private void OnDataGetPollingError(Exception ex)
        {
            _status = $"DataGet polling error: {ex.Message}";
            AddLog(_status);
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
            StopDataGetPolling();
            UnbindInternal();
        }

        private Dictionary<string, object> BuildDataGetVariables()
        {
            return new Dictionary<string, object>
            {
                ["id"] = dataGetId
            };
        }

        private void HandleDataGetResponse(DataGetResponse response, string source)
        {
            if (response == null)
            {
                _status = $"{source}: empty response";
                AddLog(_status);
                return;
            }

            if (response.HasError)
            {
                var code = response.Error?.Code ?? 0;
                var message = response.Error?.Message ?? "Unknown error";
                _status = $"{source} error [{code}]: {message}";
                AddLog(_status);
                return;
            }

            var payload = response.Result?.Data?.ToString() ?? "<null>";
            _status = $"{source} success";
            AddLog($"{source} -> {TrimForLog(payload, 240)}");
        }

        private static string TrimForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
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
                GUILayout.Label("Purpose: Shows SelectEntity and simplified DataGet by key flow.");
                GUILayout.Label("How to use: Connect, Bind by player id for reactive updates, and use DataGet buttons for key-based reads.");
                GUILayout.Label("Use in your game: HUD sync, inventory/shop snapshots, periodic read-only data refresh.");
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
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"DataGet polling: {(_dataGetPolling != null ? "Running (4s)" : "Stopped")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("DataGet Once"))
                _ = DataGetOnceAsync();
            if (GUILayout.Button("Start DataGet Poll"))
                StartDataGetPolling();
            if (GUILayout.Button("Stop DataGet Poll"))
                StopDataGetPolling();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Current value:");
            if (_snapshot == null)
            {
                GUILayout.Label("- <null>");
            }
            else
            {
                GUILayout.Label($"- Id: {_snapshot.Id}");
                GUILayout.Label($"- Name: {_snapshot.Name}");
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
