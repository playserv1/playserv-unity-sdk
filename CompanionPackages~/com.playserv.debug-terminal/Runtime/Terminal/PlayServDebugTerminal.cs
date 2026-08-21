using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Spawn;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.DebugTerminal
{
    /// <summary>
    /// Runtime command terminal for inspecting and exercising the PlayServ core SDK.
    /// </summary>
    public sealed partial class PlayServDebugTerminal : MonoBehaviour
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
        private bool _inputHasFocus;

        private ISharedEntity<DebugTerminalPlayerDto> _player;
        private IDisposable _playerDisposable;
        private IDisposable _notificationSubscription;
        private bool _isGroupJoined;
        private string _boundPlayerId;
        private string _activeBackend = "none";
        private GameObject _lastSpawned;
        private IDebugTerminalCommandExtension _commandExtension;

        private static readonly string[] AvailableCommands = DebugTerminalCommandCatalog.TopLevelCommands;

        private const string InputControlName = "playserv_console_input";
        private const string NotificationServiceName = nameof(NotificationService);
        private const string BroadcastMethodName = nameof(NotificationService.BroadcastToAll);

        private bool IsInputFocused()
        {
            return _inputHasFocus;
        }

        private void OnEnable()
        {
            PlayServ.OnTransportError += OnTransportError;
            PlayServ.OnError += OnPlayServError;
            PlayServ.OnKeepAlivePingSent += OnKeepAlivePingSent;
            PlayServ.OnKeepAlivePongReceived += OnKeepAlivePongReceived;
            PlayServAuth.SessionLost += OnSessionLost;
            PlayServRpc.OnRpcInvokeResponse += OnRpcInvokeResponse;
            if (focusInputOnEnable)
                _focusInputNextFrame = true;

            AddLog("PlayServ console ready. Type 'help'.");
        }

        private void OnDisable()
        {
            PlayServ.OnTransportError -= OnTransportError;
            PlayServ.OnError -= OnPlayServError;
            PlayServ.OnKeepAlivePingSent -= OnKeepAlivePingSent;
            PlayServ.OnKeepAlivePongReceived -= OnKeepAlivePongReceived;
            PlayServAuth.SessionLost -= OnSessionLost;
            PlayServRpc.OnRpcInvokeResponse -= OnRpcInvokeResponse;

            _notificationSubscription?.Dispose();
            _notificationSubscription = null;

            DetachLegacySubscription();
            _playerDisposable?.Dispose();
            ClearLegacySubscriptionState();

            DisposeExtendedState();
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
            if (!showOverlay || PlayServDebugTerminalBootstrap.IsStartupConfigurationVisible)
                return;

            DebugTerminalGuiFontScale.Apply();

            var margin = 10f;
            var width = Mathf.Max(500f, Screen.width - margin * 2f);
            var height = Mathf.Max(300f, Screen.height - margin * 2f);

            GUILayout.BeginArea(new Rect(margin, margin, width, height), GUI.skin.box);
            GUILayout.Label("PlayServ Console Sample");
            GUILayout.Label($"SDK: {PlayServ.State} | Status: {_status}");
            GUILayout.Label($"Auth: {GetAuthSummary()}");
            GUILayout.Label($"Bound Player: {_boundPlayerId ?? "-"} | Backend: {_activeBackend} | Group Joined: {_isGroupJoined}");
            GUILayout.Label("Examples: analytics status | code status | catalog list | match status | record watch");

            if (_focusInputNextFrame)
            {
                GUI.FocusControl(InputControlName);
                _focusInputNextFrame = false;
                _inputHasFocus = true;
            }

            HandleKeyboard();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (var line in _logs)
                GUILayout.Label(line);
            GUILayout.EndScrollView();

            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input);
            _inputHasFocus = GUI.GetNameOfFocusedControl() == InputControlName;

            if (IsInputFocused())
            {
                var suggestions = GetCommandSuggestions(_input);
                if (suggestions.Count > 0)
                {
                    GUILayout.Label($"Commands: {string.Join(", ", suggestions)}");
                }

                var usageHint = GetCommandUsageHint(_input, suggestions);
                if (!string.IsNullOrEmpty(usageHint))
                {
                    GUILayout.Label($"Usage: {usageHint}");
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Run", GUILayout.Height(28f)))
            {
                _inputHasFocus = true;
                SubmitInput();
            }
            if (GUILayout.Button("Clear", GUILayout.Height(28f)))
            {
                _inputHasFocus = false;
                _logs.Clear();
            }
            if (GUILayout.Button("Help", GUILayout.Height(28f)))
            {
                _inputHasFocus = false;
                PrintHelp();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            if (IsIdentityCredentialPromptVisible)
                DrawIdentityCredentialPopup();
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown)
                return;

            if (e.type != EventType.KeyDown)
                return;

            if (!_inputHasFocus || IsIdentityCredentialPromptVisible)
                return;

            if (e.keyCode == KeyCode.Tab)
            {
                TryAutocompleteCommand();
                e.Use();
                return;
            }

            if (IsSubmitEvent(e))
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

        private static bool IsSubmitEvent(Event e)
        {
            if (e == null)
                return false;

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                return true;

            return e.character == '\n' || e.character == '\r';
        }
        private static List<string> GetCommandSuggestions(string input) =>
            DebugTerminalCommandCatalog.GetSuggestions(NormalizeConsoleInput(input), Tokenize);

        private string GetCommandUsageHint(string input, IReadOnlyList<string> suggestions)
        {
            var trimmed = NormalizeConsoleInput(input);
            if (string.IsNullOrEmpty(trimmed))
                return string.Empty;

            var parts = Tokenize(trimmed);
            if (parts.Count == 0)
                return string.Empty;

            return DebugTerminalCommandCatalog.GetUsage(parts, suggestions);
        }

        private void TryAutocompleteCommand()
        {
            var completed = DebugTerminalCommandCatalog.TryAutocomplete(
                NormalizeConsoleInput(_input),
                Tokenize,
                GetCommonPrefix);
            if (completed == null)
                return;

            _input = completed;
            _focusInputNextFrame = true;
        }

        private static string GetCommonPrefix(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
                return string.Empty;

            var prefix = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                var current = values[i];
                var max = Mathf.Min(prefix.Length, current.Length);
                var length = 0;

                while (length < max && char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(current[length]))
                    length++;

                prefix = prefix.Substring(0, length);
                if (prefix.Length == 0)
                    break;
            }

            return prefix;
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
            var line = NormalizeConsoleInput(_input);
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
            line = NormalizeConsoleInput(line);
            var parts = Tokenize(line);
            if (parts.Count == 0)
                return;

            var cmd = NormalizeConsoleToken(parts[0]).ToLowerInvariant();

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
                        AddLog($"SDK state = {PlayServ.State}; auth = {GetAuthSummary()}");
                        break;

                    case "sdk":
                        await ExecuteSdkCommandAsync(parts);
                        break;

                    case "auth":
                        await ExecuteAuthCommandAsync(parts);
                        break;

                    case "identity":
                        await ExecuteIdentityCommandAsync(parts);
                        break;

                    case "providers":
                        await PrintProvidersAsync();
                        break;

                    case "logout":
                        await LogoutAsync();
                        break;

                    case "bind":
                    {
                        var playerId = parts.Count > 1 ? parts[1] : defaultPlayerId;
                        var backend = parts.Count > 2 ? parts[2].ToLowerInvariant() : "polling";
                        await BindAsync(playerId, backend == "transport");
                        break;
                    }

                    case "unbind":
                        await CloseLegacySubscriptionAsync();
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

                    case "record":
                        if (parts.Count > 1 && string.Equals(parts[1], "caller", StringComparison.OrdinalIgnoreCase))
                            await ExecuteRecordCallerCommandAsync(parts);
                        else
                            await ExecuteRecordCommandAsync(parts);
                        break;

                    case "server":
                        await ExecuteServerCommandAsync(parts);
                        break;

                    case "platform":
                        await ExecutePlatformCommandAsync(parts);
                        break;

                    case "table":
                        await ExecuteTableCommandAsync(parts);
                        break;

                    case "analytics":
                        await ExecuteAnalyticsCommandAsync(parts);
                        break;

                    case "code":
                        await ExecuteCodeCommandAsync(parts);
                        break;

                    case "catalog":
                        await ExecuteCatalogCommandAsync(parts);
                        break;

                    case "storefront":
                        await ExecuteStorefrontCommandAsync(parts);
                        break;

                    case "match":
                        await ExecuteMatchCommandAsync(parts);
                        break;

                    case "subscription":
                        await ExecuteSubscriptionCommandAsync(parts);
                        break;

                    case "subevent":
                        SubscribeSampleEvent();
                        break;

                    case "unsubevent":
                        UnsubscribeSampleEvent();
                        break;

                    case "event":
                        ExecuteEventCommand(parts);
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
                        await ExecuteRpcCommandAsync(parts);
                        break;

                    case "spawn":
                        await ExecuteSpawnCommandAsync(parts);
                        break;

                    case "keepalive":
                        ExecuteKeepAliveCommand(parts);
                        break;

                    case "errors":
                        ExecuteErrorsCommand(parts);
                        break;

                    case "clear":
                        _logs.Clear();
                        break;

                    default:
                        AddLog($"Unknown command: '{cmd}'. Type 'help'. Raw length={cmd.Length}");
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
            var settings = PlayServ.Settings;
            var usesAutomaticAuthentication = settings != null &&
                                              settings.EnableAutomaticPlayerAuthentication &&
                                              settings.RuntimeTokenProvider == null &&
                                              string.IsNullOrWhiteSpace(settings.PlayerAccessToken);

            _status = usesAutomaticAuthentication
                ? "Creating or restoring player session"
                : "Resolving runtime player credential";
            AddLog(_status);

            var connected = await PlayServ.Connect();
            _status = connected
                ? $"SDK connected; {GetAuthSummary()}"
                : "SDK connection failed after authentication";
            AddLog(_status);
        }

        private async Task PrintProvidersAsync()
        {
            var result = await PlayServAuth.GetProvidersAsync();
            if (!result.IsSuccess)
            {
                _status = $"Provider discovery failed: {result.Error}";
                AddLog(_status);
                return;
            }

            var providers = result.Providers.Count == 0
                ? "none"
                : string.Join(", ", result.Providers.Select(provider =>
                    $"{provider.Id} ({(provider.Enabled && provider.Available ? "available" : "unavailable")})"));
            _status = $"Auth providers: {providers}";
            AddLog($"Project={result.ProjectSlug}, environment={result.Environment}, providers={providers}");
        }

        private async Task LogoutAsync()
        {
            if (!PlayServAuth.IsLoggedIn)
            {
                AddLog("No authenticated player session to log out.");
                return;
            }

            var result = await PlayServAuth.LogoutAsync();
            if (!result.IsSuccess)
            {
                _status = $"Logout failed: {result.Error}";
                AddLog(_status);
                return;
            }

            _status = result.TransportReady
                ? $"Signed out; reconnected as {GetAuthSummary()}"
                : $"Signed out; replacement {GetAuthSummary()} is offline";
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

            Func<Player, DebugTerminalPlayerDto> map = entity => new DebugTerminalPlayerDto
            {
                Nickname = entity?.Nickname ?? string.Empty,
                Level = entity?.Level ?? 1
            };

            _player = useTransport
                ? await PlayServData.SelectEntity<Player, DebugTerminalPlayerDto>(playerId, map, DataSubscriptionMode.Transport)
                : await PlayServData.SelectEntity<Player, DebugTerminalPlayerDto>(playerId, map);

            _player.Changed += OnPlayerChanged;
            _player.Error += OnPlayerError;
            _player.Terminated += OnPlayerTerminated;
            _player.Failure += OnSubscriptionFailure;
            _playerDisposable = _player as IDisposable;
            _boundPlayerId = playerId;
            _activeBackend = useTransport ? "transport" : "polling";

            _status = $"Bound player '{playerId}' via {_activeBackend}";
            AddLog(_status);
            AddLog($"Snapshot => nickname='{_player.Value?.Nickname}', level={_player.Value?.Level}");
        }

        private void Unbind()
        {
            DetachLegacySubscription();
            _playerDisposable?.Dispose();
            ClearLegacySubscriptionState();
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
            if (TypedEventSubscriptionCount > 0)
            {
                AddLog("DebugTerminalChatEvent already subscribed.");
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                AddLog("Connect first.");
                return;
            }

            AddEventSubscriptions(raw: false, count: 1);
            _status = "Subscribed DebugTerminalChatEvent";
            AddLog(_status);
        }

        private void UnsubscribeSampleEvent()
        {
            DisposeEventSubscriptions(raw: false, disposeAll: true);
            _status = "Unsubscribed DebugTerminalChatEvent";
            AddLog(_status);
        }

        private async Task JoinGroupAsync(string groupName)
        {
            var joined = await PlayServEvents.SubscribeGroupAsync(groupName);
            _isGroupJoined = joined;
            _status = joined ? $"Joined group '{groupName}'" : $"Failed to join group '{groupName}'";
            AddLog(_status);
        }

        private async Task LeaveGroupAsync(string groupName)
        {
            var left = await PlayServEvents.UnsubscribeGroupAsync(groupName);
            if (left)
                _isGroupJoined = false;
            _status = left ? $"Left group '{groupName}'" : $"Failed to leave group '{groupName}'";
            AddLog(_status);
        }

        private void PublishGlobal(string text)
        {
            PlayServEvents.Publish(new DebugTerminalChatEvent
            {
                SenderId = PlayServAuth.CurrentSession.PlayerId,
                Text = text,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _status = "Published global event";
            AddLog($"Global => {text}");
        }

        private void PublishGroup(string groupName, string text)
        {
            PlayServEvents.PublishForGroup(groupName, new DebugTerminalChatEvent
            {
                SenderId = PlayServAuth.CurrentSession.PlayerId,
                Text = text,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _status = $"Published group event to '{groupName}'";
            AddLog($"Group[{groupName}] => {text}");
        }

        private void PublishUser(string userId, string text)
        {
            PlayServEvents.PublishForUser(userId, new DebugTerminalChatEvent
            {
                SenderId = PlayServAuth.CurrentSession.PlayerId,
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

            _notificationSubscription = PlayServEvents.Subscribe<NotificationEvent>(OnNotificationReceived);
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
#if UNITY_WEBGL && !UNITY_EDITOR
                    AddLog("rpc expr is not supported in WebGL (Expression.Compile is unavailable under IL2CPP). Use 'rpc named' or 'rpc args'.");
                    _status = "rpc expr unsupported in WebGL";
                    return;
#else
                    PlayServRpc.Invoke<NotificationService>(x => x.BroadcastToAll(text));
                    AddLog($"RPC expr => {text}");
                    break;
#endif

                case "args":
                    PlayServRpc.InvokeArgs(NotificationServiceName, BroadcastMethodName, text);
                    AddLog($"RPC args => {text}");
                    break;

                case "named":
                default:
                    PlayServRpc.InvokeNamed(
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

            var instance = await PlayServSpawn.Spawn(assetName, position, rotation);
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

        private void OnPlayerChanged(DebugTerminalPlayerDto dto)
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

        private void OnSampleEventReceived(DebugTerminalChatEvent evt)
        {
            _status = "DebugTerminalChatEvent received";
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

        private void OnSessionLost(PlayServSessionLostInfo info)
        {
            _status = $"Player session lost: {info.Reason}";
            AddLog($"{_status}; playerId={info.PreviousSession.PlayerId}; retry={info.CanRetry}; {info.Message}");
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
            foreach (var usage in DebugTerminalCommandCatalog.AllUsages)
                AddLog(usage);
        }

        private static string GetAuthSummary(bool includeProviders = false)
        {
            var session = PlayServAuth.CurrentSession;
            if (!session.IsLoggedIn)
                return "not authenticated";

            var summary = $"playerId={session.PlayerId}, session={session.Kind}";
            if (!includeProviders)
                return summary;

            var providers = session.AreLinkedProvidersKnown
                ? session.LinkedProviders.Count == 0
                    ? "none"
                    : string.Join(",", session.LinkedProviders)
                : "not loaded";
            return $"{summary}, linkedProviders={providers}";
        }

        private static string NormalizeConsoleInput(string input) =>
            DebugTerminalCommandParser.Normalize(input);

        private static string NormalizeConsoleToken(string token)
        {
            return NormalizeConsoleInput(token);
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

        private static List<string> Tokenize(string input) =>
            DebugTerminalCommandParser.Tokenize(input);

        internal void AddExtensionLog(string line) => AddLog(line);

        internal void SetExtensionStatus(string status)
        {
            _status = string.IsNullOrWhiteSpace(status) ? "Idle" : status;
        }

        internal string CurrentBackendAddress => PlayServ.Settings?.BackendServerAddress;

        internal void CaptureExtensionError(PlayServError error) => OnSubscriptionFailure(error);

        internal void RequestSecureSecret(
            string title,
            string label,
            Func<string, Task> callback) =>
            OpenExternalCredentialPrompt(title, label, callback);

        private bool TryGetCommandExtension(out IDebugTerminalCommandExtension extension)
        {
            extension = _commandExtension;
            if (extension != null)
                return true;
            if (!DebugTerminalCommandExtensionRegistry.TryCreate(this, out extension))
                return false;
            _commandExtension = extension;
            return true;
        }

        private Task ExecuteServerCommandAsync(IReadOnlyList<string> parts)
        {
            if (TryGetCommandExtension(out var extension))
                return extension.ExecuteServerCommandAsync(parts);

            AddLog(
                "Dedicated-server commands are unavailable. Install and enable com.playserv.game-server, " +
                "then run the terminal in the Editor or a UNITY_SERVER build.");
            return Task.CompletedTask;
        }

        private Task ExecuteRecordCallerCommandAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count > 2 && string.Equals(parts[2], "client", StringComparison.OrdinalIgnoreCase))
                return SwitchRecordCallerAsync("client", null, null, null);
            if (TryGetCommandExtension(out var extension))
                return extension.ExecuteRecordCallerCommandAsync(parts);

            AddLog("Usage: record caller client. Server callers require the Game Server terminal extension.");
            return Task.CompletedTask;
        }
    }
}
