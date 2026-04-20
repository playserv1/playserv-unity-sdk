using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Spawn;
using Playserv.Test.RPC;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// One-scene developer console for PlayServ samples.
    /// Replaces multiple sample scenes with a single command-driven overlay.
    /// </summary>
    public sealed class PlayServConsoleSample : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool focusInputOnEnable = true;
        [SerializeField] private int maxLogEntries = 300;

        [Header("Defaults")]
        [SerializeField] private string defaultPlayerId = "player-001";
        [SerializeField] private string defaultGroupName = "demo-group";
        [SerializeField] private string defaultTargetUserId = "player-002";
        [SerializeField] private string defaultSpawnAssetName = "TestCube";
        [SerializeField] private Vector3 spawnCenter = Vector3.zero;
        [SerializeField] private float spawnRadius = 6f;

        private readonly List<string> _logs = new();
        private readonly Queue<string> _history = new();
        private int _historyIndex = -1;
        private string _input = string.Empty;
        private string _status = "Idle";
        private Vector2 _scroll;
        private bool _focusInputNextFrame;

        private ISharedEntity<SamplePlayerDto> _player;
        private IDisposable _playerDisposable;
        private IDisposable _sampleEventSubscription;
        private IDisposable _notificationSubscription;
        private bool _isGroupJoined;
        private string _boundPlayerId;
        private string _activeBackend = "none";
        private GameObject _lastSpawned;

        private const string InputControlName = "playserv_console_input";
        private const string NotificationServiceName = nameof(NotificationService);
        private const string BroadcastMethodName = nameof(NotificationService.BroadcastToAll);

        private void OnEnable()
        {
            PlayServ.OnTransportError += OnTransportError;
            PlayServ.OnRpcInvokeResponse += OnRpcInvokeResponse;
            if (focusInputOnEnable)
                _focusInputNextFrame = true;

            AddLog("PlayServ console ready. Type 'help'.");
        }

        private void OnDisable()
        {
            PlayServ.OnTransportError -= OnTransportError;
            PlayServ.OnRpcInvokeResponse -= OnRpcInvokeResponse;

            _sampleEventSubscription?.Dispose();
            _sampleEventSubscription = null;

            _notificationSubscription?.Dispose();
            _notificationSubscription = null;

            _playerDisposable?.Dispose();
            _playerDisposable = null;
            _player = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                showOverlay = !showOverlay;
                if (showOverlay)
                    _focusInputNextFrame = true;
            }
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            SampleGuiFontScale.Apply();

            var margin = 10f;
            var width = Mathf.Max(500f, Screen.width - margin * 2f);
            var height = Mathf.Max(300f, Screen.height - margin * 2f);

            GUILayout.BeginArea(new Rect(margin, margin, width, height), GUI.skin.box);
            GUILayout.Label("PlayServ Console Sample");
            GUILayout.Label($"SDK: {PlayServ.State} | Status: {_status}");
            GUILayout.Label($"Bound Player: {_boundPlayerId ?? "-"} | Backend: {_activeBackend} | Group Joined: {_isGroupJoined}");
            GUILayout.Label("Examples: connect | bind player-001 polling | subevent | publishglobal hello | rpc named hi | spawn TestCube");

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (var line in _logs)
                GUILayout.Label(line);
            GUILayout.EndScrollView();

            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input);

            if (_focusInputNextFrame)
            {
                GUI.FocusControl(InputControlName);
                _focusInputNextFrame = false;
            }

            HandleKeyboard();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Run", GUILayout.Height(28f)))
                SubmitInput();
            if (GUILayout.Button("Clear", GUILayout.Height(28f)))
                _logs.Clear();
            if (GUILayout.Button("Help", GUILayout.Height(28f)))
                PrintHelp();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return;

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                SubmitInput();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.UpArrow)
            {
                NavigateHistory(-1);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.DownArrow)
            {
                NavigateHistory(1);
                e.Use();
            }
        }

        private void NavigateHistory(int direction)
        {
            if (_history.Count == 0)
                return;

            var items = _history.ToArray();
            if (_historyIndex < 0)
                _historyIndex = items.Length;

            _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, items.Length);
            _input = _historyIndex >= items.Length ? string.Empty : items[_historyIndex];
            _focusInputNextFrame = true;
        }

        private void SubmitInput()
        {
            var line = _input?.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                _focusInputNextFrame = true;
                return;
            }

            _history.Enqueue(line);
            while (_history.Count > 50)
                _history.Dequeue();
            _historyIndex = -1;

            AddLog($"> {line}");
            _input = string.Empty;
            _focusInputNextFrame = true;
            _ = ExecuteCommandAsync(line);
        }

        private async Task ExecuteCommandAsync(string line)
        {
            var parts = Tokenize(line);
            if (parts.Count == 0)
                return;

            var cmd = parts[0].ToLowerInvariant();

            try
            {
                switch (cmd)
                {
                    case "help":
                        PrintHelp();
                        break;

                    case "connect":
                        await ConnectAsync();
                        break;

                    case "disconnect":
                        Disconnect();
                        break;

                    case "state":
                        AddLog($"SDK state = {PlayServ.State}");
                        break;

                    case "bind":
                    {
                        var playerId = parts.Count > 1 ? parts[1] : defaultPlayerId;
                        var backend = parts.Count > 2 ? parts[2].ToLowerInvariant() : "polling";
                        await BindAsync(playerId, backend == "transport");
                        break;
                    }

                    case "unbind":
                        Unbind();
                        break;

                    case "rename":
                        Rename(parts.Count > 1 ? string.Join(" ", parts.Skip(1)) : $"Player_{UnityEngine.Random.Range(100, 999)}");
                        break;

                    case "addlevel":
                    {
                        var amount = parts.Count > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 1;
                        AddLevel(amount);
                        break;
                    }

                    case "setlevel":
                    {
                        if (parts.Count < 2 || !int.TryParse(parts[1], out var value))
                        {
                            AddLog("Usage: setlevel <number>");
                            break;
                        }
                        await SetLevelAsync(value);
                        break;
                    }

                    case "refresh":
                        await RefreshPlayerAsync();
                        break;

                    case "subevent":
                        SubscribeSampleEvent();
                        break;

                    case "unsubevent":
                        UnsubscribeSampleEvent();
                        break;

                    case "joingroup":
                        await JoinGroupAsync(parts.Count > 1 ? parts[1] : defaultGroupName);
                        break;

                    case "leavegroup":
                        await LeaveGroupAsync(parts.Count > 1 ? parts[1] : defaultGroupName);
                        break;

                    case "publishglobal":
                        PublishGlobal(parts.Count > 1 ? string.Join(" ", parts.Skip(1)) : "hello");
                        break;

                    case "publishgroup":
                    {
                        var group = parts.Count > 2 ? parts[1] : defaultGroupName;
                        var start = parts.Count > 2 ? 2 : 1;
                        var text = parts.Count > start ? string.Join(" ", parts.Skip(start)) : "hello";
                        PublishGroup(group, text);
                        break;
                    }

                    case "publishuser":
                    {
                        var userId = parts.Count > 2 ? parts[1] : defaultTargetUserId;
                        var start = parts.Count > 2 ? 2 : 1;
                        var text = parts.Count > start ? string.Join(" ", parts.Skip(start)) : "hello";
                        PublishUser(userId, text);
                        break;
                    }

                    case "subrpc":
                        SubscribeRpcNotifications();
                        break;

                    case "unsubrpc":
                        UnsubscribeRpcNotifications();
                        break;

                    case "rpc":
                    {
                        var mode = parts.Count > 1 ? parts[1].ToLowerInvariant() : "named";
                        var text = parts.Count > 2 ? string.Join(" ", parts.Skip(2)) : "hello from rpc";
                        InvokeRpc(mode, text);
                        break;
                    }

                    case "spawn":
                    {
                        var assetName = parts.Count > 1 ? parts[1] : defaultSpawnAssetName;
                        await SpawnAsync(assetName);
                        break;
                    }

                    case "clear":
                        _logs.Clear();
                        break;

                    default:
                        AddLog($"Unknown command: {cmd}. Type 'help'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _status = $"Command error: {ex.Message}";
                AddLog(_status);
            }
        }

        private async Task ConnectAsync()
        {
            var connected = await PlayServ.Connect();
            _status = connected ? "SDK connected" : "SDK connection failed";
            AddLog(_status);
        }

        private void Disconnect()
        {
            PlayServ.Disconnect();
            _isGroupJoined = false;
            _status = "SDK disconnected";
            AddLog(_status);
        }

        private async Task BindAsync(string playerId, bool useTransport)
        {
            if (_player != null)
            {
                AddLog("Already bound. Use 'unbind' first.");
                return;
            }

            Func<Player, SamplePlayerDto> map = entity => new SamplePlayerDto
            {
                Nickname = entity?.Nickname ?? string.Empty,
                Level = entity?.Level ?? 1
            };

            _player = useTransport
                ? await PlayServ.SelectEntity<Player, SamplePlayerDto>(playerId, map, DataSubscriptionMode.Transport)
                : await PlayServ.SelectEntity<Player, SamplePlayerDto>(playerId, map);

            _player.Changed += OnPlayerChanged;
            _player.Error += OnPlayerError;
            _player.Terminated += OnPlayerTerminated;
            _playerDisposable = _player as IDisposable;
            _boundPlayerId = playerId;
            _activeBackend = useTransport ? "transport" : "polling";

            _status = $"Bound player '{playerId}' via {_activeBackend}";
            AddLog(_status);
            AddLog($"Snapshot => nickname='{_player.Value?.Nickname}', level={_player.Value?.Level}");
        }

        private void Unbind()
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
            _boundPlayerId = null;
            _activeBackend = "none";
            _status = "Unbound player";
            AddLog(_status);
        }

        private void Rename(string nickname)
        {
            if (!EnsurePlayerBound())
                return;

            _player.Update(dto => dto.Nickname = nickname);
            _status = $"Rename requested: {nickname}";
            AddLog(_status);
        }

        private void AddLevel(int amount)
        {
            if (!EnsurePlayerBound())
                return;

            _player.Update(dto => dto.Level += amount);
            _status = $"Add level requested: +{amount}";
            AddLog(_status);
        }

        private async Task SetLevelAsync(int value)
        {
            if (!EnsurePlayerBound())
                return;

            await _player.UpdateAsync(dto => dto.Level = value);
            _status = $"Set level requested: {value}";
            AddLog(_status);
        }

        private async Task RefreshPlayerAsync()
        {
            if (!EnsurePlayerBound())
                return;

            await _player.RefreshAsync();
            _status = "Refresh requested";
            AddLog(_status);
        }

        private void SubscribeSampleEvent()
        {
            if (_sampleEventSubscription != null)
            {
                AddLog("SampleChatEvent already subscribed.");
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                AddLog("Connect first.");
                return;
            }

            _sampleEventSubscription = PlayServ.Subscribe<SampleChatEvent>(OnSampleEventReceived);
            _status = "Subscribed SampleChatEvent";
            AddLog(_status);
        }

        private void UnsubscribeSampleEvent()
        {
            _sampleEventSubscription?.Dispose();
            _sampleEventSubscription = null;
            _status = "Unsubscribed SampleChatEvent";
            AddLog(_status);
        }

        private async Task JoinGroupAsync(string groupName)
        {
            var joined = await PlayServ.SubscribeGroupAsync(groupName);
            _isGroupJoined = joined;
            _status = joined ? $"Joined group '{groupName}'" : $"Failed to join group '{groupName}'";
            AddLog(_status);
        }

        private async Task LeaveGroupAsync(string groupName)
        {
            var left = await PlayServ.UnsubscribeGroupAsync(groupName);
            if (left)
                _isGroupJoined = false;
            _status = left ? $"Left group '{groupName}'" : $"Failed to leave group '{groupName}'";
            AddLog(_status);
        }

        private void PublishGlobal(string text)
        {
            PlayServ.Publish(new SampleChatEvent
            {
                SenderId = PlayServ.Settings.UserId,
                Text = text,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _status = "Published global event";
            AddLog($"Global => {text}");
        }

        private void PublishGroup(string groupName, string text)
        {
            PlayServ.PublishForGroup(groupName, new SampleChatEvent
            {
                SenderId = PlayServ.Settings.UserId,
                Text = text,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _status = $"Published group event to '{groupName}'";
            AddLog($"Group[{groupName}] => {text}");
        }

        private void PublishUser(string userId, string text)
        {
            PlayServ.PublishForUser(userId, new SampleChatEvent
            {
                SenderId = PlayServ.Settings.UserId,
                Text = text,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _status = $"Published user event to '{userId}'";
            AddLog($"User[{userId}] => {text}");
        }

        private void SubscribeRpcNotifications()
        {
            if (_notificationSubscription != null)
            {
                AddLog("NotificationEvent already subscribed.");
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                AddLog("Connect first.");
                return;
            }

            _notificationSubscription = PlayServ.Subscribe<NotificationEvent>(OnNotificationReceived);
            _status = "Subscribed NotificationEvent";
            AddLog(_status);
        }

        private void UnsubscribeRpcNotifications()
        {
            _notificationSubscription?.Dispose();
            _notificationSubscription = null;
            _status = "Unsubscribed NotificationEvent";
            AddLog(_status);
        }

        private void InvokeRpc(string mode, string text)
        {
            switch (mode)
            {
                case "expr":
                case "expression":
                    PlayServ.Invoke<NotificationService>(x => x.BroadcastToAll(text));
                    AddLog($"RPC expr => {text}");
                    break;

                case "args":
                    PlayServ.InvokeArgs(NotificationServiceName, BroadcastMethodName, text);
                    AddLog($"RPC args => {text}");
                    break;

                case "named":
                default:
                    PlayServ.InvokeNamed(
                        NotificationServiceName,
                        BroadcastMethodName,
                        new Dictionary<string, object> { ["message"] = text });
                    AddLog($"RPC named => {text}");
                    break;
            }

            _status = $"RPC {mode} invoke sent";
        }

        private async Task SpawnAsync(string assetName)
        {
            var position = spawnCenter + UnityEngine.Random.insideUnitSphere * spawnRadius;
            position.y = Mathf.Max(0f, position.y);
            var rotation = UnityEngine.Random.rotation;

            var instance = await PlayServ.Spawn(assetName, position, rotation);
            _lastSpawned = instance;

            if (instance == null)
            {
                _status = "Spawn failed";
                AddLog(_status);
                return;
            }

            var networkObject = instance.GetComponent<NetworkObject>();
            _status = networkObject != null
                ? $"Spawned '{instance.name}' id={networkObject.NetworkId}"
                : $"Spawned '{instance.name}'";

            AddLog(_status);
        }

        private void OnPlayerChanged(SamplePlayerDto dto)
        {
            AddLog($"Player changed => nickname='{dto?.Nickname}', level={dto?.Level}");
            _status = "Player changed";
        }

        private void OnPlayerError(Exception ex)
        {
            _status = $"Player subscription error: {ex.Message}";
            AddLog(_status);
        }

        private void OnPlayerTerminated()
        {
            _status = "Player subscription terminated";
            AddLog(_status);
        }

        private void OnSampleEventReceived(SampleChatEvent evt)
        {
            _status = "SampleChatEvent received";
            AddLog($"Event <= [{evt.SenderId}] {evt.Text}");
        }

        private void OnNotificationReceived(NotificationEvent evt)
        {
            _status = "NotificationEvent received";
            AddLog($"RPC Event <= type={evt.EventType}, message={evt.Message}");
        }

        private void OnTransportError(TransportError error)
        {
            _status = $"Transport error: {error}";
            AddLog(_status);
        }

        private void OnRpcInvokeResponse(InvokeRpcResponse response)
        {
            var request = response.Request == null
                ? "n/a"
                : $"{response.Request.ServiceName}.{response.Request.MethodName}";

            AddLog($"RPC Response <= status={response.Status}, request={request}, message={response.Message}, result={response.Result}");
            _status = $"RPC response: {response.Status}";
        }

        private bool EnsurePlayerBound()
        {
            if (_player != null)
                return true;

            AddLog("Player is not bound. Use: bind <playerId> [polling|transport]");
            return false;
        }

        private void PrintHelp()
        {
            AddLog("=== COMMANDS ===");
            AddLog("connect");
            AddLog("disconnect");
            AddLog("state");
            AddLog("bind <playerId> [polling|transport]");
            AddLog("unbind");
            AddLog("rename <new name>");
            AddLog("addlevel [amount]");
            AddLog("setlevel <value>");
            AddLog("refresh");
            AddLog("subevent / unsubevent");
            AddLog("joingroup [group]");
            AddLog("leavegroup [group]");
            AddLog("publishglobal <text>");
            AddLog("publishgroup [group] <text>");
            AddLog("publishuser [userId] <text>");
            AddLog("subrpc / unsubrpc");
            AddLog("rpc [expr|args|named] <text>");
            AddLog("spawn [assetName]");
            AddLog("clear");
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _logs.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            while (_logs.Count > maxLogEntries)
                _logs.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private static List<string> Tokenize(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
                return result;

            var current = string.Empty;
            var inQuotes = false;

            foreach (var ch in input)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        result.Add(current);
                        current = string.Empty;
                    }
                    continue;
                }

                current += ch;
            }

            if (!string.IsNullOrEmpty(current))
                result.Add(current);

            return result;
        }
    }
}
