using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    public enum PlayServUplinkSubscriptionState { Opening, Recovering, Live, Observed, Disconnected, Faulted, Disposed }

    /// <summary>An immutable current-state change, not a lossless event log.</summary>
    public sealed class PlayServUplinkDataChange<T>
    {
        private readonly string _json;
        internal PlayServUplinkDataChange(string key, string epoch, long? revision, bool deleted, string json)
        { Key = key; Epoch = epoch; Revision = revision; Deleted = deleted; _json = json; }
        public string Key { get; }
        public string Epoch { get; }
        public long? Revision { get; }
        public bool Deleted { get; }
        public T Value => PlayServUplinkData.ReadValue<T>(_json);
    }

    /// <summary>
    /// A process-owned route handle, surviving natural uplink reconnects. A governed Live snapshot is complete;
    /// an ungoverned Observed snapshot contains only rows actually received. Dispose releases this handle only.
    /// </summary>
    public sealed class PlayServUplinkDataSubscription<T> : IDisposable, IPlayServUplinkDataListener
    {
        private readonly PlayServGameServerUplink _uplink;
        private readonly PlayServGameServerUplink.DataRoute _route;
        private int _disposed;
        internal PlayServUplinkDataSubscription(PlayServGameServerUplink uplink, PlayServGameServerUplink.DataRoute route)
        { _uplink = uplink; _route = route; }
        public string Entity => _route.Entity;
        public string KeyPath => _route.KeyPath;
        public string Epoch => _uplink.ReadRouteEpoch(_route);
        public long? Through => _uplink.ReadRouteThrough(_route);
        public PlayServUplinkSubscriptionState State => IsDisposed ? PlayServUplinkSubscriptionState.Disposed : _uplink.ReadRouteState(_route);
        public bool IsComplete => State == PlayServUplinkSubscriptionState.Live;
        /// <summary>Defensive copies; no partial recovery snapshot is advertised as complete.</summary>
        public IReadOnlyDictionary<string, T> Snapshot
        {
            get
            {
                var copy = new Dictionary<string, T>(StringComparer.Ordinal);
                try { foreach (var row in _uplink.ReadRouteRows(_route)) copy.Add(row.Key, PlayServUplinkData.ReadValue<T>(row.Value)); }
                catch { throw PlayServUplinkData.InvalidResult(); }
                return new ReadOnlyDictionary<string, T>(copy);
            }
        }
        public event Action<PlayServUplinkDataChange<T>> Changed;
        public event Action<PlayServUplinkSubscriptionState> StateChanged;
        public event Action<PlayServError> Failed;
        /// <summary>Explicit bounded catch-up after a surfaced recovery error; it never repeats a write.</summary>
        public Task RefreshAsync(CancellationToken ct = default)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(PlayServUplinkDataSubscription<T>));
            return _uplink.RefreshDataRouteAsync(_route, ct);
        }
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _uplink.ReleaseDataRoute(_route, this); }
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        bool IPlayServUplinkDataListener.Disposed => IsDisposed;
        void IPlayServUplinkDataListener.Change(string key, string epoch, long? revision, bool deleted, string json) =>
            Changed?.Invoke(new PlayServUplinkDataChange<T>(key, epoch, revision, deleted, json));
        void IPlayServUplinkDataListener.Status(PlayServUplinkSubscriptionState state, PlayServError error)
        { StateChanged?.Invoke(state); if (error != null && error.IsError) Failed?.Invoke(error); }
    }

    internal interface IPlayServUplinkDataListener
    {
        bool Disposed { get; }
        void Change(string key, string epoch, long? revision, bool deleted, string json);
        void Status(PlayServUplinkSubscriptionState state, PlayServError error);
    }

    public sealed partial class PlayServUplinkData
    {
        /// <summary>
        /// Subscribes using self, field:Name or relation:Link-&gt;Name. Identical routes share wire state;
        /// different key paths for one entity are refused. Initial governed state is a complete paginated baseline.
        /// </summary>
        public Task<PlayServUplinkDataSubscription<T>> SubscribeAsync<T>(string entityName, string keyPath = "self", CancellationToken ct = default)
        {
            Validate(entityName, "subscription", ct);
            if (!ValidKeyPath(keyPath)) throw new ArgumentException("Use self, field:Name or relation:Link->Name.", nameof(keyPath));
            return _uplink.SubscribeDataAsync<T>(_connection, entityName, keyPath, ct);
        }
        private static bool ValidKeyPath(string path)
        {
            if (path == "self") return true;
            if (string.IsNullOrEmpty(path) || path.Length > 256) return false;
            if (path.StartsWith("field:", StringComparison.Ordinal)) return Identifier(path.Substring(6));
            if (!path.StartsWith("relation:", StringComparison.Ordinal)) return false;
            var parts = path.Substring(9).Split(new[] { "->" }, StringSplitOptions.None);
            return parts.Length == 2 && Identifier(parts[0]) && Identifier(parts[1]);
        }
        private static bool Identifier(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }
        internal Task CheckSubscriptionAccessAsync(string entity, CancellationToken ct) =>
            EnsureAccessAsync(entity, "subscription", PlayServDataAccessOperation.Read, false, ct);
    }

    public sealed partial class PlayServGameServerUplink
    {
        internal sealed class DataRow
        {
            internal string Json;
            internal long Revision;
            internal long Arrival;
            internal bool Deleted;
        }
        internal sealed class DataRoute
        {
            internal string Entity, KeyPath, Epoch;
            internal long? Through;
            internal long? RequiredOwnAfter, RequiredOwnRevision;
            internal int Generation;
            internal long Bytes;
            internal long Arrival;
            internal long EpochVersion;
            internal bool Removed;
            internal DataConnection Connection;
            internal PlayServGameServerContext Context;
            internal PlayServUplinkSubscriptionState State;
            internal readonly Dictionary<string, DataRow> Rows = new Dictionary<string, DataRow>(StringComparer.Ordinal);
            internal readonly HashSet<string> Retired = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<IPlayServUplinkDataListener> Listeners = new List<IPlayServUplinkDataListener>();
            internal TaskCompletionSource<string> Ack;
            internal Task Ready;
            internal bool Recovering;
            internal readonly SemaphoreSlim RecoveryGate = new SemaphoreSlim(1, 1);
            internal readonly Dictionary<string, DataRow> PendingChanges = new Dictionary<string, DataRow>(StringComparer.Ordinal);
            internal long PendingBytes;
            internal int CallbackGeneration;
            internal string CallbackEpoch;
            internal bool PendingStatus;
            internal PlayServError PendingError;
        }
        private readonly Dictionary<string, DataRoute> _dataRoutes = new Dictionary<string, DataRoute>(StringComparer.Ordinal);
        private readonly HashSet<string> _dataUnsubscribes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<DataRoute> _dataCallbackRoutes = new HashSet<DataRoute>();
        private bool _dispatchingDataCallbacks;

        internal async Task<PlayServUplinkDataSubscription<T>> SubscribeDataAsync<T>(DataConnection connection, string entity, string keyPath, CancellationToken ct)
        {
            DataRoute route; PlayServUplinkDataSubscription<T> handle; Task ready;
            lock (_sync)
            {
                ValidateDataConnection(connection);
                if (_dataRoutes.TryGetValue(entity, out route))
                {
                    if (route.KeyPath != keyPath) throw new InvalidOperationException("An entity already has a subscription with another key path.");
                }
                else
                {
                    if (_dataRoutes.Count >= 128) throw UplinkErrors.Exception("uplink_subscription_limit");
                    route = new DataRoute { Entity = entity, KeyPath = keyPath, Context = connection.Context };
                    _dataRoutes.Add(entity, route);
                }
                handle = new PlayServUplinkDataSubscription<T>(this, route);
                route.Listeners.Add(handle);
                ready = route.Ready ?? StartDataRouteLocked(route, connection);
            }
            try { await WaitAsync(ready, ct).ConfigureAwait(false); return handle; }
            catch { handle.Dispose(); throw; }
        }

        private Task StartDataRouteLocked(DataRoute route, DataConnection connection)
        {
            foreach (var retired in _dataCallbackRoutes.Where(pending => pending.Removed).ToArray()) ClearDataCallbacksLocked(retired);
            if (route.Connection != null && (route.Connection.Authority == null) != (connection.Authority == null))
            {
                route.Epoch = null; route.Through = null; route.EpochVersion++;
                route.RequiredOwnAfter = null; route.RequiredOwnRevision = null;
                route.Rows.Clear(); route.Retired.Clear(); route.Bytes = 0;
            }
            route.Connection = connection; route.Generation++;
            route.Ack = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ObserveDataTask(route.Ack.Task);
            SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Opening);
            var generation = route.Generation;
            var epoch = route.Through.HasValue ? route.Epoch : "";
            var after = route.Through ?? 0;
            route.Ready = Task.Run(() => OpenDataRouteAsync(route, connection, generation, epoch, after));
            ObserveDataTask(route.Ready);
            return route.Ready;
        }

        private async Task OpenDataRouteAsync(DataRoute route, DataConnection connection, int generation, string epoch, long after)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
            budget.CancelAfter(connection.Context.HttpTimeout);
            try
            {
                var access = new PlayServUplinkData(this, connection).CheckSubscriptionAccessAsync(route.Entity, budget.Token);
                ObserveDataTask(access);
                await WaitAsync(access, budget.Token).ConfigureAwait(false);
                Task send;
                lock (_sync)
                {
                    ValidateRoute(route, connection, generation);
                    send = SendDataFrameAsync(connection, DataFrame(new { type = "subscribe_data", entity = route.Entity, key_path = route.KeyPath, row_deletes = true }), budget.Token);
                }
                await send.ConfigureAwait(false);
                if (connection.Authority == null)
                {
                    lock (_sync) { ValidateRoute(route, connection, generation); SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Observed); }
                    return;
                }
                await WaitAsync(route.Ack.Task, budget.Token).ConfigureAwait(false);
                var ack = PlayServUplinkData.ParseReply(await route.Ack.Task.ConfigureAwait(false));
                ThrowDataFrameError(ack);
                if (!SafeText(Text(ack, "epoch"), 256) || !Revision(ack, "through", out _)) throw PlayServUplinkData.InvalidResult();
                // A reconnect ack is current, not evidence that this SDK saw the changes missed while disconnected.
                await CatchUpDataRouteAsync(route, connection, generation, budget.Token, epoch, after).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var error = SubscriptionError(ex);
                lock (_sync)
                    if (RouteCurrent(route, connection, generation)) SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, error);
                throw new PlayServGameServerException(error);
            }
        }

        private async Task CatchUpDataRouteAsync(DataRoute route, DataConnection connection, int generation, CancellationToken ct,
            string checkpointEpoch = null, long? checkpointAfter = null, string changedEpoch = null, string receiptEpoch = null)
        {
            await route.RecoveryGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    ValidateRoute(route, connection, generation);
                    if (receiptEpoch != null && route.Epoch != receiptEpoch) return;
                    if (changedEpoch != null && (route.Epoch != changedEpoch || route.State == PlayServUplinkSubscriptionState.Live)) return;
                    route.Recovering = true;
                }
                for (var attempt = 0; attempt < 10000; attempt++)
                {
                    lock (_sync) if (receiptEpoch != null && route.Epoch != receiptEpoch) return;
                    if (await CatchUpDataRoutePagesAsync(route, connection, generation, ct, checkpointEpoch, checkpointAfter).ConfigureAwait(false)) return;
                    checkpointEpoch = null; checkpointAfter = null;
                }
                throw UplinkErrors.Exception("uplink_subscription_limit");
            }
            catch (Exception ex)
            {
                lock (_sync)
                    if (RouteCurrent(route, connection, generation) && (receiptEpoch == null || route.Epoch == receiptEpoch))
                        SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, SubscriptionError(ex));
                throw;
            }
            finally
            {
                lock (_sync) if (route.Generation == generation) route.Recovering = false;
                route.RecoveryGate.Release();
            }
        }

        private async Task<bool> CatchUpDataRoutePagesAsync(DataRoute route, DataConnection connection, int generation, CancellationToken ct,
            string checkpointEpoch, long? checkpointAfter)
        {
            string requestEpoch; long after, startArrival, epochVersion;
            lock (_sync)
            {
                ValidateRoute(route, connection, generation);
                requestEpoch = checkpointEpoch ?? (route.Through.HasValue ? route.Epoch : "") ?? "";
                after = checkpointAfter ?? route.Through ?? 0;
                if (route.RequiredOwnAfter.HasValue && requestEpoch == route.Epoch)
                    after = Math.Min(after, route.RequiredOwnAfter.Value);
                if (route.RequiredOwnRevision == 0) { requestEpoch = ""; after = 0; }
                startArrival = route.Arrival;
                epochVersion = route.EpochVersion;
                SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Recovering);
            }
            string cursor = null, responseEpoch = null; bool? full = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
                for (var page = 0; page < 10000; page++)
                {
                    lock (_sync) ValidateRoute(route, connection, generation);
                    var id = Guid.NewGuid().ToString("N");
                    var frame = new Dictionary<string, object> { ["type"] = "data_changes", ["request_id"] = id,
                        ["entity"] = route.Entity, ["key_path"] = route.KeyPath, ["epoch"] = requestEpoch,
                        ["after"] = after.ToString(CultureInfo.InvariantCulture) };
                    if (cursor != null) frame["cursor"] = cursor;
                    var json = await ExecuteDataAsync(connection, id, DataFrame(frame), _ => Task.CompletedTask, ct).ConfigureAwait(false);
                    var body = PlayServUplinkData.ParseReply(json);
                    if (body == null || Text(body, "type") != "data_changes_result" || Text(body, "entity") != route.Entity || HasForeignClient(body)) throw PlayServUplinkData.InvalidResult();
                    ThrowDataFrameError(body);
                    if (!SafeText(Text(body, "epoch"), 256) || !body.TryGetValue("full", out var rawFull) || !(rawFull is bool isFull) ||
                        !body.TryGetValue("rows", out var rawRows) || !(rawRows is IList<object> rows)) throw PlayServUplinkData.InvalidResult();
                    var epoch = Text(body, "epoch");
                    lock (_sync)
                    {
                        ValidateRoute(route, connection, generation);
                        if (route.EpochVersion != epochVersion && route.Epoch != epoch) return false;
                    }
                    if (responseEpoch != null && responseEpoch != epoch && isFull)
                    {
                        lock (_sync)
                        {
                            ValidateRoute(route, connection, generation);
                            if (route.Retired.Contains(epoch)) throw PlayServUplinkData.InvalidResult();
                            SwitchRouteEpochLocked(route, epoch);
                            ObserveDataTask(RecoverChangedEpochAsync(route, connection, generation, epoch));
                        }
                        return false;
                    }
                    if (responseEpoch != null && (responseEpoch != epoch || full != isFull)) throw PlayServUplinkData.InvalidResult();
                    if (responseEpoch == null)
                    {
                        responseEpoch = epoch; full = isFull;
                        if (!isFull && requestEpoch != epoch) throw PlayServUplinkData.InvalidResult();
                        lock (_sync)
                        {
                            ValidateRoute(route, connection, generation);
                            if (route.Retired.Contains(epoch)) throw PlayServUplinkData.InvalidResult();
                            var epochChanged = route.Epoch != null && route.Epoch != epoch;
                            SwitchRouteEpochLocked(route, epoch);
                            if (epochChanged) ObserveDataTask(RecoverChangedEpochAsync(route, connection, generation, epoch));
                        }
                    }
                    lock (_sync)
                    {
                        ValidateRoute(route, connection, generation);
                        if (route.Epoch != epoch) throw PlayServUplinkData.InvalidResult();
                        foreach (var rawRow in rows)
                        {
                            if (!(rawRow is IDictionary<string, object> row)) throw PlayServUplinkData.InvalidResult();
                            var key = Text(row, "id");
                            if (key == null) throw PlayServUplinkData.InvalidResult();
                            seen.Add(key);
                            ApplyRouteRowLocked(route, row, true, epoch);
                        }
                    }
                    cursor = Text(body, "cursor");
                    if (body.TryGetValue("cursor", out var rawCursor) && rawCursor != null && !(rawCursor is string)) throw PlayServUplinkData.InvalidResult();
                    if (!string.IsNullOrEmpty(cursor))
                    {
                        if (cursor.Length > 16384 || !cursors.Add(cursor)) throw PlayServUplinkData.InvalidResult();
                        continue;
                    }
                    if (!Revision(body, "through", out var through) || through < after && !isFull) throw PlayServUplinkData.InvalidResult();
                    lock (_sync)
                    {
                        ValidateRoute(route, connection, generation);
                        if (route.Epoch != epoch) throw PlayServUplinkData.InvalidResult();
                        if (isFull)
                            foreach (var key in route.Rows.Where(pair => !seen.Contains(pair.Key) && pair.Value.Arrival <= startArrival && pair.Value.Revision <= through).Select(pair => pair.Key).ToArray())
                            {
                                route.Bytes -= RowBytes(key, route.Rows[key]); route.Rows.Remove(key);
                                NotifyRouteChangeLocked(route, key, null, true);
                            }
                        route.Through = Math.Max(route.Through ?? 0, through);
                        route.Ready = Task.CompletedTask;
                        if (route.RequiredOwnRevision.HasValue && through >= route.RequiredOwnRevision.Value &&
                            (isFull || requestEpoch == epoch && after <= route.RequiredOwnAfter.Value))
                        {
                            route.RequiredOwnRevision = null; route.RequiredOwnAfter = null;
                        }
                        if (route.RequiredOwnRevision.HasValue)
                            SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted,
                                UplinkErrors.Exception("uplink_subscription_recovery_required", true).UnifiedError);
                        else SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Live);
                    }
                    return true;
                }
                throw UplinkErrors.Exception("uplink_subscription_limit");
        }

        private void HandleDataSubscriptionFrame(string json, string type, IPlayServUplinkSocket socket)
        {
            var body = PlayServUplinkData.ParseReply(json);
            if (body == null || !SafeText(Text(body, "entity"), 256) || HasForeignClient(body)) return;
            DataRoute route; DataConnection connection; int generation;
            lock (_sync)
            {
                connection = _dataConnection;
                if (connection == null || !ReferenceEquals(connection.Socket, socket) || !_dataRoutes.TryGetValue(Text(body, "entity"), out route) || route.Removed) return;
                generation = route.Generation;
                if (type == "subscribe_data_ack") { route.Ack?.TrySetResult(json); return; }
                try
                {
                    if (connection.Authority != null && body.TryGetValue("authority", out var raw) && raw is IDictionary<string, object> authority)
                    {
                        var epoch = Text(authority, "epoch");
                        if (route.Retired.Contains(epoch ?? "")) return;
                        if (!SafeText(epoch, 256) || !Revision(authority, "through", out _)) throw PlayServUplinkData.InvalidResult();
                        if (route.Epoch == null) SwitchRouteEpochLocked(route, epoch);
                        else if (route.Epoch != epoch)
                        {
                            SwitchRouteEpochLocked(route, epoch);
                            SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Recovering);
                            ObserveDataTask(RecoverChangedEpochAsync(route, connection, generation, epoch));
                        }
                    }
                    ApplyRouteRowLocked(route, body, connection.Authority != null, route.Epoch);
                    if (!route.Recovering && route.State == PlayServUplinkSubscriptionState.Live &&
                        body.TryGetValue("authority", out var stamp) && stamp is IDictionary<string, object> fields && Revision(fields, "through", out var through))
                        route.Through = Math.Max(route.Through ?? 0, through);
                }
                catch (Exception ex) { SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, SubscriptionError(ex)); }
            }
        }

        private async Task RecoverChangedEpochAsync(DataRoute route, DataConnection connection, int generation, string epoch)
        {
            await Task.Yield();
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
                budget.CancelAfter(connection.Context.HttpTimeout);
                Task ready;
                lock (_sync) ready = route.Ready;
                if (ready != null && !ready.IsCompleted) { try { await WaitAsync(ready, budget.Token).ConfigureAwait(false); } catch { } }
                await CatchUpDataRouteAsync(route, connection, generation, budget.Token, changedEpoch: epoch).ConfigureAwait(false);
            }
            catch (Exception ex)
            { lock (_sync) if (RouteCurrent(route, connection, generation) && route.Epoch == epoch && route.State != PlayServUplinkSubscriptionState.Live) SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, SubscriptionError(ex)); }
        }

        private void ApplyRouteRowLocked(DataRoute route, IDictionary<string, object> frame, bool governed, string epoch)
        {
            var key = Text(frame, "id"); var operation = Text(frame, "op");
            if (key == null || key.Length > 4096 || (operation != "upsert" && operation != "delete") ||
                !frame.TryGetValue("data", out var rawData) || !(rawData is IDictionary<string, object>)) throw PlayServUplinkData.InvalidResult();
            long revision = 0;
            if (governed)
            {
                if (!frame.TryGetValue("authority", out var raw) || !(raw is IDictionary<string, object> authority) ||
                    Text(authority, "epoch") != epoch || !Revision(authority, "revision", out revision)) throw PlayServUplinkData.InvalidResult();
            }
            else if (frame.TryGetValue("version", out var rawVersion) && rawVersion != null && (!Integer(rawVersion, out revision) || revision < 0)) throw PlayServUplinkData.InvalidResult();
            if (route.Rows.TryGetValue(key, out var previous) && (governed ? revision <= previous.Revision : revision != 0 && revision < previous.Revision)) return;
            var row = new DataRow { Revision = revision, Arrival = ++route.Arrival, Deleted = operation == "delete", Json = operation == "delete" ? null : PlayServGameServerJson.Serialize(rawData) };
            var bytes = route.Bytes - (previous == null ? 0 : RowBytes(key, previous)) + RowBytes(key, row);
            if (previous == null && route.Rows.Count >= 100000 || bytes > 64L * 1024 * 1024) throw UplinkErrors.Exception("uplink_subscription_limit");
            route.Rows[key] = row; route.Bytes = bytes;
            NotifyRouteChangeLocked(route, key, row, row.Deleted);
        }

        private static long RowBytes(string key, DataRow row) => Encoding.UTF8.GetByteCount(key) + (row?.Json == null ? 0 : Encoding.UTF8.GetByteCount(row.Json)) + 32L;
        private void SwitchRouteEpochLocked(DataRoute route, string epoch)
        {
            if (route.Epoch == epoch) return;
            if (route.Epoch != null) route.Retired.Add(route.Epoch);
            route.Epoch = epoch; route.EpochVersion++; route.Through = null; route.Rows.Clear(); route.Bytes = 0;
            route.RequiredOwnAfter = null; route.RequiredOwnRevision = null;
        }
        private void NotifyRouteChangeLocked(DataRoute route, string key, DataRow row, bool deleted)
        {
            PrepareDataCallbackLocked(route);
            var exists = route.PendingChanges.TryGetValue(key, out var previous);
            var bytes = route.PendingBytes - (exists ? RowBytes(key, previous) : 0) + RowBytes(key, row);
            if (!route.PendingChanges.ContainsKey(key) && route.PendingChanges.Count >= 100000 || bytes > 64L * 1024 * 1024)
            {
                route.PendingChanges.Clear(); route.PendingBytes = 0;
                SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, UplinkErrors.Exception("uplink_subscription_limit").UnifiedError);
                return;
            }
            route.PendingChanges[key] = row; route.PendingBytes = bytes;
            PostDataCallbackLocked(route);
        }
        private void SetRouteStateLocked(DataRoute route, PlayServUplinkSubscriptionState state, PlayServError error = null)
        {
            if (route.State == state && error == null) return;
            route.State = state;
            PrepareDataCallbackLocked(route);
            route.PendingStatus = true;
            if (error != null) route.PendingError = error;
            PostDataCallbackLocked(route);
        }
        private static void PrepareDataCallbackLocked(DataRoute route)
        {
            if (route.CallbackGeneration == route.Generation && route.CallbackEpoch == route.Epoch) return;
            route.PendingChanges.Clear(); route.PendingBytes = 0;
            route.PendingStatus = false; route.PendingError = null;
            route.CallbackGeneration = route.Generation; route.CallbackEpoch = route.Epoch;
        }
        private void ClearDataCallbacksLocked(DataRoute route)
        {
            _dataCallbackRoutes.Remove(route);
            route.PendingChanges.Clear(); route.PendingBytes = 0;
            route.PendingStatus = false; route.PendingError = null;
        }
        private void PostDataCallbackLocked(DataRoute route)
        {
            _dataCallbackRoutes.Add(route);
            if (_dispatchingDataCallbacks) return;
            _dispatchingDataCallbacks = true;
            ObserveDataTask(Task.Run(() => route.Context.Post(DispatchDataCallbacks)));
        }
        private void DispatchDataCallbacks()
        {
            var callbacks = new List<Action>();
            lock (_sync)
            {
                foreach (var route in _dataCallbackRoutes)
                {
                    var generation = route.CallbackGeneration; var epoch = route.CallbackEpoch;
                    var listeners = route.Listeners.ToArray();
                    var changes = route.PendingChanges.ToArray();
                    var state = route.State; var status = route.PendingStatus; var error = route.PendingError;
                    route.PendingChanges.Clear(); route.PendingBytes = 0;
                    route.PendingStatus = false; route.PendingError = null;
                    callbacks.Add(() =>
                    {
                        foreach (var listener in listeners)
                        {
                            foreach (var change in changes)
                            {
                                lock (_sync) if (route.Removed || route.Generation != generation || route.Epoch != epoch || listener.Disposed) break;
                                var row = change.Value;
                                try { listener.Change(change.Key, epoch, epoch == null ? (long?)null : row?.Revision, row == null || row.Deleted, row?.Json); } catch { }
                            }
                            if (!status) continue;
                            lock (_sync) if (route.Generation != generation || route.Epoch != epoch || listener.Disposed) continue;
                            try { listener.Status(state, error); } catch { }
                        }
                    });
                }
                _dataCallbackRoutes.Clear();
            }
            foreach (var callback in callbacks) callback();
            lock (_sync)
            {
                _dispatchingDataCallbacks = false;
                if (_dataCallbackRoutes.Count != 0) PostDataCallbackLocked(_dataCallbackRoutes.First());
            }
        }
        private bool RouteCurrent(DataRoute route, DataConnection connection, int generation) => !route.Removed && route.Generation == generation &&
            ReferenceEquals(route.Connection, connection) && ReferenceEquals(_dataConnection, connection) && ReferenceEquals(PlayServGameServer.Uplink, this);
        private void ValidateRoute(DataRoute route, DataConnection connection, int generation)
        { if (!RouteCurrent(route, connection, generation)) throw UplinkErrors.Exception("uplink_subscription_closed"); ValidateDataConnection(connection); }
        private static bool HasForeignClient(IDictionary<string, object> body) => body.TryGetValue("client_key", out var raw) && raw != null && (!(raw is string s) || s.Length != 0);
        private static PlayServError SubscriptionError(Exception ex) => ex is PlayServGameServerException known ? known.UnifiedError :
            ex is PlayServDataException catalogue ? PlayServUplinkData.CatalogueFailure(catalogue).UnifiedError :
            ex is OperationCanceledException ? new PlayServError(PlayServErrorCode.Timeout, "request_timeout", "The uplink subscription did not complete within its budget.", retryable: true) :
            PlayServUplinkData.InvalidResult().UnifiedError;

        private void ResumeDataSubscriptions()
        {
            lock (_sync)
            {
                foreach (var entity in _dataUnsubscribes.ToArray())
                {
                    var connection = _dataConnection;
                    ObserveDataTask(SendDataFrameAsync(connection, DataFrame(new { type = "unsubscribe_data", entity }), connection.Token));
                    _dataUnsubscribes.Remove(entity);
                }
                foreach (var route in _dataRoutes.Values)
                    if (!ReferenceEquals(route.Connection, _dataConnection)) StartDataRouteLocked(route, _dataConnection);
            }
        }
        private void SuspendDataSubscriptions(DataConnection connection, PlayServGameServerException error)
        {
            var terminal = _state == PlayServUplinkState.Disconnected || _state == PlayServUplinkState.Terminated || _context.ShuttingDown;
            foreach (var route in _dataRoutes.Values)
            {
                if (!terminal && !ReferenceEquals(route.Connection, connection)) continue;
                route.Generation++; route.Recovering = false;
                route.Ack?.TrySetException(error);
                SetRouteStateLocked(route, terminal ? PlayServUplinkSubscriptionState.Disposed : PlayServUplinkSubscriptionState.Disconnected);
                if (terminal) route.Removed = true;
            }
            if (terminal) { _dataRoutes.Clear(); _dataUnsubscribes.Clear(); }
        }
        internal void ReleaseDataRoute(DataRoute route, IPlayServUplinkDataListener listener)
        {
            lock (_sync)
            {
                route.Listeners.Remove(listener);
                if (route.Removed || route.Listeners.Count != 0) return;
                route.Removed = true; route.Generation++; _dataRoutes.Remove(route.Entity);
                ClearDataCallbacksLocked(route);
                route.Ack?.TrySetException(UplinkErrors.Exception("uplink_subscription_closed"));
                if (_dataConnection != null && _state == PlayServUplinkState.Connected)
                    ObserveDataTask(SendDataFrameAsync(_dataConnection, DataFrame(new { type = "unsubscribe_data", entity = route.Entity }), _dataConnection.Token));
                else _dataUnsubscribes.Add(route.Entity);
            }
        }
        internal async Task RefreshDataRouteAsync(DataRoute route, CancellationToken ct)
        {
            DataConnection connection; int generation;
            lock (_sync)
            {
                connection = _dataConnection; generation = route.Generation;
                ValidateRoute(route, connection, generation);
                if (connection.Authority == null) throw new InvalidOperationException("Ungoverned routes provide observed updates, not complete catch-up.");
            }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, connection.Token);
            budget.CancelAfter(connection.Context.HttpTimeout);
            await CatchUpDataRouteAsync(route, connection, generation, budget.Token).ConfigureAwait(false);
        }
        internal string ReadRouteEpoch(DataRoute route) { lock (_sync) return route.Epoch; }
        internal long? ReadRouteThrough(DataRoute route) { lock (_sync) return route.Through; }
        internal PlayServUplinkSubscriptionState ReadRouteState(DataRoute route) { lock (_sync) return route.State; }
        internal Dictionary<string, string> ReadRouteRows(DataRoute route)
        { lock (_sync) return route.Rows.Where(pair => !pair.Value.Deleted).ToDictionary(pair => pair.Key, pair => pair.Value.Json, StringComparer.Ordinal); }

        private async Task ApplyOwnDataReceiptAsync(string entity, PlayServUplinkWriteOutcome receipt, DataConnection connection, CancellationToken ct)
        {
            DataRoute route; int generation;
            lock (_sync)
            {
                if (!_dataRoutes.TryGetValue(entity, out route) || route.Removed) return;
                if (!ReferenceEquals(route.Connection, connection) || !ReferenceEquals(_dataConnection, connection)) return;
                if (route.Retired.Contains(receipt.Epoch)) return;
                if (route.Epoch == null) SwitchRouteEpochLocked(route, receipt.Epoch);
                if (route.Epoch != receipt.Epoch) return;
                generation = route.Generation;
                string key = route.KeyPath == "self" ? receipt.RecordId : null;
                if (key == null && route.KeyPath.StartsWith("field:", StringComparison.Ordinal) && receipt.Data is IDictionary<string, object> data &&
                    data.TryGetValue(route.KeyPath.Substring(6), out var value) && value is string text) key = text;
                if (key != null)
                {
                    try
                    {
                        ApplyRouteRowLocked(route, new Dictionary<string, object> { ["id"] = key, ["op"] = receipt.Deleted ? "delete" : "upsert",
                            ["data"] = receipt.Data ?? new Dictionary<string, object>(),
                            ["authority"] = new Dictionary<string, object> { ["epoch"] = receipt.Epoch, ["revision"] = receipt.Revision.Value.ToString(CultureInfo.InvariantCulture) } }, true, receipt.Epoch);
                    }
                    catch (Exception ex)
                    {
                        RequireOwnDataRecoveryLocked(route, receipt.Revision.Value);
                        SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, SubscriptionError(ex));
                    }
                    return;
                }
                RequireOwnDataRecoveryLocked(route, receipt.Revision.Value);
                SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Recovering);
            }
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, connection.Token);
                budget.CancelAfter(connection.Context.HttpTimeout);
                await CatchUpDataRouteAsync(route, connection, generation, budget.Token, receiptEpoch: receipt.Epoch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_sync)
                    if (RouteCurrent(route, connection, generation) && route.Epoch == receipt.Epoch)
                        SetRouteStateLocked(route, PlayServUplinkSubscriptionState.Faulted, SubscriptionError(ex));
            }
        }

        private static void RequireOwnDataRecoveryLocked(DataRoute route, long revision)
        {
            var floor = Math.Max(0, revision - 1);
            route.RequiredOwnAfter = Math.Min(route.RequiredOwnAfter ?? floor, floor);
            route.RequiredOwnRevision = Math.Max(route.RequiredOwnRevision ?? 0, revision);
        }
    }
}
