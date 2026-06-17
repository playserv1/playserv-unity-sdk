using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using ILogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionRefreshClient
    {
        private readonly IPlayServCommandBus _commandBus;
        private readonly DataSubscriptionRequestIdSource _requestIds;
        private readonly IJsonCodec _jsonCodec;

        public DataSubscriptionRefreshClient(
            IPlayServCommandBus commandBus,
            ILogger logger,
            DataSubscriptionRequestIdSource requestIds,
            IJsonCodec jsonCodec)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public async Task RefreshAsync(long subscriptionId, int timeoutMs, CancellationToken ct = default)
        {
            if (subscriptionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(subscriptionId));

            if (_commandBus.State != PlayServState.Online)
            {
                throw new DataSubscriptionException(
                    0,
                    $"Data subscription refresh skipped because SDK state is {_commandBus.State}.");
            }

            var request = new DataSubscriptionRefreshRequest
            {
                RequestId = _requestIds.Next(),
                SubscriptionId = subscriptionId
            };

            await SendRefreshRequestAsync(request, timeoutMs, ct);
        }

        private async Task SendRefreshRequestAsync(DataSubscriptionRefreshRequest request, int timeoutMs, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<DataSubscriptionUpdate>();
            IDisposable typedSubscription = null;
            IDisposable byCommandSubscription = null;
            IDisposable byModuleCommandSubscription = null;
            IDisposable commandErrorSubscription = null;
            IDisposable commandErrorNamedSubscription = null;
            IDisposable commandErrorRpcSubscription = null;

            typedSubscription = _commandBus.On<DataSubscriptionUpdate>(update =>
            {
                if (MatchesRefreshUpdate(update, request))
                    tcs.TrySetResult(update);
            });

            byCommandSubscription = _commandBus.OnCommand("DataSubscriptionUpdate", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionUpdate(_jsonCodec, command, out var update))
                    return;

                if (MatchesRefreshUpdate(update, request))
                    tcs.TrySetResult(update);
            });

            byModuleCommandSubscription = _commandBus.OnCommand("module_dataflow.DataSubscriptionUpdate", command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionUpdate(_jsonCodec, command, out var update))
                    return;

                if (MatchesRefreshUpdate(update, request))
                    tcs.TrySetResult(update);
            });

            commandErrorSubscription = _commandBus.OnCommand("error", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionRefreshCommandError(errorResponse))
                    return;

                tcs.TrySetException(new DataSubscriptionException(
                    0,
                    DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse)));
            });

            commandErrorNamedSubscription = _commandBus.OnCommand("CommandErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionRefreshCommandError(errorResponse))
                    return;

                tcs.TrySetException(new DataSubscriptionException(
                    0,
                    DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse)));
            });

            commandErrorRpcSubscription = _commandBus.OnCommand("RpcErrorResponse", command =>
            {
                if (command is not CommandErrorResponse errorResponse)
                    return;

                if (!DataSubscriptionRequestSupport.LooksLikeDataSubscriptionRefreshCommandError(errorResponse))
                    return;

                tcs.TrySetException(new DataSubscriptionException(
                    0,
                    DataSubscriptionRequestSupport.FormatCommandErrorMessage(errorResponse)));
            });

            try
            {
                await _commandBus.SendAsync(request, "module_dataflow");
                var effectiveTimeoutMs = timeoutMs > 0 ? timeoutMs : PlayServDataSubscriptionAdapter.DataSubscriptionRefreshTimeoutMs;
                var completedInTime = await DataSubscriptionRequestSupport.WaitForCompletionOrTimeoutAsync(tcs.Task, effectiveTimeoutMs, ct);
                if (!completedInTime)
                {
                    throw new DataSubscriptionException(
                        0,
                        $"Timed out waiting for DataSubscriptionUpdate after refresh request {request.RequestId} ({effectiveTimeoutMs}ms).");
                }

                var update = await tcs.Task;
                if (update != null && update.HasError)
                {
                    throw new DataSubscriptionException(
                        update.ErrorCode ?? 0,
                        string.IsNullOrWhiteSpace(update.ErrorMessage)
                            ? "Subscription refresh returned an error update."
                            : update.ErrorMessage);
                }
            }
            finally
            {
                typedSubscription?.Dispose();
                byCommandSubscription?.Dispose();
                byModuleCommandSubscription?.Dispose();
                commandErrorSubscription?.Dispose();
                commandErrorNamedSubscription?.Dispose();
                commandErrorRpcSubscription?.Dispose();
            }
        }

        private static bool MatchesRefreshUpdate(DataSubscriptionUpdate update, DataSubscriptionRefreshRequest request)
        {
            return update != null &&
                   update.RequestId == request.RequestId &&
                   update.DataSubscriptionId == request.SubscriptionId;
        }
    }
}
