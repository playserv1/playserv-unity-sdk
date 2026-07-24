using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Proxy.Common;
using Playserv.RPC;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime.RPC
{
    public sealed class PlayServRpcAwaitableTests
    {
        [UnityTest]
        public IEnumerator InvokeAsync_DeserializesTypedResponseAndSendsRequestId()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);

            var invocation = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest { Mode = "duo" },
                options: null,
                CancellationToken.None);

            var sent = runtime.SingleSentRequest;
            Assert.That(sent.RequestId, Is.Not.Empty);
            Assert.That(sent.ServiceName, Is.EqualTo("RoomService"));
            Assert.That(sent.MethodName, Is.EqualTo("FindMatch"));

            runtime.EmitResponse(ResponseFor(sent, "{\"MatchId\":\"match-42\"}"));

            yield return Await(invocation);
            var result = invocation.GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.RequestId, Is.EqualTo(sent.RequestId));
            Assert.That(result.Value.MatchId, Is.EqualTo("match-42"));
            Assert.That(result.Error, Is.Null);
        }

        [UnityTest]
        public IEnumerator InvokeAsync_CorrelatesReorderedResponsesByRequestId()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);

            var first = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest { Mode = "first" },
                Options("request-1"),
                CancellationToken.None);
            var second = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest { Mode = "second" },
                Options("request-2"),
                CancellationToken.None);

            runtime.EmitResponse(ResponseFor(runtime.SentRequests[1], "{\"MatchId\":\"second\"}"));
            runtime.EmitResponse(ResponseFor(runtime.SentRequests[0], "{\"MatchId\":\"first\"}"));

            yield return Await(first);
            yield return Await(second);
            Assert.That(first.GetAwaiter().GetResult().Value.MatchId, Is.EqualTo("first"));
            Assert.That(second.GetAwaiter().GetResult().Value.MatchId, Is.EqualTo("second"));
        }

        [UnityTest]
        public IEnumerator InvokeAsync_FallsBackToOldestRequestForLegacyResponseWithoutRequestId()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);

            var first = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                Options("legacy-1"),
                CancellationToken.None);
            var second = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                Options("legacy-2"),
                CancellationToken.None);

            runtime.EmitResponse(new InvokeRpcResponse
            {
                Status = "ok",
                Request = new InvokeRpcRequestInfo
                {
                    ServiceName = "RoomService",
                    MethodName = "FindMatch"
                },
                Result = "{\"MatchId\":\"first\"}"
            });
            runtime.EmitResponse(new InvokeRpcResponse
            {
                Status = "ok",
                Request = new InvokeRpcRequestInfo
                {
                    ServiceName = "RoomService",
                    MethodName = "FindMatch"
                },
                Result = "{\"MatchId\":\"second\"}"
            });

            yield return Await(first);
            yield return Await(second);
            Assert.That(first.GetAwaiter().GetResult().Value.MatchId, Is.EqualTo("first"));
            Assert.That(second.GetAwaiter().GetResult().Value.MatchId, Is.EqualTo("second"));
        }

        [UnityTest]
        public IEnumerator InvokeAsync_ReturnsStructuredServerError()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);

            var invocation = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                options: null,
                CancellationToken.None);

            runtime.EmitResponse(ResponseFor(
                runtime.SingleSentRequest,
                result: null,
                status: "not_found",
                message: "room service is unavailable"));

            yield return Await(invocation);
            var result = invocation.GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServRpcErrorCode.ServerError));
            Assert.That(result.Error.Status, Is.EqualTo("not_found"));
            Assert.That(result.Error.Message, Is.EqualTo("room service is unavailable"));
        }

        [UnityTest]
        public IEnumerator InvokeAsync_ReturnsTimeoutAndDoesNotConsumeNextResponse()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);
            var timedOut = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                new PlayServRpcInvokeOptions
                {
                    RequestId = "expired",
                    Timeout = TimeSpan.FromMilliseconds(50)
                },
                CancellationToken.None);

            yield return Await(timedOut);
            var timeoutResult = timedOut.GetAwaiter().GetResult();
            Assert.That(timeoutResult.Error.Code, Is.EqualTo(PlayServRpcErrorCode.Timeout));

            var active = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                Options("active"),
                CancellationToken.None);

            runtime.EmitResponse(ResponseFor(
                new InvokeRpc
                {
                    RequestId = "expired",
                    ServiceName = "RoomService",
                    MethodName = "FindMatch"
                },
                "{\"MatchId\":\"late\"}"));
            Assert.That(active.IsCompleted, Is.False);

            runtime.EmitResponse(ResponseFor(runtime.SentRequests[1], "{\"MatchId\":\"active\"}"));
            yield return Await(active);
            Assert.That(active.GetAwaiter().GetResult().Value.MatchId, Is.EqualTo("active"));
        }

        [UnityTest]
        public IEnumerator InvokeAsync_ReturnsCanceledResult()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);
            var cancellation = new CancellationTokenSource();

            var invocation = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                Options("canceled"),
                cancellation.Token);
            cancellation.Cancel();

            yield return Await(invocation);
            var result = invocation.GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServRpcErrorCode.Canceled));
            cancellation.Dispose();
        }

        [UnityTest]
        public IEnumerator InvokeAsync_ReturnsDeserializationError()
        {
            var runtime = new FakeRuntimeAccess();
            var facade = new PlayServApiRpcFacade(runtime);
            var invocation = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                options: null,
                CancellationToken.None);

            runtime.EmitResponse(ResponseFor(runtime.SingleSentRequest, "{invalid-json"));

            yield return Await(invocation);
            var result = invocation.GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(PlayServRpcErrorCode.DeserializationFailed));
            Assert.That(result.Error.Exception, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator InvokeAsync_ReturnsTransportErrorWhenSendFails()
        {
            var runtime = new FakeRuntimeAccess
            {
                SendException = new InvalidOperationException("not connected")
            };
            var facade = new PlayServApiRpcFacade(runtime);

            var invocation = facade.InvokeAsync<FindMatchRequest, FindMatchResponse>(
                "RoomService",
                "FindMatch",
                new FindMatchRequest(),
                options: null,
                CancellationToken.None);

            yield return Await(invocation);
            var result = invocation.GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(PlayServRpcErrorCode.TransportFailed));
            Assert.That(result.Error.Exception.Message, Is.EqualTo("not connected"));
        }

        private static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                yield return null;

            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for the RPC test task.");
            if (task.IsFaulted)
                throw task.Exception?.InnerException ?? task.Exception;
        }

        private static PlayServRpcInvokeOptions Options(string requestId)
        {
            return new PlayServRpcInvokeOptions
            {
                RequestId = requestId,
                Timeout = TimeSpan.FromSeconds(2)
            };
        }

        private static InvokeRpcResponse ResponseFor(
            InvokeRpc request,
            string result,
            string status = "ok",
            string message = null)
        {
            return new InvokeRpcResponse
            {
                RequestId = request.RequestId,
                Status = status,
                Message = message,
                Request = new InvokeRpcRequestInfo
                {
                    RequestId = request.RequestId,
                    ServiceName = request.ServiceName,
                    MethodName = request.MethodName
                },
                Result = result,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public sealed class FindMatchRequest
        {
            public string Mode { get; set; }
        }

        public sealed class FindMatchResponse
        {
            public string MatchId { get; set; }
        }

        private sealed class FakeRuntimeAccess : IPlayServRpcRuntimeAccess
        {
            private readonly FakeLocalExecution _localExecution = new FakeLocalExecution();
            private readonly IJsonCodec _jsonCodec = new NewtonsoftJsonCodec();

            public event Action<string, object> ModuleCommandReceived;

            public List<InvokeRpc> SentRequests { get; } = new List<InvokeRpc>();

            public Exception SendException { get; set; }

            public InvokeRpc SingleSentRequest
            {
                get
                {
                    Assert.That(SentRequests.Count, Is.EqualTo(1));
                    return SentRequests[0];
                }
            }

            public ILocalRpcExecution LocalExecution => _localExecution;

            public bool HasCurrentInstance => true;

            public IJsonCodec ResolveJsonCodec() => _jsonCodec;

            public void Send<T>(T command)
            {
                Send(command, moduleName: null);
            }

            public void Send<T>(T command, string moduleName)
            {
                if (SendException != null)
                    throw SendException;

                if (command is InvokeRpc request)
                    SentRequests.Add(request);
            }

            public void EmitResponse(InvokeRpcResponse response)
            {
                ModuleCommandReceived?.Invoke("InvokeRpcResponse", response);
            }
        }

        private sealed class FakeLocalExecution : ILocalRpcExecution
        {
            public bool TryInvokeRpc(
                string serviceName,
                string methodName,
                string payloadBase64,
                bool hasTransport)
            {
                return false;
            }
        }
    }
}
