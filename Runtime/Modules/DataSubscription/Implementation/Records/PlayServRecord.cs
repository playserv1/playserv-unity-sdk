using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Data
{
    public sealed class PlayServRecord<T>
    {
        private readonly PlayServRecordsClient _client;
        private readonly SemaphoreSlim _operations = new SemaphoreSlim(1, 1);
        private string _snapshot;

        internal PlayServRecord(
            PlayServRecordSet<T> ownerSet,
            PlayServRecordsClient client,
            string id,
            string entityId,
            T value,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            string owner,
            string etag,
            string snapshot,
            bool isPartial)
        {
            OwnerSet = ownerSet ?? throw new ArgumentNullException(nameof(ownerSet));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Id = id ?? throw new ArgumentNullException(nameof(id));
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            Value = value;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            Owner = owner ?? string.Empty;
            ETag = etag ?? string.Empty;
            _snapshot = snapshot ?? "{}";
            IsPartial = isPartial;
        }

        public string Id { get; }

        public string EntityId { get; }

        public T Value { get; set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public string Owner { get; private set; }

        public string ETag { get; private set; }

        public bool IsPartial { get; private set; }

        public bool IsDeleted { get; private set; }

        public bool HasPendingChanges
        {
            get
            {
                if (IsDeleted || Value == null)
                    return false;
                return !string.Equals(
                    _snapshot,
                    _client.Canonical(_client.ToRecordFields(Value)),
                    StringComparison.Ordinal);
            }
        }

        internal PlayServRecordSet<T> OwnerSet { get; }

        internal string CanonicalSnapshot => _snapshot;

        /// <summary>
        /// Keeps this full record handle current through the realtime dataflow transport.
        /// Push frames trigger a canonical REST reload so timestamps, snapshot and ETag advance
        /// together. Partial records are fully reloaded before the subscription is opened.
        /// </summary>
        public Task<IPlayServRecordSubscription<T>> SubscribeAsync(
            CancellationToken ct = default) =>
            OwnerSet.SubscribeAsync(this, ct);

        /// <summary>
        /// Saves a captured top-level patch. Edits made while the request is pending are
        /// retained over the server response for the next save; snapshot and ETag reflect
        /// the acknowledged server state. Reload and realtime retain backend-wins behavior.
        /// </summary>
        public async Task SaveAsync(CancellationToken ct = default)
        {
            await _operations.WaitAsync(ct);
            try
            {
                EnsureActive();
                if (IsPartial)
                {
                    throw new InvalidOperationException(
                        "A partially loaded record cannot be saved. Call ReloadAsync first to load the full record.");
                }
                if (Value == null)
                    throw new InvalidOperationException("A record value cannot be null when saving.");

                // Detach nested objects/collections before the first await, including ACL
                // and credential resolution. This is the value represented by this patch.
                var current = _client.ParseSnapshot(_client.Canonical(_client.ToRecordFields(Value)));
                var patch = BuildMergePatch(_client.ParseSnapshot(_snapshot), current);
                if (patch.Count == 0)
                    return;

                await OwnerSet.EnsureAccessAsync(
                    PlayServDataAccessOperation.Write,
                    false,
                    ct);
                var updated = await _client.SaveAsync(this, patch, ct);
                updated.Value = _client.PreserveChangesDuringSave(current, Value, updated.Value, updated._snapshot);
                CopyFrom(updated);
            }
            finally
            {
                _operations.Release();
            }
        }

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            await _operations.WaitAsync(ct);
            try
            {
                EnsureActive();
                var loaded = await OwnerSet.LoadAsync(Id, null, ct);
                CopyFrom(loaded);
            }
            finally
            {
                _operations.Release();
            }
        }

        public async Task DeleteAsync(CancellationToken ct = default)
        {
            await _operations.WaitAsync(ct);
            try
            {
                EnsureActive();
                await OwnerSet.EnsureAccessAsync(
                    PlayServDataAccessOperation.Write,
                    false,
                    ct);
                await _client.DeleteAsync(this, ct);
                IsDeleted = true;
            }
            finally
            {
                _operations.Release();
            }
        }

        private Dictionary<string, object> BuildMergePatch(
            IReadOnlyDictionary<string, object> snapshot,
            IReadOnlyDictionary<string, object> current)
        {
            var patch = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var key in snapshot.Keys.Union(current.Keys, StringComparer.Ordinal))
            {
                var hadBefore = snapshot.TryGetValue(key, out var before);
                var hasNow = current.TryGetValue(key, out var now);
                if (!hasNow)
                {
                    patch[key] = null;
                    continue;
                }

                if (!hadBefore ||
                    !string.Equals(_client.Canonical(before), _client.Canonical(now), StringComparison.Ordinal))
                {
                    patch[key] = now;
                }
            }
            return patch;
        }

        private void CopyFrom(PlayServRecord<T> source)
        {
            if (!string.Equals(Id, source.Id, StringComparison.Ordinal) ||
                !string.Equals(EntityId, source.EntityId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The server returned a different record identity.");
            }

            Value = source.Value;
            CreatedAt = source.CreatedAt;
            UpdatedAt = source.UpdatedAt;
            Owner = source.Owner;
            ETag = source.ETag;
            _snapshot = source._snapshot;
            IsPartial = source.IsPartial;
            IsDeleted = false;
        }

        internal bool MatchesRealtimeSnapshot(object value)
        {
            if (value == null)
                return false;
            try
            {
                return string.Equals(_snapshot, _client.Canonical(value), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal async Task<PlayServRecordRealtimeApplyResult<T>> SynchronizeRealtimeAsync(
            CancellationToken ct)
        {
            await _operations.WaitAsync(ct);
            try
            {
                EnsureActive();

                // Realtime payloads intentionally contain record fields only. Reload through the
                // REST record route so the handle receives the authoritative ETag and timestamps.
                var loaded = await OwnerSet.LoadAsync(Id, null, ct);
                if (loaded.UpdatedAt != default && UpdatedAt != default && loaded.UpdatedAt < UpdatedAt)
                    return PlayServRecordRealtimeApplyResult<T>.NotApplied;

                var baseline = _client.ParseSnapshot(_snapshot);
                var current = Value == null
                    ? new Dictionary<string, object>(StringComparer.Ordinal)
                    : _client.ToRecordFields(Value);
                var remote = _client.ParseSnapshot(loaded._snapshot);
                var localChangedFields = ChangedFields(baseline, current);
                var remoteChangedFields = ChangedFields(baseline, remote);
                var metadataChanged = loaded.UpdatedAt != UpdatedAt ||
                                      !string.Equals(loaded.ETag, ETag, StringComparison.Ordinal) ||
                                      !string.Equals(loaded.Owner, Owner, StringComparison.Ordinal) ||
                                      loaded.CreatedAt != CreatedAt;

                if (remoteChangedFields.Count == 0 && !metadataChanged)
                    return PlayServRecordRealtimeApplyResult<T>.NotApplied;

                var localValue = localChangedFields.Count == 0
                    ? default
                    : CloneValue(Value);
                CopyFrom(loaded);
                return new PlayServRecordRealtimeApplyResult<T>(
                    true,
                    remoteChangedFields,
                    localChangedFields,
                    localValue,
                    Value);
            }
            finally
            {
                _operations.Release();
            }
        }

        internal async Task<bool> MarkDeletedFromRealtimeAsync(CancellationToken ct)
        {
            await _operations.WaitAsync(ct);
            try
            {
                if (IsDeleted)
                    return false;
                IsDeleted = true;
                return true;
            }
            finally
            {
                _operations.Release();
            }
        }

        private T CloneValue(T value)
        {
            if (value == null)
                return default;
            try
            {
                return _client.CloneRecordValue(value);
            }
            catch
            {
                return value;
            }
        }

        private IReadOnlyList<string> ChangedFields(
            IReadOnlyDictionary<string, object> before,
            IReadOnlyDictionary<string, object> after)
        {
            return before.Keys
                .Union(after.Keys, StringComparer.Ordinal)
                .Where(key =>
                {
                    var hadBefore = before.TryGetValue(key, out var previous);
                    var hasAfter = after.TryGetValue(key, out var next);
                    return hadBefore != hasAfter ||
                           !string.Equals(
                               _client.Canonical(previous),
                               _client.Canonical(next),
                               StringComparison.Ordinal);
                })
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
        }

        private void EnsureActive()
        {
            if (IsDeleted)
                throw new InvalidOperationException($"Record '{Id}' has been deleted.");
        }
    }

    public sealed class PlayServSingleton<T>
    {
        private readonly PlayServRecordsClient _client;
        private readonly SemaphoreSlim _operations = new SemaphoreSlim(1, 1);
        private string _snapshot;

        internal PlayServSingleton(
            PlayServRecordSet<T> ownerSet,
            PlayServRecordsClient client,
            string entityId,
            T value,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            string etag,
            string snapshot,
            bool isPartial)
        {
            OwnerSet = ownerSet ?? throw new ArgumentNullException(nameof(ownerSet));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            Value = value;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            ETag = etag ?? string.Empty;
            _snapshot = snapshot ?? "{}";
            IsPartial = isPartial;
        }

        public string EntityId { get; }

        public T Value { get; set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public string ETag { get; private set; }

        public bool IsPartial { get; private set; }

        public bool HasPendingChanges
        {
            get
            {
                if (Value == null)
                    return false;
                return !string.Equals(
                    _snapshot,
                    _client.Canonical(_client.ToRecordFields(Value)),
                    StringComparison.Ordinal);
            }
        }

        internal PlayServRecordSet<T> OwnerSet { get; }

        /// <summary>
        /// Saves a captured top-level patch while retaining edits made during the request
        /// as pending changes against the acknowledged server snapshot and ETag.
        /// </summary>
        public async Task SaveAsync(CancellationToken ct = default)
        {
            await _operations.WaitAsync(ct);
            try
            {
                if (IsPartial)
                {
                    throw new InvalidOperationException(
                        "A partially loaded singleton cannot be saved. Call ReloadAsync first.");
                }
                if (Value == null)
                    throw new InvalidOperationException("A singleton value cannot be null when saving.");

                var current = _client.ParseSnapshot(_client.Canonical(_client.ToRecordFields(Value)));
                var snapshot = _client.ParseSnapshot(_snapshot);
                var patch = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var key in snapshot.Keys.Union(current.Keys, StringComparer.Ordinal))
                {
                    var hadBefore = snapshot.TryGetValue(key, out var before);
                    var hasNow = current.TryGetValue(key, out var now);
                    if (!hasNow)
                        patch[key] = null;
                    else if (!hadBefore ||
                             !string.Equals(_client.Canonical(before), _client.Canonical(now), StringComparison.Ordinal))
                        patch[key] = now;
                }
                if (patch.Count == 0)
                    return;

                await OwnerSet.EnsureAccessAsync(
                    PlayServDataAccessOperation.Write,
                    true,
                    ct);
                var updated = await _client.SaveSingletonAsync(this, patch, ct);
                updated.Value = _client.PreserveChangesDuringSave(current, Value, updated.Value, updated._snapshot);
                CopyFrom(updated);
            }
            finally
            {
                _operations.Release();
            }
        }

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            await _operations.WaitAsync(ct);
            try
            {
                var loaded = await OwnerSet.GetSingletonAsync(null, ct);
                CopyFrom(loaded);
            }
            finally
            {
                _operations.Release();
            }
        }

        private void CopyFrom(PlayServSingleton<T> source)
        {
            if (!string.Equals(EntityId, source.EntityId, StringComparison.Ordinal))
                throw new InvalidOperationException("The server returned a different singleton identity.");

            Value = source.Value;
            CreatedAt = source.CreatedAt;
            UpdatedAt = source.UpdatedAt;
            ETag = source.ETag;
            _snapshot = source._snapshot;
            IsPartial = source.IsPartial;
        }
    }
}
