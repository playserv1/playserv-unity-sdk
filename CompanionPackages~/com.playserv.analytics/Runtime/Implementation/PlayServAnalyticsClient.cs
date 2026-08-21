using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Wrapper;
using UnityApplication = UnityEngine.Application;

namespace Playserv.Analytics
{
    internal interface IPlayServAnalyticsClient
    {
        bool CollectionEnabled { get; }

        int PendingEventCount { get; }

        void SetCollectionEnabled(bool enabled);

        void SetProvider(IPlayServAnalyticsProvider provider);

        void NotifyProviderChanged();

        void SetUserId(string userId);

        void SetUserProperty(string key, string value);

        void RemoveUserProperty(string key);

        void ClearUserProperties();

        void Track(string eventName, IReadOnlyDictionary<string, object> parameters);

        void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters,
            string eventUserId);

        Task FlushAsync(CancellationToken cancellationToken);
    }

    internal sealed class PlayServAnalyticsClient : IPlayServAnalyticsClient, IDisposable
    {
        internal const int DefaultBatchSize = 20;
        internal const int DefaultMaxQueueSize = 500;
        internal const int MaxParameterCount = 50;

        private static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(10);

        private readonly object _gate = new object();
        private readonly List<PlayServAnalyticsEvent> _queue = new List<PlayServAnalyticsEvent>();
        private readonly Dictionary<string, string> _userProperties =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _flushGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Func<string> _resolveRuntimeUserId;
        private readonly ILogger _logger;
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private readonly string _sdkVersion;
        private readonly string _applicationVersion;
        private readonly string _platform;
        private readonly int _batchSize;
        private readonly int _maxQueueSize;
        private readonly TimeSpan _flushInterval;

        private IPlayServAnalyticsProvider _provider;
        private Task _backgroundFlush;
        private Task _flushPump;
        private string _userId = string.Empty;
        private long _sequence;
        private bool _collectionEnabled = true;
        private bool _disposed;

        internal static PlayServAnalyticsClient CreateServer(
            IPlayServAnalyticsProvider provider)
        {
            return new PlayServAnalyticsClient(
                provider,
                () => string.Empty,
                PlayServLog.ForCategory(PlayServLogCategory.Analytics),
                SdkInfo.Version,
                UnityApplication.version,
                UnityApplication.platform.ToString());
        }

        public PlayServAnalyticsClient(
            IPlayServAnalyticsProvider provider,
            Func<string> resolveRuntimeUserId,
            ILogger logger,
            string sdkVersion,
            string applicationVersion,
            string platform,
            int batchSize = DefaultBatchSize,
            int maxQueueSize = DefaultMaxQueueSize,
            TimeSpan? flushInterval = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _resolveRuntimeUserId = resolveRuntimeUserId ?? (() => string.Empty);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sdkVersion = sdkVersion ?? string.Empty;
            _applicationVersion = applicationVersion ?? string.Empty;
            _platform = platform ?? string.Empty;
            _batchSize = batchSize > 0
                ? batchSize
                : throw new ArgumentOutOfRangeException(nameof(batchSize));
            _maxQueueSize = maxQueueSize >= batchSize
                ? maxQueueSize
                : throw new ArgumentOutOfRangeException(
                    nameof(maxQueueSize),
                    "Analytics queue size must be greater than or equal to batch size.");
            _flushInterval = flushInterval ?? DefaultFlushInterval;

            if (_flushInterval > TimeSpan.Zero)
                _flushPump = RunFlushPumpAsync(_lifetime.Token);
        }

        public bool CollectionEnabled
        {
            get
            {
                lock (_gate)
                    return _collectionEnabled;
            }
        }

        public int PendingEventCount
        {
            get
            {
                lock (_gate)
                    return _queue.Count;
            }
        }

        public void SetCollectionEnabled(bool enabled)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _collectionEnabled = enabled;
                if (!enabled)
                    _queue.Clear();
            }
        }

        public void SetProvider(IPlayServAnalyticsProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (_gate)
            {
                ThrowIfDisposed();
                _provider = provider;
            }

            StartBackgroundFlush();
        }

        public void NotifyProviderChanged()
        {
            StartBackgroundFlush();
        }

        public void SetUserId(string userId)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _userId = (userId ?? string.Empty).Trim();
            }
        }

        public void SetUserProperty(string key, string value)
        {
            var normalizedKey = ValidatePropertyKey(key);
            lock (_gate)
            {
                ThrowIfDisposed();
                _userProperties[normalizedKey] = value ?? string.Empty;
            }
        }

        public void RemoveUserProperty(string key)
        {
            var normalizedKey = ValidatePropertyKey(key);
            lock (_gate)
            {
                ThrowIfDisposed();
                _userProperties.Remove(normalizedKey);
            }
        }

        public void ClearUserProperties()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _userProperties.Clear();
            }
        }

        public void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters)
        {
            Track(eventName, parameters, null);
        }

        public void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters,
            string eventUserId)
        {
            var normalizedName = ValidateEventName(eventName);
            var convertedParameters = ConvertParameters(parameters);
            var shouldFlush = false;
            var droppedOldest = false;

            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_collectionEnabled)
                    return;

                while (_queue.Count >= _maxQueueSize)
                {
                    _queue.RemoveAt(0);
                    droppedOldest = true;
                }

                _queue.Add(new PlayServAnalyticsEvent
                {
                    EventId = Guid.NewGuid().ToString("N"),
                    Name = normalizedName,
                    TimestampUnixMilliseconds = UtcNowUnixMilliseconds(),
                    Sequence = Interlocked.Increment(ref _sequence),
                    SessionId = _sessionId,
                    UserId = ResolveUserId(eventUserId),
                    SdkVersion = _sdkVersion,
                    ApplicationVersion = _applicationVersion,
                    Platform = _platform,
                    Parameters = convertedParameters,
                    UserProperties = SnapshotUserProperties()
                });
                shouldFlush = _queue.Count >= _batchSize;
            }

            if (droppedOldest)
            {
                _logger.LogWarning(
                    $"Analytics queue reached {_maxQueueSize} events; the oldest event was dropped.");
            }

            if (shouldFlush)
                StartBackgroundFlush();
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return FlushCoreAsync(cancellationToken);
        }

        public void NotifyConnected()
        {
            StartBackgroundFlush();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _queue.Clear();
            }

            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private async Task RunFlushPumpAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_flushInterval, cancellationToken);
                    await FlushCoreAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Analytics background flush failed: {ex.Message}");
                }
            }
        }

        private void StartBackgroundFlush()
        {
            lock (_gate)
            {
                if (_disposed ||
                    !_collectionEnabled ||
                    _queue.Count == 0 ||
                    !_provider.IsReady ||
                    (_backgroundFlush != null && !_backgroundFlush.IsCompleted))
                {
                    return;
                }

                _backgroundFlush = FlushInBackgroundAsync(_lifetime.Token);
            }
        }

        private async Task FlushInBackgroundAsync(CancellationToken cancellationToken)
        {
            try
            {
                await FlushCoreAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Analytics flush failed: {ex.Message}");
            }
        }

        private async Task FlushCoreAsync(CancellationToken cancellationToken)
        {
            await _flushGate.WaitAsync(cancellationToken);
            try
            {
                while (true)
                {
                    PlayServAnalyticsEvent[] events;
                    IPlayServAnalyticsProvider provider;
                    lock (_gate)
                    {
                        if (_disposed || !_collectionEnabled || _queue.Count == 0)
                            return;

                        provider = _provider;
                        if (!provider.IsReady)
                            return;

                        events = _queue
                            .Take(Math.Min(_batchSize, _queue.Count))
                            .ToArray();
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = new PlayServAnalyticsBatch
                    {
                        BatchId = Guid.NewGuid().ToString("N"),
                        SentAtUnixMilliseconds = UtcNowUnixMilliseconds(),
                        Events = events
                    };
                    await provider.SendAsync(batch, cancellationToken);

                    var sentEventIds = new HashSet<string>(
                        events.Select(@event => @event.EventId),
                        StringComparer.Ordinal);
                    lock (_gate)
                        _queue.RemoveAll(@event => sentEventIds.Contains(@event.EventId));
                }
            }
            finally
            {
                _flushGate.Release();
            }
        }

        private string ResolveUserId(string eventUserId)
        {
            if (!string.IsNullOrWhiteSpace(eventUserId))
                return eventUserId.Trim();

            if (!string.IsNullOrWhiteSpace(_userId))
                return _userId;

            return (_resolveRuntimeUserId() ?? string.Empty).Trim();
        }

        private PlayServAnalyticsUserProperty[] SnapshotUserProperties()
        {
            return _userProperties
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PlayServAnalyticsUserProperty(pair.Key, pair.Value))
                .ToArray();
        }

        private static PlayServAnalyticsParameter[] ConvertParameters(
            IReadOnlyDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return Array.Empty<PlayServAnalyticsParameter>();
            if (parameters.Count > MaxParameterCount)
            {
                throw new ArgumentException(
                    $"Analytics events cannot contain more than {MaxParameterCount} parameters.",
                    nameof(parameters));
            }

            return parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => PlayServAnalyticsParameter.FromObject(pair.Key, pair.Value))
                .ToArray();
        }

        private static string ValidateEventName(string eventName)
        {
            var normalized = (eventName ?? string.Empty).Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("Analytics event name is required.", nameof(eventName));
            if (normalized.Length > 128)
                throw new ArgumentException("Analytics event name cannot exceed 128 characters.", nameof(eventName));

            return normalized;
        }

        private static string ValidatePropertyKey(string key)
        {
            var normalized = (key ?? string.Empty).Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("Analytics user property key is required.", nameof(key));
            if (normalized.Length > 64)
                throw new ArgumentException("Analytics user property key cannot exceed 64 characters.", nameof(key));

            return normalized;
        }

        private static long UtcNowUnixMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlayServAnalyticsClient));
        }
    }
}
