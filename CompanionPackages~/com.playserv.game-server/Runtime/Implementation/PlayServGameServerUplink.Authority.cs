using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Accepted state is not a durability acknowledgement. Unconfirmed means an ungoverned send only.</summary>
    public enum PlayServUplinkWriteState { Accepted, Rejected, Pending, Unknown, Unconfirmed }

    /// <summary>An immutable receipt or unresolved operation. Resolve keeps its original session, identity and payload.</summary>
    public sealed class PlayServUplinkWriteOutcome
    {
        private readonly string _data;
        private readonly Func<CancellationToken, Task<PlayServUplinkWriteOutcome>> _resolve;
        internal PlayServUplinkWriteOutcome(PlayServUplinkWriteState state, string operationId, string epoch,
            long? revision = null, string recordId = null, bool deleted = false, string data = null,
            PlayServError error = null, Func<CancellationToken, Task<PlayServUplinkWriteOutcome>> resolve = null)
        {
            State = state; OperationId = operationId; Epoch = epoch; Revision = revision; RecordId = recordId;
            Deleted = deleted; _data = data; Error = error ?? PlayServError.None; _resolve = resolve;
        }
        public PlayServUplinkWriteState State { get; }
        public string OperationId { get; }
        public string Epoch { get; }
        public long? Revision { get; }
        public string RecordId { get; }
        public bool Deleted { get; }
        public PlayServError Error { get; }
        /// <summary>A defensive copy of the accepted authorized state, never the submitted value.</summary>
        public object Data => _data == null ? null : PlayServUplinkData.ParseReply(_data);
        public T GetData<T>() => PlayServUplinkData.ReadValue<T>(_data);
        /// <summary>Queries the original operation and, if still unknown, resends the identical envelope within its retry window.</summary>
        public Task<PlayServUplinkWriteOutcome> ResolveAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return _resolve == null ? Task.FromResult(this) : _resolve(ct); }
    }

    /// <summary>A compatibility Send method did not obtain accepted state. The receipt can be inspected or resolved.</summary>
    public sealed class PlayServUplinkWriteException : Exception
    {
        internal PlayServUplinkWriteException(PlayServUplinkWriteOutcome outcome)
            : base("The uplink write has no confirmed acceptance.") { Outcome = outcome; }
        public PlayServUplinkWriteOutcome Outcome { get; }
        public PlayServError UnifiedError => Outcome.Error;
    }

    public sealed partial class PlayServUplinkData
    {
        /// <summary>Writes an object snapshot. An optional revision guards the write in a governed epoch.</summary>
        public Task<PlayServUplinkWriteOutcome> UpsertWithOutcomeAsync<T>(string entityName, string key, T value,
            long? expectedRevision = null, CancellationToken ct = default)
        {
            Validate(entityName, key, ct, true);
            if (value == null) throw new ArgumentNullException(nameof(value));
            string snapshot;
            try { snapshot = Codec.Serialize(value, JsonOptions); } catch { throw SerializationFailed(); }
            if (!(ParseReply(snapshot) is IDictionary<string, object>))
                throw new ArgumentException("The value must serialize as a JSON object.", nameof(value));
            return MutateAsync(entityName, key, "upsert", snapshot, false, expectedRevision, ct);
        }

        /// <summary>Applies signed 64-bit deltas, keeping one operation identity across bounded retries.</summary>
        public Task<PlayServUplinkWriteOutcome> IncrementWithOutcomeAsync(string entityName, string key,
            IReadOnlyDictionary<string, long> deltas, long? expectedRevision = null, CancellationToken ct = default)
        {
            Validate(entityName, key, ct);
            if (deltas == null) throw new ArgumentNullException(nameof(deltas));
            if (deltas.Count == 0) throw new ArgumentException("At least one field delta is required.", nameof(deltas));
            var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in deltas) { GameServerServiceValues.RequireText(pair.Key, nameof(deltas)); snapshot.Add(pair.Key, pair.Value); }
            return MutateAsync(entityName, key, "increment", Codec.Serialize(snapshot, JsonOptions), true, expectedRevision, ct);
        }

        /// <summary>Deletes an ordinary row. Singleton deletion is not supported by this dataflow path.</summary>
        public Task<PlayServUplinkWriteOutcome> DeleteWithOutcomeAsync(string entityName, string key,
            long? expectedRevision = null, CancellationToken ct = default)
        {
            Validate(entityName, key, ct);
            return MutateAsync(entityName, key, "delete", "{}", true, expectedRevision, ct);
        }

        private Task<PlayServUplinkWriteOutcome> MutateAsync(string entity, string key, string operation,
            string snapshot, bool rejectSingleton, long? expected, CancellationToken ct)
        {
            if (expected < 0) throw new ArgumentOutOfRangeException(nameof(expected));
            if (Encoding.UTF8.GetByteCount(snapshot) > PlayServUplinkSocket.MaxFrameBytes)
                throw UplinkErrors.Exception("frame_too_large");
            if (expected.HasValue && _connection.Authority == null)
                throw new InvalidOperationException("Expected revision requires negotiated state authority.");
            return _uplink.MutateDataAsync(_connection, entity, key, operation, snapshot, expected,
                token => EnsureAccessAsync(entity, key, PlayServDataAccessOperation.Write, rejectSingleton, token), ct);
        }

        private static async Task RequireAcceptedAsync(Task<PlayServUplinkWriteOutcome> operation)
        {
            var outcome = await operation.ConfigureAwait(false);
            if (outcome.State != PlayServUplinkWriteState.Accepted && outcome.State != PlayServUplinkWriteState.Unconfirmed)
                throw new PlayServUplinkWriteException(outcome);
        }
    }

    public sealed partial class PlayServGameServerUplink
    {
        internal sealed class DataAuthorityContext
        {
            internal string Epoch, Session;
            internal double Until;
        }

        private sealed class DataMutation
        {
            internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
            internal PlayServGameServerContext Context;
            internal CancellationToken Lifetime;
            internal DataAuthorityContext Authority;
            internal string Entity, Key, Operation, Snapshot, Id;
            internal long? Expected;
            internal bool Sent, Ambiguous;
            internal PlayServUplinkWriteOutcome Terminal;
        }

        private DataAuthorityContext ReadHelloAuthority(string json, double started)
        {
            var body = PlayServUplinkData.ParseReply(json);
            if (body == null || !body.TryGetValue("authority", out var raw) || !(raw is IDictionary<string, object> authority) ||
                !authority.TryGetValue("enabled", out var flag) || !(flag is bool enabled))
                throw new PlayServUplinkFailure("uplink_invalid_authority", true);
            if (!enabled)
            {
                if (Text(authority, "reason") == "not_enabled") return null;
                if (Text(authority, "reason") == "authority_unavailable") throw new PlayServUplinkFailure("authority_unavailable", false);
                throw new PlayServUplinkFailure("uplink_invalid_authority", true);
            }
            try { return ReadAuthorityContext(authority, started); }
            catch { throw new PlayServUplinkFailure("uplink_invalid_authority", true); }
        }

        private DataAuthorityContext ReadAuthorityContext(IDictionary<string, object> authority, double started)
        {
            if (Text(authority, "capability") != "authoritative_state_v1" ||
                !SafeText(Text(authority, "epoch"), 256) || !SafeText(Text(authority, "retry_session"), 16384) ||
                !authority.TryGetValue("retry_window_ms", out var raw) || !Integer(raw, out var window) || window <= 0 ||
                window > int.MaxValue || started + window / 1000d <= Seconds()) throw PlayServUplinkData.InvalidResult();
            return new DataAuthorityContext { Epoch = Text(authority, "epoch"), Session = Text(authority, "retry_session"), Until = started + window / 1000d };
        }

        internal async Task<PlayServUplinkWriteOutcome> MutateDataAsync(DataConnection connection, string entity, string key,
            string operation, string snapshot, long? expected, Func<CancellationToken, Task> authorize, CancellationToken ct)
        {
            CancellationToken lifetime;
            lock (_sync) { ValidateDataConnection(connection); lifetime = _stop.Token; }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
            budget.CancelAfter(connection.Context.HttpTimeout);
            try
            {
                var authorization = authorize(budget.Token);
                ObserveDataTask(authorization);
                await WaitAsync(authorization, budget.Token).ConfigureAwait(false);
                if (connection.Authority == null)
                {
                    var raw = new Dictionary<string, object> { ["type"] = "data_write", ["entity"] = entity, ["id"] = key,
                        ["op"] = operation, ["data"] = PlayServUplinkData.ParseReply(snapshot) };
                    await SendDataFrameAsync(connection, DataFrame(raw), budget.Token).ConfigureAwait(false);
                    return new PlayServUplinkWriteOutcome(PlayServUplinkWriteState.Unconfirmed, null, null);
                }
                var authority = await GetDataAuthorityAsync(connection, budget.Token).ConfigureAwait(false);
                var mutation = new DataMutation
                {
                    Context = connection.Context, Lifetime = lifetime, Authority = authority, Entity = entity, Key = key,
                    Operation = operation, Snapshot = snapshot, Expected = expected, Id = Guid.NewGuid().ToString("D")
                };
                // The retry budget includes authorization and context refresh; a natural reconnect is allowed within it.
                return await ExecuteMutationAsync(mutation, false, ct, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            { throw new PlayServGameServerException(new PlayServError(PlayServErrorCode.Timeout, "request_timeout", "The uplink write preparation timed out.", retryable: true)); }
            catch (PlayServDataException error) { throw PlayServUplinkData.CatalogueFailure(error); }
        }

        private async Task<DataAuthorityContext> GetDataAuthorityAsync(DataConnection connection, CancellationToken ct)
        {
            Task<DataAuthorityContext> refresh;
            lock (_sync)
            {
                ValidateDataConnection(connection);
                if (connection.Authority.Until - Seconds() > 5) return connection.Authority;
                refresh = connection.AuthorityRefresh;
                if (refresh == null || refresh.IsCompleted)
                {
                    refresh = connection.AuthorityRefresh = RefreshDataAuthorityAsync(connection);
                    ObserveDataTask(refresh);
                }
            }
            await WaitAsync(refresh, ct).ConfigureAwait(false);
            return await refresh.ConfigureAwait(false);
        }

        private async Task<DataAuthorityContext> RefreshDataAuthorityAsync(DataConnection connection)
        {
            var started = Seconds(); var id = Guid.NewGuid().ToString("N");
            var json = await ExecuteDataAsync(connection, id, DataFrame(new { type = "authority_context_request", request_id = id }),
                _ => Task.CompletedTask, connection.Token).ConfigureAwait(false);
            var body = PlayServUplinkData.ParseReply(json);
            if (body == null || Text(body, "type") != "authority_context") throw PlayServUplinkData.InvalidResult();
            ThrowDataFrameError(body);
            if (!body.TryGetValue("authority", out var raw) || !(raw is IDictionary<string, object> value)) throw PlayServUplinkData.InvalidResult();
            var authority = ReadAuthorityContext(value, started);
            lock (_sync) { ValidateDataConnection(connection); connection.Authority = authority; }
            return authority;
        }

        private async Task<PlayServUplinkWriteOutcome> ResolveMutationAsync(DataMutation mutation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, mutation.Lifetime);
            budget.CancelAfter(mutation.Context.HttpTimeout);
            try { await mutation.Gate.WaitAsync(budget.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                return Unresolved(mutation, PlayServUplinkWriteState.Unknown,
                    new PlayServError(PlayServErrorCode.Timeout, "uplink_write_unknown",
                        "Resolution did not finish within its budget; retain the original operation."));
            }
            try
            {
                if (mutation.Terminal != null) return mutation.Terminal;
                return await ExecuteMutationAsync(mutation, true, ct, budget.Token).ConfigureAwait(false);
            }
            finally { mutation.Gate.Release(); }
        }

        private async Task<PlayServUplinkWriteOutcome> ExecuteMutationAsync(DataMutation mutation, bool statusFirst,
            CancellationToken caller, CancellationToken budget)
        {
            PlayServUplinkWriteOutcome last = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    budget.ThrowIfCancellationRequested();
                    if (mutation.Lifetime.IsCancellationRequested || mutation.Authority.Until <= Seconds()) break;
                    var connection = await MutationConnectionAsync(mutation, budget).ConfigureAwait(false);
                    if (connection.Authority == null || connection.Authority.Epoch != mutation.Authority.Epoch) break;
                    var requestId = Guid.NewGuid().ToString("N");
                    object frame;
                    if (statusFirst)
                        frame = new { type = "authority_status_request", request_id = requestId, epoch = mutation.Authority.Epoch,
                            retry_session = mutation.Authority.Session, operation_id = mutation.Id };
                    else
                    {
                        var authority = new Dictionary<string, object>
                        { ["epoch"] = mutation.Authority.Epoch, ["retry_session"] = mutation.Authority.Session, ["operation_id"] = mutation.Id };
                        if (mutation.Expected.HasValue) authority["expected_revision"] = mutation.Expected.Value.ToString(CultureInfo.InvariantCulture);
                        frame = new Dictionary<string, object> { ["type"] = "data_write", ["request_id"] = requestId,
                            ["entity"] = mutation.Entity, ["id"] = mutation.Key, ["op"] = mutation.Operation,
                            ["data"] = PlayServUplinkData.ParseReply(mutation.Snapshot), ["authority"] = authority };
                    }
                    using var attemptBudget = CancellationTokenSource.CreateLinkedTokenSource(budget);
                    attemptBudget.CancelAfter(TimeSpan.FromSeconds(Math.Min(5, Math.Max(.001, mutation.Authority.Until - Seconds()))));
                    var json = await ExecuteDataAsync(connection, requestId, DataFrame(frame), _ => Task.CompletedTask,
                        attemptBudget.Token, () => { if (!statusFirst) mutation.Sent = true; }).ConfigureAwait(false);
                    var body = PlayServUplinkData.ParseReply(json);
                    if (body == null || Text(body, "type") != (statusFirst ? "authority_status" : "data_write_result")) throw PlayServUplinkData.InvalidResult();
                    if (statusFirst)
                    {
                        ThrowDataFrameError(body);
                        if (!body.TryGetValue("known", out var known) || !(known is bool isKnown)) throw PlayServUplinkData.InvalidResult();
                        if (!isKnown) { statusFirst = false; continue; }
                    }
                    else if (Text(body, "entity") != mutation.Entity || Text(body, "id") != mutation.Key) throw PlayServUplinkData.InvalidResult();
                    if (!body.TryGetValue("authority", out var raw) || !(raw is IDictionary<string, object> receipt)) throw PlayServUplinkData.InvalidResult();
                    last = ReadWriteOutcome(mutation, receipt, !statusFirst);
                    if (last.State == PlayServUplinkWriteState.Accepted)
                    {
                        await ApplyOwnDataReceiptAsync(mutation.Entity, last, connection, budget).ConfigureAwait(false);
                        mutation.Terminal = last;
                        return last;
                    }
                    if (last.State == PlayServUplinkWriteState.Rejected)
                    {
                        // A new-session refusal cannot disprove an earlier unknown send.
                        if (mutation.Ambiguous && (Text(receipt, "operation_id") == null || last.Error.TransportCode == 32101 || last.Error.TransportCode == 32103 ||
                            last.Error.TransportCode == 32104 || last.Error.TransportCode == 32106 || last.Error.TransportCode == 32107 ||
                            last.Error.TransportCode == 32111)) break;
                        mutation.Terminal = last;
                        return last;
                    }
                    statusFirst = false;
                    await Delay(100, budget).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { mutation.Ambiguous |= mutation.Sent; if (budget.IsCancellationRequested) break; statusFirst = false; }
                catch (PlayServGameServerException ex)
                {
                    if (!mutation.Sent) throw;
                    mutation.Ambiguous = true;
                    if (ex.UnifiedError.Code == PlayServErrorCode.InvalidResponse || ex.UnifiedError.SourceCode == "frame_too_large" ||
                        statusFirst && ex.UnifiedError.TransportCode.HasValue)
                        return Unresolved(mutation, PlayServUplinkWriteState.Unknown, ex.UnifiedError);
                    statusFirst = false;
                }
            }
            if (!mutation.Sent) { caller.ThrowIfCancellationRequested(); budget.ThrowIfCancellationRequested(); }
            return Unresolved(mutation, last?.State == PlayServUplinkWriteState.Pending ? PlayServUplinkWriteState.Pending : PlayServUplinkWriteState.Unknown,
                last?.Error.IsError == true ? last.Error : new PlayServError(caller.IsCancellationRequested ? PlayServErrorCode.Canceled : PlayServErrorCode.Timeout,
                    "uplink_write_unknown", "The write outcome is not confirmed; retain and resolve this operation."));
        }

        private async Task<DataConnection> MutationConnectionAsync(DataMutation mutation, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (!ReferenceEquals(this, PlayServGameServer.Uplink) || !ReferenceEquals(_context, mutation.Context) ||
                        _state == PlayServUplinkState.Terminated || mutation.Lifetime.IsCancellationRequested)
                        throw UplinkErrors.Exception("uplink_disconnected");
                    if (_dataConnection != null && _state == PlayServUplinkState.Connected)
                    { ValidateDataConnection(_dataConnection); return _dataConnection; }
                }
                await Delay(10, ct).ConfigureAwait(false);
            }
        }

        private PlayServUplinkWriteOutcome ReadWriteOutcome(DataMutation mutation, IDictionary<string, object> receipt, bool allowPreflight)
        {
            if (allowPreflight && Text(receipt, "outcome") == "rejected" && !receipt.ContainsKey("operation_id") && !receipt.ContainsKey("epoch") &&
                receipt.TryGetValue("error_code", out var preflightCode) && Integer(preflightCode, out var refusal) &&
                (refusal == 32002 || refusal == 32003 || refusal == 32109))
                return new PlayServUplinkWriteOutcome(PlayServUplinkWriteState.Rejected, mutation.Id, mutation.Authority.Epoch,
                    error: DataAuthorityError((int)refusal, Text(receipt, "rejection")));
            if (Text(receipt, "operation_id") != mutation.Id || !SafeText(Text(receipt, "epoch"), 256)) throw PlayServUplinkData.InvalidResult();
            var epoch = Text(receipt, "epoch");
            switch (Text(receipt, "outcome"))
            {
                case "accepted":
                    if (epoch != mutation.Authority.Epoch || !Revision(receipt, "revision", out var revision) ||
                        !SafeText(Text(receipt, "record_id"), 1024) || !receipt.TryGetValue("deleted", out var deleted) || !(deleted is bool isDeleted) ||
                        (!isDeleted && (!receipt.TryGetValue("data", out var data) || !(data is IDictionary<string, object>)))) throw PlayServUplinkData.InvalidResult();
                    return new PlayServUplinkWriteOutcome(PlayServUplinkWriteState.Accepted, mutation.Id, epoch, revision,
                        Text(receipt, "record_id"), isDeleted, isDeleted ? null : PlayServGameServerJson.Serialize(receipt["data"]));
                case "rejected":
                    if (!receipt.TryGetValue("error_code", out var codeValue) || !Integer(codeValue, out var code) || code < 0 || code > int.MaxValue)
                        throw PlayServUplinkData.InvalidResult();
                    return new PlayServUplinkWriteOutcome(PlayServUplinkWriteState.Rejected, mutation.Id, epoch,
                        error: DataAuthorityError((int)code, Text(receipt, "rejection")));
                case "pending":
                    if (epoch != mutation.Authority.Epoch) throw PlayServUplinkData.InvalidResult();
                    return Unresolved(mutation, PlayServUplinkWriteState.Pending);
                default: throw PlayServUplinkData.InvalidResult();
            }
        }

        private PlayServUplinkWriteOutcome Unresolved(DataMutation mutation, PlayServUplinkWriteState state, PlayServError error = null) =>
            new PlayServUplinkWriteOutcome(state, mutation.Id, mutation.Authority.Epoch, error: error,
                resolve: token => ResolveMutationAsync(mutation, token));

        internal static byte[] DataFrame(object frame)
        {
            byte[] bytes;
            try { bytes = GameServerUplinkServices.Frame(frame); }
            catch (PlayServGameServerException) { throw; }
            catch { throw PlayServUplinkData.InvalidResult(); }
            if (bytes.Length > PlayServUplinkSocket.MaxFrameBytes) throw UplinkErrors.Exception("frame_too_large");
            return bytes;
        }
        internal static string Text(IDictionary<string, object> value, string field) => value != null && value.TryGetValue(field, out var raw) ? raw as string : null;
        internal static bool SafeText(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !HasControl(value);
        private static bool HasControl(string value) { foreach (var c in value) if (char.IsControl(c)) return true; return false; }
        internal static bool Integer(object value, out long result)
        {
            if (value is long l) { result = l; return true; }
            if (value is int i) { result = i; return true; }
            result = 0; return false;
        }
        internal static bool Revision(IDictionary<string, object> value, string field, out long result)
        {
            var text = Text(value, field);
            result = 0;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text) if (c < '0' || c > '9') return false;
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }
        internal static void ThrowDataFrameError(IDictionary<string, object> frame)
        {
            if (!frame.TryGetValue("error_code", out var value)) return;
            if (!Integer(value, out var code) || code < 0 || code > int.MaxValue) throw PlayServUplinkData.InvalidResult();
            throw new PlayServGameServerException(DataAuthorityError((int)code, Text(frame, "error")));
        }
        internal static PlayServError DataAuthorityError(int code, string reason = null)
        {
            var names = new Dictionary<int, string>
            {
                [32001] = "not_governed", [32002] = "authority_required", [32003] = "invalid_authority",
                [32101] = "epoch_mismatch", [32102] = "revision_conflict", [32103] = "retry_session_expired",
                [32104] = "retry_session_invalid", [32105] = "operation_payload_mismatch", [32106] = "capacity",
                [32107] = "base_unavailable", [32108] = "validation", [32109] = "access_denied", [32110] = "not_found",
                [32111] = "authority_unavailable", [32112] = "unsupported_operation", [32113] = "already_exists", [32114] = "scope_violation"
            };
            var source = names.TryGetValue(code, out var known) ? known : "authority_refused";
            if (reason == "invalid_cursor" || reason == "not_subscribed") source = reason;
            var category = code == 32109 || code == 32114 ? PlayServErrorCode.Forbidden : code == 32110 ? PlayServErrorCode.NotFound :
                code == 32102 || code == 32105 || code == 32113 ? PlayServErrorCode.Conflict : code == 32106 ? PlayServErrorCode.RateLimited : PlayServErrorCode.Validation;
            return new PlayServError(category, source, "The state authority refused this operation.", transportCode: code,
                retryable: code == 32106 || code == 32107 || code == 32111);
        }
    }
}
