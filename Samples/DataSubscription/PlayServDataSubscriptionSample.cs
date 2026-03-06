using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
        private const int UiLogTrimLimit = 220;
        private static readonly Regex RequestIdRegex =
            new Regex("\"RequestId\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly string[] RandomNamePrefixes =
        {
            "Player",
            "Ranger",
            "Falcon",
            "Nova",
            "Tanker",
            "Shadow",
            "Blaze",
            "Storm"
        };

        [Header("Target")]
        [SerializeField] private string playerId = "player-001";

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showTransportDataGetLogsInUi = true;

        private readonly List<string> _history = new List<string>();
        private ISharedEntity<SamplePlayerDto>? _player;
        private IDisposable? _playerDisposable;
        private Vector2 _historyScroll;
        private string _status = "Not subscribed";
        private SamplePlayerDto? _snapshot;
        private bool _showInfo;
        private bool _setLevelInProgress;
        private bool _logHooked;

        private void OnEnable()
        {
            HookUnityLogs();
        }

        private void OnDisable()
        {
            UnhookUnityLogs();
        }

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

        [ContextMenu("Randomize Nickname")]
        public void Rename()
        {
            if (_player == null)
                return;

            var randomNickname = GenerateRandomNickname();
            _player.Update(dto => dto.Nickname = randomNickname);
            _status = $"Random nickname requested: {randomNickname}";
            AddLog(_status);
        }

        [ContextMenu("Reset Player")]
        public void ResetPlayer()
        {
            if (_player == null)
                return;

            _player.Update(dto =>
            {
                dto.Nickname = "Player";
                dto.Level = 0;
            });

            _status = "Reset requested: nickname=Player, level=0";
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

            if (_setLevelInProgress)
            {
                _status = "Set level is already in progress";
                AddLog(_status);
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                _status = $"Set level skipped: SDK state is {PlayServ.State}";
                AddLog(_status);
                return;
            }

            try
            {
                _setLevelInProgress = true;
                await _player.UpdateAsync(dto => dto.Level += 10);
                _status = "Set level async requested: +10";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Set level error: {ex.Message}";
                AddLog(_status);
            }
            finally
            {
                _setLevelInProgress = false;
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
            UnhookUnityLogs();
            UnbindInternal();
        }

        private void HookUnityLogs()
        {
            if (_logHooked)
                return;

            Application.logMessageReceived += OnUnityLogMessageReceived;
            _logHooked = true;
        }

        private void UnhookUnityLogs()
        {
            if (!_logHooked)
                return;

            Application.logMessageReceived -= OnUnityLogMessageReceived;
            _logHooked = false;
        }

        private void OnUnityLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!showTransportDataGetLogsInUi || string.IsNullOrWhiteSpace(condition))
                return;

            if (condition.StartsWith("Message sent: DataGetRequest", StringComparison.Ordinal))
            {
                AddLog($"[Transport] -> DataGetRequest requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (condition.StartsWith("Received JSON: {\"Command\":\"DataGetResponse\"", StringComparison.Ordinal))
            {
                AddLog($"[Transport] <- DataGetResponse requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (condition.IndexOf("[DataGet]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddLog($"[Transport] {TrimForUi(condition)}");
            }
        }

        private static string ExtractRequestIdOrUnknown(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "n/a";

            var match = RequestIdRegex.Match(text);
            return match.Success ? match.Groups[1].Value : "n/a";
        }

        private static string TrimForUi(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (text.Length <= UiLogTrimLimit)
                return text;

            return text.Substring(0, UiLogTrimLimit) + "...";
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

        private static string GenerateRandomNickname()
        {
            var prefix = RandomNamePrefixes[UnityEngine.Random.Range(0, RandomNamePrefixes.Length)];
            var suffix = UnityEngine.Random.Range(100, 1000);
            return $"{prefix}{suffix}";
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
            GUILayout.Label("How to use: connect SDK, click Bind, then run Rename/Add Level/Set Level/Reset and watch updates in logs.");
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
                GUILayout.Label("UI transport logs: request/response DataGet lines are mirrored from Unity console.");
                GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Bind"))
                _ = BindAsync();
            if (GUILayout.Button("Unbind"))
                UnbindInternal();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Random Name"))
                Rename();
            if (GUILayout.Button("Add Level"))
                AddLevel();
            var setLevelLabel = _setLevelInProgress ? "Set Level Async (Running...)" : "Set Level Async";
            var prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && !_setLevelInProgress && PlayServ.State == PlayServState.Online;
            if (GUILayout.Button(setLevelLabel))
                _ = SetLevelInternalAsync();
            GUI.enabled = prevEnabled;
            if (GUILayout.Button("Reset"))
                ResetPlayer();
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
