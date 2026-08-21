using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.DataSubscription.Exceptions;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.DataSubscription
{
    internal sealed class DataSubscriptionCloseClient
    {
        private readonly IPlayServCommandBus _commandBus;
        private readonly DataSubscriptionRequestIdSource _requestIds;
        private readonly IJsonCodec _jsonCodec;

        public DataSubscriptionCloseClient(
            IPlayServCommandBus commandBus,
            DataSubscriptionRequestIdSource requestIds,
            IJsonCodec jsonCodec)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public async Task<PlayServSubscriptionCloseResult> CloseAsync(
            long subscriptionId,
            int timeoutMs,
            CancellationToken ct,
            bool allowDuringReconnect = false)
        {
            if (subscriptionId <= 0)
                return PlayServSubscriptionCloseResult.Success(true);

            if (!allowDuringReconnect && _commandBus.State != PlayServState.Online)
            {
                return PlayServSubscriptionCloseResult.Failed(new PlayServError(
                    PlayServErrorCode.Network,
                    "connection_unavailable",
                    $"Subscription close skipped because SDK state is {_commandBus.State}.",
                    retryable: true));
            }

            var request = new DataSubscriptionCloseRequest
            {
                RequestId = _requestIds.Next(),
                SubscriptionId = subscriptionId
            };
            var tcs = new TaskCompletionSource<PlayServSubscriptionCloseResult>();
            IDisposable typed = null;
            IDisposable shortCommand = null;
            IDisposable moduleCommand = null;
            IDisposable legacyError = null;
            IDisposable commandError = null;
            IDisposable namedCommandError = null;
            IDisposable rpcCommandError = null;

            Action<object> handleResponse = command =>
            {
                if (!DataSubscriptionRequestSupport.TryMapDataSubscriptionCloseResponse(
                        _jsonCodec,
                        command,
                        out var response) ||
                    response.RequestId != request.RequestId)
                {
                    return;
                }

                tcs.TrySetResult(MapResponse(response));
            };
            Action<object> handleCommandError = command =>
            {
                if (!(command is CommandErrorResponse error) ||
                    !DataSubscriptionRequestSupport.LooksLikeDataSubscriptionCloseCommandError(error))
                {
                    return;
                }

                tcs.TrySetResult(PlayServSubscriptionCloseResult.Failed(new PlayServError(
                    PlayServErrorCode.ServerError,
                    error.Error,
                    error.Message,
                    retryable: error.Retryable,
                    rawDetails: error.Details)));
            };

            typed = _commandBus.On<DataSubscriptionCloseResponse>(response => handleResponse(response));
            shortCommand = _commandBus.OnCommand("DataSubscriptionCloseResponse", handleResponse);
            moduleCommand = _commandBus.OnCommand("module_dataflow.DataSubscriptionCloseResponse", handleResponse);
            legacyError = _commandBus.OnCommand("ErrorResponse", command =>
            {
                if (!(command is ErrorResponse error))
                    return;
                tcs.TrySetResult(MapError(error.ErrorCode, error.Message));
            });
            commandError = _commandBus.OnCommand("error", handleCommandError);
            namedCommandError = _commandBus.OnCommand("CommandErrorResponse", handleCommandError);
            rpcCommandError = _commandBus.OnCommand("RpcErrorResponse", handleCommandError);

            try
            {
                await _commandBus.SendAsync(request, "module_dataflow");
                var completed = await DataSubscriptionRequestSupport.WaitForCompletionOrTimeoutAsync(
                    tcs.Task,
                    timeoutMs,
                    ct);
                if (!completed)
                {
                    return PlayServSubscriptionCloseResult.Failed(new PlayServError(
                        PlayServErrorCode.Timeout,
                        "subscription_close_timeout",
                        $"Timed out waiting for DataSubscriptionCloseResponse after {timeoutMs}ms.",
                        retryable: true));
                }

                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return PlayServSubscriptionCloseResult.Failed(new PlayServError(
                    PlayServErrorCode.Network,
                    "subscription_close_transport_failed",
                    ex.Message,
                    retryable: true));
            }
            finally
            {
                typed?.Dispose();
                shortCommand?.Dispose();
                moduleCommand?.Dispose();
                legacyError?.Dispose();
                commandError?.Dispose();
                namedCommandError?.Dispose();
                rpcCommandError?.Dispose();
            }
        }

        private static PlayServSubscriptionCloseResult MapResponse(DataSubscriptionCloseResponse response)
        {
            if (response == null)
            {
                return PlayServSubscriptionCloseResult.Failed(new PlayServError(
                    PlayServErrorCode.InvalidResponse,
                    "invalid_close_response",
                    "DataSubscriptionCloseResponse was null."));
            }
            if (response.HasError)
                return MapError(response.Error.ErrorCode, response.Error.Message);
            if (response.Result?.Success == true)
                return PlayServSubscriptionCloseResult.Success();
            return PlayServSubscriptionCloseResult.Failed(new PlayServError(
                PlayServErrorCode.InvalidResponse,
                "invalid_close_response",
                "DataSubscriptionCloseResponse did not contain a successful result."));
        }

        private static PlayServSubscriptionCloseResult MapError(int code, string message)
        {
            if (code == SubscriptionNotFoundException.Code)
                return PlayServSubscriptionCloseResult.Success(true);
            var exception = DataSubscriptionErrorMapper.MapErrorToException(new DataSubscriptionError
            {
                ErrorCode = code,
                Message = message
            });
            return PlayServSubscriptionCloseResult.Failed(exception.UnifiedError);
        }
    }
}
