using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Analytics;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>
    /// Dedicated-server analytics with a bounded retry-safe queue and optional
    /// per-event player attribution. No mutable global player identity is used.
    /// </summary>
    public sealed class PlayServGameServerAnalytics
    {
        private readonly object _gate = new object();
        private PlayServAnalyticsClient _client;

        internal PlayServGameServerAnalytics()
        {
        }

        public bool CollectionEnabled => GetClient().CollectionEnabled;

        public int PendingEventCount => GetClient().PendingEventCount;

        public void SetCollectionEnabled(bool enabled) =>
            GetClient().SetCollectionEnabled(enabled);

        /// <summary>
        /// Queues one event. <paramref name="playerId"/> applies only to this event,
        /// so concurrent players cannot overwrite one another's analytics context.
        /// </summary>
        public void Track(
            string eventName,
            IReadOnlyDictionary<string, object> parameters = null,
            string playerId = null)
        {
            var normalizedPlayerId = PlayServGameServer.ValidateOptionalAnalyticsPlayerId(playerId);
            GetClient().Track(eventName, parameters, normalizedPlayerId);
        }

        /// <summary>Flushes all currently queued batches with the current rotating server key.</summary>
        public Task FlushAsync(CancellationToken cancellationToken = default) =>
            GetClient().FlushAsync(cancellationToken);

        internal async Task<PlayServError> FlushForShutdownAsync(
            CancellationToken cancellationToken)
        {
            PlayServAnalyticsClient client;
            lock (_gate)
                client = _client;

            if (client == null || client.PendingEventCount == 0)
                return PlayServError.None;

            try
            {
                await client.FlushAsync(cancellationToken);
                return PlayServError.None;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServGameServerException exception)
            {
                return exception.UnifiedError;
            }
            catch (Exception exception)
            {
                return new PlayServError(
                    PlayServErrorCode.Network,
                    "analytics_flush_failed",
                    "PlayServ server analytics could not be flushed.",
                    retryable: true,
                    rawDetails: exception.Message);
            }
        }

        internal void ResetForConfiguration()
        {
            PlayServAnalyticsClient previous;
            lock (_gate)
            {
                previous = _client;
                _client = null;
            }

            previous?.Dispose();
        }

        private PlayServAnalyticsClient GetClient()
        {
            PlayServGameServer.EnsureSupportedBuildForServices();
            PlayServGameServer.GetContextForServices();
            lock (_gate)
            {
                if (_client == null)
                {
                    _client = PlayServAnalyticsClient.CreateServer(
                        new PlayServGameServerAnalyticsProvider());
                }

                return _client;
            }
        }
    }

    internal sealed class PlayServGameServerAnalyticsProvider : IPlayServAnalyticsProvider
    {
        private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
        private readonly PlayServGameServerRuntimeHttpClient _http =
            new PlayServGameServerRuntimeHttpClient();

        public bool IsReady => PlayServGameServer.IsConfigured;

        public async Task SendAsync(
            PlayServAnalyticsBatch batch,
            CancellationToken cancellationToken = default)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));

            try
            {
                await _http.SendDataAsync(new PlayServRuntimeDataRequest
                {
                    Method = "POST",
                    RelativePath = PlayServHttpAnalyticsProvider.EventsPath,
                    RequiresClientToken = false,
                    JsonBody = PlayServHttpAnalyticsProvider.SerializeBatch(_json, batch)
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlayServRuntimeHttpException exception)
            {
                throw new PlayServGameServerException(
                    PlayServError.FromHttp(
                        exception.StatusCode,
                        exception.BackendCode,
                        string.IsNullOrWhiteSpace(exception.ProblemDetail)
                            ? "PlayServ server analytics ingestion failed."
                            : exception.ProblemDetail,
                        exception.IsNetworkError,
                        exception.Message.IndexOf(
                            "timed out",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        exception.ResponseBody),
                    exception);
            }
        }
    }
}
