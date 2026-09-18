using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

namespace Playserv.Data
{
    public enum PlayServRecordRefState { Unloaded, Expanded, Loading, Loaded, Missing, Faulted }

    internal interface IPlayServRecordRef
    {
        string Id { get; }
        void ValidateContext();
        void Bind(PlayServRecordsClient client, JToken expanded, JsonSerializer serializer);
    }

    /// <summary>An ID relation. Expanded values are previews; LoadAsync obtains an editable canonical record.</summary>
    [Preserve]
    public sealed class PlayServRecordRef<T> : IPlayServRecordRef
    {
        private readonly object _gate = new object();
        private PlayServRecordSet<T> _records;
        private PlayServRecordsClient _client;
        private T _preview;
        private PlayServRecord<T> _record;
        private Task<PlayServRecord<T>> _loading;
        private PlayServRecordRefState _state;
        private Exception _error;

        /// <summary>Creates an ID-only value for writes. Use Records&lt;T&gt;().Reference(id) for a loadable reference.</summary>
        [Preserve]
        public PlayServRecordRef(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Record ID is required.", nameof(id));
            Id = id.Trim();
        }
        internal PlayServRecordRef(string id, PlayServRecordSet<T> records, PlayServRecordsClient client) : this(id)
        { _records = records; _client = client; }

        public string Id { get; }
        public T Value { get { ValidateContext(); return _record != null ? _record.Value : _preview; } }
        public PlayServRecord<T> Record { get { ValidateContext(); return _record; } }
        public PlayServRecordRefState State => _state;
        public Exception Error => _error;

        public Task<PlayServRecord<T>> LoadAsync(CancellationToken ct = default) => Load(false, ct);
        public Task<PlayServRecord<T>> ReloadAsync(CancellationToken ct = default) => Load(true, ct);

        private Task<PlayServRecord<T>> Load(bool reload, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateContext();
            if (_records == null) throw new InvalidOperationException("Create a loadable reference through Records<T>().Reference(id).");
            Task<PlayServRecord<T>> shared;
            lock (_gate)
            {
                if (_loading != null) shared = _loading;
                else if (!reload && (_state == PlayServRecordRefState.Loaded || _state == PlayServRecordRefState.Missing))
                    return Task.FromResult(_record);
                else
                {
                    var completion = new TaskCompletionSource<PlayServRecord<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shared = _loading = completion.Task;
                    _state = PlayServRecordRefState.Loading; _error = null;
                    _ = LoadCore(completion);
                }
            }
            return WaitAsync(shared, ct);
        }

        private async Task LoadCore(TaskCompletionSource<PlayServRecord<T>> completion)
        {
            try
            {
                var record = await _records.LoadAsync(Id, ct: CancellationToken.None);
                ValidateContext();
                lock (_gate) { Apply(record, null); _loading = null; }
                completion.TrySetResult(record);
            }
            catch (Exception error)
            {
                // Context invalidation must not become a cached "missing" result.
                try { ValidateContext(); } catch (Exception contextError) { error = contextError; }
                lock (_gate) { Apply(null, error); _loading = null; }
                if (error is PlayServRecordNotFoundException) completion.TrySetResult(null);
                else completion.TrySetException(error);
            }
        }

        private static async Task<PlayServRecord<T>> WaitAsync(Task<PlayServRecord<T>> shared, CancellationToken ct)
        {
            if (!ct.CanBeCanceled) return await shared;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(shared, cancelled.Task) != shared)
                {
                    // Observe a later shared failure even when every waiting caller has cancelled.
                    _ = ObserveAsync(shared);
                    ct.ThrowIfCancellationRequested();
                }
                ct.ThrowIfCancellationRequested();
                return await shared;
            }
        }
        private static async Task ObserveAsync(Task task) { try { await task; } catch { } }
        internal void Apply(PlayServRecord<T> record, Exception error)
        {
            _record = record; _preview = default;
            _error = error is PlayServRecordNotFoundException ? null : error;
            _state = record != null ? PlayServRecordRefState.Loaded :
                error is PlayServRecordNotFoundException ? PlayServRecordRefState.Missing : PlayServRecordRefState.Faulted;
        }
        private void ValidateContext() => _client?.ValidateReferenceContext();
        void IPlayServRecordRef.ValidateContext() => ValidateContext();
        void IPlayServRecordRef.Bind(PlayServRecordsClient client, JToken expanded, JsonSerializer serializer)
        {
            _client = client.ForReferences();
            _records = new PlayServRecordSet<T>(_client, null);
            if (expanded != null)
            {
                _preview = expanded.ToObject<T>(serializer);
                _state = PlayServRecordRefState.Expanded;
            }
        }
    }

    public sealed partial class PlayServRecordSet<T>
    {
        public PlayServRecordRef<T> Reference(string id)
        {
            var client = _client.ForReferences();
            client.ValidateReferenceContext();
            return new PlayServRecordRef<T>(id, new PlayServRecordSet<T>(client, _explicitEntityId), client);
        }

        /// <summary>Uses the ID batch query, preserving order, duplicates and per-item errors.</summary>
        public async Task<PlayServBulkResult<PlayServRecordRef<T>>> LoadReferencesAsync(
            IEnumerable<string> ids, int maxConcurrency = 4, CancellationToken ct = default)
        {
            var client = _client.ForReferences();
            client.ValidateReferenceContext();
            var records = new PlayServRecordSet<T>(client, _explicitEntityId);
            var loaded = await records.LoadManyAsync(ids, maxConcurrency: maxConcurrency, ct: ct);
            client.ValidateReferenceContext();
            var items = new List<PlayServBulkItemResult<PlayServRecordRef<T>>>();
            foreach (var item in loaded.Items)
            {
                var reference = new PlayServRecordRef<T>(item.Key, records, client);
                reference.Apply(item.Value, item.Exception);
                items.Add(new PlayServBulkItemResult<PlayServRecordRef<T>>(item.Key, reference, item.Error, item.Exception));
            }
            return new PlayServBulkResult<PlayServRecordRef<T>>(items);
        }
    }
}
