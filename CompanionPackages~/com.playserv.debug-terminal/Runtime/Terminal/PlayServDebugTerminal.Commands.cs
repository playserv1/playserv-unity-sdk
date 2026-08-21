using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.RPC;
using Playserv.Events;
using Playserv.Proxy;
using Playserv.Proxy.Common;
using Playserv.Spawn;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private const int MinimumRpcTimeoutMs = 100;
        private const int MaximumRpcTimeoutMs = 120000;

        private readonly List<IDisposable> _typedEventSubscriptions = new List<IDisposable>();
        private readonly List<IDisposable> _rawEventSubscriptions = new List<IDisposable>();
        private CancellationTokenSource _rpcCancellation;
        private string _activeRpcRequestId = string.Empty;
        private string _lastRpcSummary = "none";
        private DateTimeOffset? _activeRpcStartedAt;
        private int _rpcTimeoutMs = 30000;

        private void ExecuteEventCommand(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "subscribe":
                    ExecuteEventSubscribe(parts);
                    return;
                case "unsubscribe":
                    ExecuteEventUnsubscribe(parts);
                    return;
                case "subscribe-raw":
                    AddEventSubscriptions(raw: true, count: 1);
                    return;
                case "unsubscribe-raw":
                    DisposeEventSubscriptions(raw: true, disposeAll: true);
                    return;
                case "status":
                    AddLog(
                        $"Event subscriptions: typedObservers={_typedEventSubscriptions.Count}; " +
                        $"rawObservers={_rawEventSubscriptions.Count}; logicalTopic={(_typedEventSubscriptions.Count + _rawEventSubscriptions.Count > 0 ? "active" : "off")}; " +
                        $"rpcNotifications={(_notificationSubscription != null ? "active" : "off")}; " +
                        $"groupJoined={_isGroupJoined}");
                    return;
                default:
                    AddLog("Usage: event <subscribe|unsubscribe|subscribe-raw|unsubscribe-raw|status> ...");
                    return;
            }
        }

        private int TypedEventSubscriptionCount => _typedEventSubscriptions.Count;

        private void ExecuteEventSubscribe(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 ||
                (!string.Equals(parts[2], "typed", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parts[2], "raw", StringComparison.OrdinalIgnoreCase)))
            {
                AddLog("Usage: event subscribe <typed|raw> [count]");
                return;
            }
            var count = 1;
            if (parts.Count > 3 && (!int.TryParse(parts[3], out count) || count < 1 || count > 32))
            {
                AddLog("Event observer count must be between 1 and 32.");
                return;
            }
            if (parts.Count > 4)
            {
                AddLog($"Unexpected event argument '{parts[4]}'.");
                return;
            }
            AddEventSubscriptions(string.Equals(parts[2], "raw", StringComparison.OrdinalIgnoreCase), count);
        }

        private void ExecuteEventUnsubscribe(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 ||
                (!string.Equals(parts[2], "typed", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parts[2], "raw", StringComparison.OrdinalIgnoreCase)))
            {
                AddLog("Usage: event unsubscribe <typed|raw> [one|all]");
                return;
            }
            var mode = parts.Count > 3 ? parts[3].ToLowerInvariant() : "all";
            if (mode != "one" && mode != "all")
            {
                AddLog("Event unsubscribe mode must be one or all.");
                return;
            }
            DisposeEventSubscriptions(
                string.Equals(parts[2], "raw", StringComparison.OrdinalIgnoreCase),
                mode == "all");
        }

        private void AddEventSubscriptions(bool raw, int count)
        {
            if (PlayServ.State != PlayServState.Online)
            {
                AddLog("Connect first.");
                return;
            }

            var leases = raw ? _rawEventSubscriptions : _typedEventSubscriptions;
            for (var index = 0; index < count; index++)
            {
                IDisposable lease = raw
                    ? PlayServEvents.SubscribeRaw<DebugTerminalChatEvent>().Subscribe(
                        payload => AddLog($"Raw event <= {payload}"),
                        OnEventSubscriptionError,
                        () => AddLog("Raw event topic completed."))
                    : PlayServEvents.Subscribe<DebugTerminalChatEvent>().Subscribe(
                        OnSampleEventReceived,
                        OnEventSubscriptionError,
                        () => AddLog("Typed event topic completed."));
                leases.Add(lease);
            }
            AddLog(
                $"{(raw ? "Raw" : "Typed")} DebugTerminalChatEvent observer(s) added; " +
                $"added={count}; total={leases.Count}.");
        }

        private void DisposeEventSubscriptions(bool raw, bool disposeAll)
        {
            var leases = raw ? _rawEventSubscriptions : _typedEventSubscriptions;
            var count = disposeAll ? leases.Count : Math.Min(1, leases.Count);
            for (var index = 0; index < count; index++)
            {
                var last = leases.Count - 1;
                leases[last].Dispose();
                leases.RemoveAt(last);
            }
            AddLog(
                $"{(raw ? "Raw" : "Typed")} DebugTerminalChatEvent observer(s) closed; " +
                $"closed={count}; remaining={leases.Count}.");
        }

        private void DisposeAllEventSubscriptions()
        {
            DisposeEventSubscriptions(raw: false, disposeAll: true);
            DisposeEventSubscriptions(raw: true, disposeAll: true);
        }

        private void OnEventSubscriptionError(Exception exception)
        {
            var error = exception is PlayServEventSubscriptionException rejected
                ? rejected.UnifiedError
                : new PlayServError(
                    PlayServErrorCode.SubscriptionTerminated,
                    "event_subscription_failed",
                    exception?.Message ?? "Event subscription failed.");
            OnPlayServError(error);
        }

        private async Task ExecuteRpcCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "named";
            switch (operation)
            {
                case "await":
                    await InvokeAwaitableRpcAsync(parts.Count > 2
                        ? string.Join(" ", parts.Skip(2))
                        : "hello from awaitable rpc");
                    return;
                case "timeout":
                    SetRpcTimeout(parts);
                    return;
                case "cancel":
                    CancelActiveRpc(silent: false);
                    return;
                case "status":
                    PrintRpcStatus();
                    return;
                case "expr":
                case "expression":
                case "args":
                case "named":
                    InvokeRpc(
                        operation,
                        parts.Count > 2 ? string.Join(" ", parts.Skip(2)) : "hello from rpc");
                    return;
                default:
                    AddLog("Usage: rpc <named|args|expr|await|timeout|cancel|status> ...");
                    return;
            }
        }

        private async Task InvokeAwaitableRpcAsync(string text)
        {
            if (_rpcCancellation != null)
            {
                AddLog($"Awaitable RPC '{_activeRpcRequestId}' is already running.");
                return;
            }

            _rpcCancellation = new CancellationTokenSource();
            _activeRpcRequestId = Guid.NewGuid().ToString("N");
            _activeRpcStartedAt = DateTimeOffset.UtcNow;
            var options = new PlayServRpcInvokeOptions
            {
                RequestId = _activeRpcRequestId,
                Timeout = TimeSpan.FromMilliseconds(_rpcTimeoutMs)
            };

            _status = $"Awaiting RPC {_activeRpcRequestId}";
            AddLog($"RPC await => request={_activeRpcRequestId}; timeout={_rpcTimeoutMs}ms; text={text}");
            try
            {
                var result = await PlayServRpc.InvokeAsync<Dictionary<string, object>, NotificationResult>(
                    NotificationServiceName,
                    BroadcastMethodName,
                    new Dictionary<string, object> { ["message"] = text },
                    options,
                    _rpcCancellation.Token);

                if (result.IsSuccess)
                {
                    _lastRpcSummary =
                        $"request={result.RequestId}; status={result.Status}; success={result.Value?.Success}; " +
                        $"message={result.Message}; resultError={result.Value?.Error}";
                    _status = "Awaitable RPC completed";
                }
                else
                {
                    _lastRpcSummary =
                        $"request={result.RequestId}; status={result.Status}; error={FormatError(result.UnifiedError)}";
                    _status = $"Awaitable RPC failed: {result.Error?.Code}";
                }
                AddLog(_lastRpcSummary);
            }
            catch (OperationCanceledException)
            {
                _lastRpcSummary = $"request={_activeRpcRequestId}; canceled";
                _status = "Awaitable RPC canceled";
                AddLog(_lastRpcSummary);
            }
            finally
            {
                _rpcCancellation.Dispose();
                _rpcCancellation = null;
                _activeRpcRequestId = string.Empty;
                _activeRpcStartedAt = null;
            }
        }

        private void SetRpcTimeout(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 ||
                !int.TryParse(parts[2], out var timeout) ||
                timeout < MinimumRpcTimeoutMs ||
                timeout > MaximumRpcTimeoutMs)
            {
                AddLog($"RPC timeout must be between {MinimumRpcTimeoutMs} and {MaximumRpcTimeoutMs} milliseconds.");
                return;
            }

            _rpcTimeoutMs = timeout;
            AddLog($"Awaitable RPC timeout set to {_rpcTimeoutMs}ms.");
        }

        private void CancelActiveRpc(bool silent)
        {
            if (_rpcCancellation == null)
            {
                if (!silent)
                    AddLog("No awaitable RPC is running.");
                return;
            }

            _rpcCancellation.Cancel();
            if (!silent)
                AddLog($"Cancellation requested for RPC '{_activeRpcRequestId}'.");
        }

        private void PrintRpcStatus()
        {
            if (_rpcCancellation == null)
            {
                AddLog($"Awaitable RPC: idle; timeout={_rpcTimeoutMs}ms; last={_lastRpcSummary}");
                return;
            }

            var elapsed = _activeRpcStartedAt.HasValue
                ? (DateTimeOffset.UtcNow - _activeRpcStartedAt.Value).TotalMilliseconds
                : 0d;
            AddLog(
                $"Awaitable RPC: running; request={_activeRpcRequestId}; elapsed={elapsed:F0}ms; " +
                $"timeout={_rpcTimeoutMs}ms");
        }

        private async Task ExecuteSpawnCommandAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count < 2)
            {
                await SpawnAsync(defaultSpawnAssetName);
                return;
            }

            var operation = parts[1].ToLowerInvariant();
            switch (operation)
            {
                case "scope":
                    AddLog($"Spawn scope: {PlayServSpawn.CurrentScope ?? "none"}");
                    return;
                case "join":
                    if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
                    {
                        AddLog("Usage: spawn join <group>");
                        return;
                    }
                    var joined = await PlayServSpawn.JoinSpawnScopeAsync(parts[2]);
                    AddLog(joined
                        ? $"Joined spawn scope '{PlayServSpawn.CurrentScope}'"
                        : $"Failed to join spawn scope '{parts[2]}'");
                    return;
                case "leave":
                    var left = await PlayServSpawn.LeaveSpawnScopeAsync();
                    AddLog(left ? "Left spawn scope." : "Failed to leave spawn scope.");
                    return;
                case "despawn":
                    Despawn(parts.Count > 2 ? parts[2] : "last");
                    return;
                default:
                    await SpawnAsync(parts[1]);
                    return;
            }
        }

        private void Despawn(string target)
        {
            var useLast = string.IsNullOrWhiteSpace(target) ||
                          string.Equals(target, "last", StringComparison.OrdinalIgnoreCase);
            var networkId = _lastSpawned == null
                ? string.Empty
                : _lastSpawned.GetComponent<NetworkObject>()?.NetworkId;
            var despawned = useLast
                ? _lastSpawned != null && PlayServSpawn.Despawn(_lastSpawned)
                : PlayServSpawn.Despawn(target);

            AddLog(despawned
                ? $"Despawn requested for '{(useLast ? networkId : target)}'."
                : $"Despawn failed for '{(useLast ? networkId : target)}'.");
            if (despawned && useLast)
                _lastSpawned = null;
        }
    }
}
