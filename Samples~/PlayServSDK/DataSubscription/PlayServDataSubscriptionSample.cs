using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Example of shared data subscription using the generated tanks Configuration schema.
    /// </summary>
    public sealed class PlayServDataSubscriptionSample : MonoBehaviour
    {
        private const string SubscriptionPollingInfo = "3s";
        private const int UiLogTrimLimit = 220;
        private static readonly Regex RequestIdRegex =
            new Regex("\"RequestId\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        [Header("Target")]
        [SerializeField] private string configurationId = "default";

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showTransportDataGetLogsInUi = true;

        private readonly List<string> _history = new List<string>();
        private ISharedEntity<SampleConfigurationDto> _configuration;
        private IDisposable _configurationDisposable;
        private Vector2 _historyScroll;
        private string _status = "Not subscribed";
        private SampleConfigurationDto _snapshot;
        private bool _showInfo;
        private bool _asyncMutationInProgress;
        private bool _logHooked;
        private string _activeBackend = "none";

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
            _ = BindPollingAsync();
        }

        [ContextMenu("Bind Transport")]
        public void BindTransport()
        {
            _ = BindTransportAsync();
        }

        [ContextMenu("Bind Polling")]
        public void BindPolling()
        {
            _ = BindPollingAsync();
        }

        [ContextMenu("Unbind")]
        public void Unbind()
        {
            UnbindInternal();
        }

        [ContextMenu("Increase Tank Speed")]
        public void IncreaseTankSpeed()
        {
            if (!CanUseConfiguration("Tank speed"))
                return;

            _configuration.Update(dto =>
            {
                EnsureDto(dto);
                dto.Tank.TankSpeed = Round2(dto.Tank.TankSpeed + 0.25f);
            });

            _status = "Tank speed increase requested: +0.25";
            AddLog(_status);
        }

        [ContextMenu("Add Max HP")]
        public void AddMaxHp()
        {
            if (!CanUseConfiguration("Max HP"))
                return;

            _configuration.Update(dto =>
            {
                EnsureDto(dto);
                dto.Battle.MaxHP += 1;
            });

            _status = "Max HP increase requested: +1";
            AddLog(_status);
        }

        [ContextMenu("Add Heal Amount")]
        public void AddHealAmount()
        {
            if (!CanUseConfiguration("Heal amount"))
                return;

            _configuration.Update(dto =>
            {
                EnsureDto(dto);
                dto.Battle.HeathPickups.HealAmount += 1;
            });

            _status = "Heal amount increase requested: +1";
            AddLog(_status);
        }

        [ContextMenu("Reset Configuration")]
        public void ResetConfiguration()
        {
            if (!CanUseConfiguration("Reset config"))
                return;

            _configuration.Update(ApplyDefaultConfiguration);
            _status = "Configuration reset requested";
            AddLog(_status);
        }

        [ContextMenu("Boost Combat Async")]
        public void BoostCombatAsync()
        {
            _ = BoostCombatInternalAsync();
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            _ = RefreshInternalAsync();
        }

        private Task BindTransportAsync()
        {
            return BindInternalAsync(useTransport: true);
        }

        private Task BindPollingAsync()
        {
            return BindInternalAsync(useTransport: false);
        }

        private async Task BindInternalAsync(bool useTransport)
        {
            if (_configuration != null)
            {
                _status = "Already bound";
                AddLog(_status);
                return;
            }

            try
            {
                Func<Configuration, SampleConfigurationDto> map = MapConfiguration;

                if (useTransport)
                {
                    _configuration = await PlayServData.SelectEntity<Configuration, SampleConfigurationDto>(
                        configurationId,
                        map,
                        DataSubscriptionMode.Transport);
                    _activeBackend = "transport";
                }
                else
                {
                    // mode omitted on purpose: defaults to polling
                    _configuration = await PlayServData.SelectEntity<Configuration, SampleConfigurationDto>(configurationId, map);
                    _activeBackend = "polling";
                }

                _configuration.Changed += OnConfigurationChanged;
                _configuration.Error += OnConfigurationError;
                _configuration.Terminated += OnConfigurationTerminated;

                if (_configuration is IDisposable disposable)
                    _configurationDisposable = disposable;

                _snapshot = _configuration.Value;
                _status = $"Bound to Configuration(id={configurationId}), backend={_activeBackend}";
                AddLog(_status);
                AddLog($"Subscription backend: {DescribeBackend(_activeBackend)}");
                AddLog($"Initial snapshot: {BuildConfigSummary(_snapshot)}");
            }
            catch (Exception ex)
            {
                _status = $"Bind error: {ex.Message}";
                AddLog(_status);
            }
        }

        private async Task BoostCombatInternalAsync()
        {
            if (_configuration == null)
                return;

            if (_asyncMutationInProgress)
            {
                _status = "Async mutation is already in progress";
                AddLog(_status);
                return;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                _status = $"Boost combat skipped: SDK state is {PlayServ.State}";
                AddLog(_status);
                return;
            }

            try
            {
                _asyncMutationInProgress = true;
                await _configuration.UpdateAsync(dto =>
                {
                    EnsureDto(dto);
                    dto.Battle.MaxHP += 2;
                    dto.Battle.ProjectileDamage += 1;
                    dto.Battle.HeathPickups.HealAmount += 1;
                });

                _status = "Combat boost async requested: MaxHP +2, Damage +1, Heal +1";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Combat boost error: {ex.Message}";
                AddLog(_status);
            }
            finally
            {
                _asyncMutationInProgress = false;
            }
        }

        private async Task RefreshInternalAsync()
        {
            if (!CanUseConfiguration("Refresh"))
                return;

            try
            {
                await _configuration.RefreshAsync();
                _status = "Refresh requested";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Refresh error: {ex.Message}";
                AddLog(_status);
            }
        }

        private bool CanUseConfiguration(string actionName)
        {
            if (_configuration == null)
            {
                _status = $"{actionName} skipped: not bound";
                AddLog(_status);
                return false;
            }

            if (PlayServ.State != PlayServState.Online)
            {
                _status = $"{actionName} skipped: SDK state is {PlayServ.State}";
                AddLog(_status);
                return false;
            }

            return true;
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

        private void OnConfigurationChanged(SampleConfigurationDto dto)
        {
            _snapshot = dto;
            _status = "Configuration changed";
            AddLog($"Configuration updated: key={configurationId}, {BuildConfigSummary(dto)}");
        }

        private void OnConfigurationError(DataSubscriptionException ex)
        {
            _status = $"Subscription error [{ex.ErrorCode}]: {ex.Message}";
            AddLog(_status);
        }

        private void OnConfigurationTerminated()
        {
            _status = "Subscription terminated by server";
            AddLog(_status);
            UnbindInternal();
        }

        private void UnbindInternal()
        {
            if (_configuration != null)
            {
                _configuration.Changed -= OnConfigurationChanged;
                _configuration.Error -= OnConfigurationError;
                _configuration.Terminated -= OnConfigurationTerminated;
            }

            _configurationDisposable?.Dispose();
            _configurationDisposable = null;
            _configuration = null;
            _snapshot = null;
            _activeBackend = "none";
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

            if (ContainsLogFragment(condition, "Message sent: DataSubscriptionRequest"))
            {
                AddLog($"[Transport] -> module_dataflow.DataSubscriptionRequest requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "Message sent: DataSubscriptionRefreshRequest"))
            {
                AddLog($"[Transport] -> module_dataflow.DataSubscriptionRefreshRequest requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "Message sent: DataMutationRequest"))
            {
                AddLog($"[Transport] -> module_dataflow.DataMutationRequest requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "Message sent: DataGetRequest"))
            {
                AddLog($"[Transport] -> DataGetRequest requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "\"Command\":\"module_dataflow.DataSubscriptionResponse\"") ||
                ContainsLogFragment(condition, "\"Command\":\"DataSubscriptionResponse\""))
            {
                AddLog($"[Transport] <- DataSubscriptionResponse requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "\"Command\":\"module_dataflow.DataSubscriptionUpdate\"") ||
                ContainsLogFragment(condition, "\"Command\":\"DataSubscriptionUpdate\""))
            {
                AddLog($"[Transport] <- DataSubscriptionUpdate requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "\"Command\":\"module_dataflow.DataMutationResponse\"") ||
                ContainsLogFragment(condition, "\"Command\":\"DataMutationResponse\""))
            {
                AddLog($"[Transport] <- DataMutationResponse requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "\"Command\":\"module_dataflow.DataGetResponse\"") ||
                ContainsLogFragment(condition, "\"Command\":\"DataGetResponse\""))
            {
                AddLog($"[Transport] <- DataGetResponse requestId={ExtractRequestIdOrUnknown(condition)}");
                return;
            }

            if (ContainsLogFragment(condition, "\"Command\":\"RpcErrorResponse\"") ||
                ContainsLogFragment(condition, "\"Command\":\"rpc.RpcErrorResponse\""))
            {
                AddLog($"[Transport] <- RpcErrorResponse {BuildRpcErrorSummary(condition)}");
                return;
            }

            if (condition.IndexOf("Server command error received", StringComparison.OrdinalIgnoreCase) >= 0 ||
                condition.IndexOf("Server command warning received", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddLog($"[Transport] {TrimForUi(condition)}");
                return;
            }

            if (condition.IndexOf("[DataGet]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddLog($"[Transport] {TrimForUi(condition)}");
            }
        }

        private static bool ContainsLogFragment(string text, string fragment)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   !string.IsNullOrWhiteSpace(fragment) &&
                   text.IndexOf(fragment, StringComparison.Ordinal) >= 0;
        }

        private static string ExtractRequestIdOrUnknown(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "n/a";

            var match = RequestIdRegex.Match(text);
            return match.Success ? match.Groups[1].Value : "n/a";
        }

        private static string BuildRpcErrorSummary(string text)
        {
            var code = ExtractJsonStringFieldOrUnknown(text, "Code");
            var sourceCommand = ExtractJsonStringFieldOrUnknown(text, "SourceCommand");
            var sourceService = ExtractJsonStringFieldOrUnknown(text, "SourceService");
            var message = ExtractJsonStringFieldOrUnknown(text, "Message");

            return $"code={code}, sourceCommand={sourceCommand}, sourceService={sourceService}, message={message}";
        }

        private static string ExtractJsonStringFieldOrUnknown(string text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(fieldName))
                return "n/a";

            var regex = new Regex(
                $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"([^\"]*)\"");
            var match = regex.Match(text);
            return match.Success ? Regex.Unescape(match.Groups[1].Value) : "n/a";
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

        private static string DescribeBackend(string backend)
        {
            switch (backend)
            {
                case "transport":
                    return "Transport only (DataSubscriptionRequest/DataSubscriptionUpdate)";
                case "polling":
                    return $"Polling only (DataGetRequest every {SubscriptionPollingInfo})";
                default:
                    return "Not bound";
            }
        }

        private static SampleConfigurationDto MapConfiguration(Configuration entity)
        {
            var dto = new SampleConfigurationDto();
            ApplyDefaultConfiguration(dto);

            var tank = entity?.Tank;
            if (tank != null)
            {
                dto.Tank.TankSpeed = ReadFloat(tank.TankSpeed, dto.Tank.TankSpeed);
                dto.Tank.TankRadius = ReadFloat(tank.TankRadius, dto.Tank.TankRadius);
                dto.Tank.TankTurnSpeed = ReadInt(tank.TankTurnSpeed, dto.Tank.TankTurnSpeed);
                dto.Tank.TankAcceleration = ReadInt(tank.TankAcceleration, dto.Tank.TankAcceleration);
                dto.Tank.TankDeceleration = ReadInt(tank.TankDeceleration, dto.Tank.TankDeceleration);
                dto.Tank.TankTurnDecelFactor = ReadFloat(tank.TankTurnDecelFactor, dto.Tank.TankTurnDecelFactor);
                dto.Tank.TurretRotationSpeed = ReadFloat(tank.TurretRotationSpeed, dto.Tank.TurretRotationSpeed);
                dto.Tank.ReverseSpeedMultiplier = ReadFloat(tank.ReverseSpeedMultiplier, dto.Tank.ReverseSpeedMultiplier);
            }

            var arena = entity?.Arena;
            if (arena != null)
            {
                dto.Arena.ArenaRadius = ReadInt(arena.ArenaRadius, dto.Arena.ArenaRadius);
                dto.Arena.ObstacleCount = ReadInt(arena.ObstacleCount, dto.Arena.ObstacleCount);
                dto.Arena.ObstacleMinSize = ReadInt(arena.ObstacleMinSize, dto.Arena.ObstacleMinSize);
                dto.Arena.ObstacleMaxSize = ReadInt(arena.ObstacleMaxSize, dto.Arena.ObstacleMaxSize);
            }

            var battle = entity?.Battle;
            if (battle != null)
            {
                dto.Battle.MaxHP = ReadInt(battle.MaxHP, dto.Battle.MaxHP);
                dto.Battle.ReloadTime = ReadFloat(battle.ReloadTime, dto.Battle.ReloadTime);
                dto.Battle.RecoilForce = ReadFloat(battle.RecoilForce, dto.Battle.RecoilForce);
                dto.Battle.ProjectileSpeed = ReadFloat(battle.ProjectileSpeed, dto.Battle.ProjectileSpeed);
                dto.Battle.ProjectileDamage = ReadInt(battle.ProjectileDamage, dto.Battle.ProjectileDamage);
                dto.Battle.ProjectileRadius = ReadFloat(battle.ProjectileRadius, dto.Battle.ProjectileRadius);
                dto.Battle.ProjectileLifetime = ReadFloat(battle.ProjectileLifetime, dto.Battle.ProjectileLifetime);

                var pickups = battle.HeathPickups;
                if (pickups != null)
                {
                    dto.Battle.HeathPickups.HealAmount = ReadInt(pickups.HealAmount, dto.Battle.HeathPickups.HealAmount);
                    dto.Battle.HeathPickups.MaxPickups = ReadInt(pickups.MaxPickups, dto.Battle.HeathPickups.MaxPickups);
                    dto.Battle.HeathPickups.PickupRadius = ReadInt(pickups.PickupRadius, dto.Battle.HeathPickups.PickupRadius);
                    dto.Battle.HeathPickups.PickupSpawnInterval = ReadInt(pickups.PickupSpawnInterval, dto.Battle.HeathPickups.PickupSpawnInterval);
                }
            }

            var connection = entity?.Connection;
            if (connection != null)
            {
                dto.Connection.GhostDuration = ReadInt(connection.GhostDuration, dto.Connection.GhostDuration);
                dto.Connection.ReconnectGrace = ReadInt(connection.ReconnectGrace, dto.Connection.ReconnectGrace);
            }

            dto.Colors = MapColors(entity?.Colors);
            return dto;
        }

        private static List<SampleColorEntryDto> MapColors(List<SampleColorEntryDto> colors)
        {
            var result = new List<SampleColorEntryDto>();
            if (colors == null || colors.Count == 0)
            {
                AddDefaultColors(result);
                return result;
            }

            for (var i = 0; i < colors.Count; i++)
            {
                var entry = colors[i];
                if (entry == null)
                    continue;

                result.Add(new SampleColorEntryDto
                {
                    Color = entry.Color ?? string.Empty,
                    R = entry.R,
                    G = entry.G,
                    B = entry.B
                });
            }

            if (result.Count == 0)
                AddDefaultColors(result);

            return result;
        }

        private static void AddDefaultColors(List<SampleColorEntryDto> colors)
        {
            var names = new[] { "Green", "Red", "Blue", "Yellow", "Orange", "Purple", "Cyan", "Pink", "White", "Black" };
            for (var i = 0; i < names.Length; i++)
                colors.Add(new SampleColorEntryDto { Color = names[i] });
        }

        private static void ApplyDefaultConfiguration(SampleConfigurationDto dto)
        {
            EnsureDto(dto);
            dto.Tank.TankSpeed = 3f;
            dto.Tank.TankRadius = 0.3f;
            dto.Tank.TankTurnSpeed = 90;
            dto.Tank.TankAcceleration = 4;
            dto.Tank.TankDeceleration = 6;
            dto.Tank.TankTurnDecelFactor = 0.4f;
            dto.Tank.TurretRotationSpeed = 150f;
            dto.Tank.ReverseSpeedMultiplier = 0.5f;

            dto.Arena.ArenaRadius = 50;
            dto.Arena.ObstacleCount = 25;
            dto.Arena.ObstacleMinSize = 2;
            dto.Arena.ObstacleMaxSize = 5;

            dto.Battle.MaxHP = 6;
            dto.Battle.ReloadTime = 1.5f;
            dto.Battle.RecoilForce = 0.5f;
            dto.Battle.ProjectileSpeed = 36f;
            dto.Battle.ProjectileDamage = 1;
            dto.Battle.ProjectileRadius = 0.3f;
            dto.Battle.ProjectileLifetime = 3f;
            dto.Battle.HeathPickups.HealAmount = 1;
            dto.Battle.HeathPickups.MaxPickups = 5;
            dto.Battle.HeathPickups.PickupRadius = 1;
            dto.Battle.HeathPickups.PickupSpawnInterval = 8;

            dto.Connection.GhostDuration = 5;
            dto.Connection.ReconnectGrace = 3;

            dto.Colors = new List<SampleColorEntryDto>();
            AddDefaultColors(dto.Colors);
        }

        private static void EnsureDto(SampleConfigurationDto dto)
        {
            if (dto.Tank == null)
                dto.Tank = new SampleTankConfDto();
            if (dto.Arena == null)
                dto.Arena = new SampleArenaConfDto();
            if (dto.Battle == null)
                dto.Battle = new SampleBattleConfDto();
            if (dto.Battle.HeathPickups == null)
                dto.Battle.HeathPickups = new SampleBattleHealthPickupConfDto();
            if (dto.Connection == null)
                dto.Connection = new SampleConnectionConfDto();
            if (dto.Colors == null)
                dto.Colors = new List<SampleColorEntryDto>();
        }

        private static float ReadFloat(float? value, float fallback)
        {
            return value.HasValue ? value.Value : fallback;
        }

        private static int ReadInt(int? value, int fallback)
        {
            return value.HasValue ? value.Value : fallback;
        }

        private static float Round2(float value)
        {
            return Mathf.Round(value * 100f) / 100f;
        }

        private static string BuildConfigSummary(SampleConfigurationDto dto)
        {
            if (dto == null)
                return "<null>";

            EnsureDto(dto);
            return $"tankSpeed={dto.Tank.TankSpeed:0.##}, maxHP={dto.Battle.MaxHP}, heal={dto.Battle.HeathPickups.HealAmount}, pickups={dto.Battle.HeathPickups.MaxPickups}, arena={dto.Arena.ArenaRadius}, colors={dto.Colors.Count}";
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
            GUILayout.Label("Schema: Configuration(id: default) from the tanks project. Connect, bind, mutate config fields, and watch DataGet/DataSubscription updates.");
            GUILayout.Label($"Target: Configuration(id={configurationId})");
            GUILayout.Label($"Active backend: {_activeBackend}");
            GUILayout.Label($"Backend details: {DescribeBackend(_activeBackend)}");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label("Transport commands: Bind Transport -> module_dataflow.DataSubscriptionRequest; mutation buttons -> module_dataflow.DataMutationRequest; Refresh -> module_dataflow.DataSubscriptionRefreshRequest.");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect SDK"))
                _ = ConnectSdkAsync();
            if (GUILayout.Button("Disconnect SDK"))
                DisconnectSdk();
            if (GUILayout.Button(_showInfo ? "Hide Info" : "Info"))
                _showInfo = !_showInfo;
            GUILayout.EndHorizontal();

            if (_showInfo)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Info");
                GUILayout.Label("Purpose: validates data subscriptions against the real tanks Configuration schema.");
                GUILayout.Label("Query root: Configuration(id: $id), id=default. Selection is generated from the loaded schema/model.");
                GUILayout.Label("Transport flow: Bind Transport -> module_dataflow.DataSubscriptionRequest/DataSubscriptionUpdate.");
                GUILayout.Label("Transport refresh: Refresh -> module_dataflow.DataSubscriptionRefreshRequest when bound through transport.");
                GUILayout.Label("Polling flow: Bind Polling -> SDK registry + DataGetRequest polling every 3s.");
                GUILayout.Label("Mutations: Tank Speed/Max HP/Heal/Reset/Boost Combat -> module_dataflow.DataMutationRequest.");
                GUILayout.Label("Use in tanks: live gameplay tuning for movement, combat, pickups, arena and reconnect settings.");
                GUILayout.Label("Server support: if refresh is not implemented by the target server, RpcErrorResponse is shown in the logs.");
                GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Bind Transport"))
                _ = BindTransportAsync();
            if (GUILayout.Button("Bind Polling"))
                _ = BindPollingAsync();
            if (GUILayout.Button("Unbind"))
                UnbindInternal();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            var canMutate = _configuration != null && PlayServ.State == PlayServState.Online;
            var prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && canMutate;
            if (GUILayout.Button("Tank Speed +0.25"))
                IncreaseTankSpeed();
            if (GUILayout.Button("Max HP +1"))
                AddMaxHp();
            if (GUILayout.Button("Heal +1"))
                AddHealAmount();
            var boostLabel = _asyncMutationInProgress ? "Boost Combat Async (Running...)" : "Boost Combat Async";
            GUI.enabled = prevEnabled && canMutate && !_asyncMutationInProgress;
            if (GUILayout.Button(boostLabel))
                _ = BoostCombatInternalAsync();
            GUI.enabled = prevEnabled && canMutate;
            if (GUILayout.Button("Reset Defaults"))
                ResetConfiguration();
            if (GUILayout.Button("Refresh"))
                _ = RefreshInternalAsync();
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Current Configuration:");
            DrawSnapshot(_snapshot);

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

        private void DrawSnapshot(SampleConfigurationDto dto)
        {
            if (dto == null)
            {
                GUILayout.Label("- <null>");
                return;
            }

            EnsureDto(dto);
            GUILayout.Label($"- Key: {configurationId}");
            GUILayout.Label($"- Tank: speed={dto.Tank.TankSpeed:0.##}, turn={dto.Tank.TankTurnSpeed}, turret={dto.Tank.TurretRotationSpeed:0.##}, radius={dto.Tank.TankRadius:0.##}");
            GUILayout.Label($"- Battle: maxHP={dto.Battle.MaxHP}, damage={dto.Battle.ProjectileDamage}, reload={dto.Battle.ReloadTime:0.##}, projectileSpeed={dto.Battle.ProjectileSpeed:0.##}");
            GUILayout.Label($"- Pickups: heal={dto.Battle.HeathPickups.HealAmount}, max={dto.Battle.HeathPickups.MaxPickups}, radius={dto.Battle.HeathPickups.PickupRadius}, spawn={dto.Battle.HeathPickups.PickupSpawnInterval}s");
            GUILayout.Label($"- Arena: radius={dto.Arena.ArenaRadius}, obstacles={dto.Arena.ObstacleCount}, size={dto.Arena.ObstacleMinSize}-{dto.Arena.ObstacleMaxSize}");
            GUILayout.Label($"- Connection: ghost={dto.Connection.GhostDuration}s, reconnectGrace={dto.Connection.ReconnectGrace}s");
            GUILayout.Label($"- Colors: {BuildColorPreview(dto.Colors)}");
        }

        private static string BuildColorPreview(List<SampleColorEntryDto> colors)
        {
            if (colors == null || colors.Count == 0)
                return "<none>";

            var parts = new List<string>();
            for (var i = 0; i < colors.Count && i < 6; i++)
            {
                var color = colors[i];
                if (color != null && !string.IsNullOrWhiteSpace(color.Color))
                    parts.Add(color.Color);
            }

            if (colors.Count > 6)
                parts.Add($"+{colors.Count - 6} more");

            return parts.Count == 0 ? $"{colors.Count} item(s)" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Local schema mirror. Type name must stay "Configuration" so query generation targets
    /// the tanks schema entity "Configuration".
    /// </summary>
    [Serializable]
    public sealed class Configuration : SampleConfigurationDto
    {
    }

    [Serializable]
    public class SampleConfigurationDto
    {
        public SampleTankConfDto Tank { get; set; } = new SampleTankConfDto();
        public SampleArenaConfDto Arena { get; set; } = new SampleArenaConfDto();
        public SampleBattleConfDto Battle { get; set; } = new SampleBattleConfDto();
        public List<SampleColorEntryDto> Colors { get; set; } = new List<SampleColorEntryDto>();
        public SampleConnectionConfDto Connection { get; set; } = new SampleConnectionConfDto();
    }

    [Serializable]
    public sealed class SampleTankConfDto
    {
        public float TankSpeed { get; set; }
        public float TankRadius { get; set; }
        public int TankTurnSpeed { get; set; }
        public int TankAcceleration { get; set; }
        public int TankDeceleration { get; set; }
        public float TankTurnDecelFactor { get; set; }
        public float TurretRotationSpeed { get; set; }
        public float ReverseSpeedMultiplier { get; set; }
    }

    [Serializable]
    public sealed class SampleArenaConfDto
    {
        public int ArenaRadius { get; set; }
        public int ObstacleCount { get; set; }
        public int ObstacleMaxSize { get; set; }
        public int ObstacleMinSize { get; set; }
    }

    [Serializable]
    public sealed class SampleBattleConfDto
    {
        public int MaxHP { get; set; }
        public float ReloadTime { get; set; }
        public float RecoilForce { get; set; }
        public SampleBattleHealthPickupConfDto HeathPickups { get; set; } = new SampleBattleHealthPickupConfDto();
        public float ProjectileSpeed { get; set; }
        public int ProjectileDamage { get; set; }
        public float ProjectileRadius { get; set; }
        public float ProjectileLifetime { get; set; }
    }

    [Serializable]
    public sealed class SampleBattleHealthPickupConfDto
    {
        public int HealAmount { get; set; }
        public int MaxPickups { get; set; }
        public int PickupRadius { get; set; }
        public int PickupSpawnInterval { get; set; }
    }

    [Serializable]
    public sealed class SampleConnectionConfDto
    {
        public int GhostDuration { get; set; }
        public int ReconnectGrace { get; set; }
    }

    [Serializable]
    public sealed class SampleColorEntryDto
    {
        public float B { get; set; }
        public float G { get; set; }
        public float R { get; set; }
        public string Color { get; set; } = string.Empty;
    }
}
