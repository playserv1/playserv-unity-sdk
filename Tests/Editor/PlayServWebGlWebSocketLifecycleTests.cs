using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Proxy.Implementation;
using Playserv.Runtime.Abstractions;
using ILogger = Playserv.Proxy.Logging.ILogger;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServWebGlWebSocketLifecycleTests
    {
        private readonly List<WebGLWebSocketTransportImplementation> _transports =
            new List<WebGLWebSocketTransportImplementation>();

        [TearDown]
        public void TearDown()
        {
            foreach (var transport in _transports)
                transport.Dispose();
            _transports.Clear();
            foreach (var bridge in Resources.FindObjectsOfTypeAll<WebGLWebSocketBridge>())
                UnityEngine.Object.DestroyImmediate(bridge.gameObject);
        }

        [Test]
        public void ParallelConnectionsHaveIndependentBridgesAndPendingResults()
        {
            var firstApi = new FakeNativeApi();
            var secondApi = new FakeNativeApi();
            var first = Create(firstApi);
            var second = Create(secondApi);
            var firstConnect = first.Connect();
            var secondConnect = second.Connect();

            Assert.That(firstApi.Id, Is.Not.EqualTo(secondApi.Id));
            secondApi.Bridge.OnWsOpen(string.Empty);
            Assert.That(secondConnect.Result, Is.True);
            Assert.That(firstConnect.IsCompleted, Is.False);
            firstApi.Bridge.OnWsOpen(string.Empty);
            Assert.That(firstConnect.Result, Is.True);

            first.Send(Encoding.UTF8.GetBytes("first")).GetAwaiter().GetResult();
            second.Send(Encoding.UTF8.GetBytes("second")).GetAwaiter().GetResult();
            Assert.That(firstApi.Sent, Is.EqualTo(new[] { "first" }));
            Assert.That(secondApi.Sent, Is.EqualTo(new[] { "second" }));
            first.Dispose();
            second.Send(Encoding.UTF8.GetBytes("still connected")).GetAwaiter().GetResult();
            Assert.That(firstApi.Closed, Has.Count.EqualTo(1));
            Assert.That(secondApi.Closed, Is.Empty);
        }

        [Test]
        public void DisposeCompletesPendingConnectAndRetiresItsBridge()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            var bridge = api.Bridge;
            transport.Dispose();

            Assert.That(connect.IsCompleted, Is.True, "Disposed connect must not wait for its timeout.");
            Assert.That(connect.Result, Is.False);
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
            Assert.That(bridge == null, Is.True);
        }

        [Test]
        public void ResetCreatesNewAttemptAndOldBridgeCannotCompleteIt()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var first = transport.Connect();
            var oldId = api.Id;
            var oldBridge = api.Bridge;
            transport.ResetConnection();
            var next = transport.Connect();

            Assert.That(first.Result, Is.False);
            Assert.That(api.Id, Is.Not.EqualTo(oldId));
            oldBridge.OnWsOpen(string.Empty);
            oldBridge.OnWsError("stale");
            oldBridge.OnWsClose("1008:stale");
            Assert.That(next.IsCompleted, Is.False);
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(next.Result, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RemoteFailureRetiresOnlyFailedAttempt(bool isError)
        {
            var firstApi = new FakeNativeApi();
            var secondApi = new FakeNativeApi();
            var first = Create(firstApi);
            var second = Create(secondApi);
            var firstConnect = first.Connect();
            var secondConnect = second.Connect();
            var bridge = firstApi.Bridge;
            if (isError)
                bridge.OnWsError("failure");
            else
                bridge.OnWsClose("1008:policy");

            Assert.That(firstConnect.Result, Is.False);
            Assert.That(bridge == null, Is.True);
            Assert.That(secondConnect.IsCompleted, Is.False);
            Assert.That(secondApi.Closed, Is.Empty);
        }

        [Test]
        public void CloseIsDeliveredBeforePendingConnectContinuationUnsubscribes()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var delivered = 0;
            Action<PlayServTransportCloseInfo> onClosed = _ => delivered++;
            transport.Closed += onClosed;
            var connect = transport.Connect();
            var continuation = connect.ContinueWith(
                _ => transport.Closed -= onClosed,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            api.Bridge.OnWsClose("1008:policy");

            Assert.That(continuation.IsCompleted, Is.True);
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(connect.Result, Is.False);
        }

        [Test]
        public void CloseCannotReachReplacementStartedByPendingConnectContinuation()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var oldDelivered = 0;
            var replacementDelivered = 0;
            Action<PlayServTransportCloseInfo> onClosed = _ => oldDelivered++;
            transport.Closed += onClosed;
            var connect = transport.Connect();
            var oldBridge = api.Bridge;
            var oldId = api.Id;
            Task<bool> replacement = null;
            var continuation = connect.ContinueWith(_ =>
            {
                transport.Closed -= onClosed;
                transport.Closed += __ => replacementDelivered++;
                replacement = transport.Connect();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            oldBridge.OnWsClose("1008:policy");

            Assert.That(continuation.IsCompleted, Is.True);
            Assert.That(oldDelivered, Is.EqualTo(1));
            Assert.That(replacementDelivered, Is.Zero);
            Assert.That(api.Id, Is.Not.EqualTo(oldId));
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement.IsCompleted, Is.False);
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(replacement.Result, Is.True);
        }

        [Test]
        public void ThrowingCloseHandlerCannotStrandPendingConnect()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            transport.Closed += _ => throw new InvalidOperationException("application callback failed");
            var connect = transport.Connect();
            var bridge = api.Bridge;

            Assert.Throws<InvalidOperationException>(() => bridge.OnWsClose("1008:policy"));

            Assert.That(connect.IsCompleted, Is.True);
            Assert.That(connect.Result, Is.False);
            Assert.That(bridge == null, Is.True);
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
        }

        [UnityTest]
        public IEnumerator TimeoutRetiresItsBridgeAndCompletesPendingConnect()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            var bridge = api.Bridge;
            var deadline = UnityEditor.EditorApplication.timeSinceStartup + 15;
            while (!connect.IsCompleted && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                yield return null;

            Assert.That(connect.IsCompleted, Is.True);
            Assert.That(connect.Result, Is.False);
            Assert.That(bridge == null, Is.True);
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
        }

        [Test]
        public void OutboundPayloadLimitStillAppliesBeforeNativeSend()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(connect.Result, Is.True);

            transport.Send(new byte[Playserv.Wrapper.PlayServWebSocketPayloadLimits.MaxMessageBytes])
                .GetAwaiter().GetResult();
            Assert.That(api.Sent, Has.Count.EqualTo(1));
            Assert.Throws<Playserv.Wrapper.PlayServWebSocketPayloadException>(() => transport.Send(
                new byte[Playserv.Wrapper.PlayServWebSocketPayloadLimits.MaxMessageBytes + 1]));
            Assert.That(api.Sent, Has.Count.EqualTo(1));
            Assert.That(api.Closed, Is.Empty);
        }

        [Test]
        public void SendFailureRetiresBridgeWithoutWaitingForBrowserClose()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(connect.Result, Is.True);
            var bridge = api.Bridge;
            api.SendResult = 0;
            Assert.Throws<InvalidOperationException>(() => transport.Send(Encoding.UTF8.GetBytes("message")));
            Assert.That(bridge == null, Is.True);
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
        }

        [Test]
        public void NativeConnectFailureCompletesAttemptAndRetiresBridge()
        {
            var api = new FakeNativeApi { ThrowOnConnect = true };
            var transport = Create(api);
            var connect = transport.Connect();
            Assert.That(connect.IsCompleted, Is.True);
            Assert.That(connect.Result, Is.False);
            Assert.That(api.Bridge == null, Is.True);
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
        }

        [TestCase("left\0right")]
        [TestCase("гра😀\0続き")]
        [TestCase("\uFEFFkept")]
        [TestCase("")]
        public void NativeMessageCallbackDecodesTextWithoutTruncatingNul(string text)
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(connect.Result, Is.True);
            string received = null;
            api.Bridge.MessageReceived += message => received = message;

            api.Bridge.OnWsMessage(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));

            Assert.That(received, Is.EqualTo(text));
        }

        [Test]
        public void NativeMessageCallbackLimitMeasuresDecodedText()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(connect.Result, Is.True);
            PlayServTransportCloseInfo closed = null;
            transport.Closed += info => closed = info;
            var allowed = new byte[Playserv.Wrapper.PlayServWebSocketPayloadLimits.MaxMessageBytes];

            api.Bridge.OnWsMessage(Convert.ToBase64String(allowed));
            Assert.That(closed, Is.Null);
            Assert.That(api.Closed, Is.Empty);

            api.Bridge.OnWsMessage(Convert.ToBase64String(new byte[allowed.Length + 1]));
            Assert.That(closed, Is.Not.Null);
            Assert.That(closed.StatusCode, Is.EqualTo(1009));
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
        }

        [Test]
        public void MalformedNativeMessageCallbackIsRejectedWithoutEchoingPayload()
        {
            var api = new FakeNativeApi();
            var transport = Create(api);
            var connect = transport.Connect();
            api.Bridge.OnWsOpen(string.Empty);
            Assert.That(connect.Result, Is.True);
            string received = null;
            string error = null;
            var bridge = api.Bridge;
            bridge.MessageReceived += message => received = message;
            bridge.ErrorReceived += message => error = message;

            bridge.OnWsMessage("invalid base64: secret frame");

            Assert.That(received, Is.Null);
            Assert.That(error, Is.Not.Null);
            StringAssert.DoesNotContain("secret", error);
            Assert.That(bridge == null, Is.True);
            Assert.That(api.Closed, Is.EqualTo(new[] { api.Id }));
        }

        private WebGLWebSocketTransportImplementation Create(FakeNativeApi api)
        {
            var transport = new WebGLWebSocketTransportImplementation("wss://fixture.invalid/", new SilentLogger(), api);
            _transports.Add(transport);
            return transport;
        }

        private sealed class FakeNativeApi : IWebGLWebSocketApi
        {
            public string Id;
            public WebGLWebSocketBridge Bridge;
            public int SendResult = 1;
            public bool ThrowOnConnect;
            public readonly List<string> Sent = new List<string>();
            public readonly List<string> Closed = new List<string>();
            public void Connect(string connectionId, string url)
            {
                Id = connectionId;
                Bridge = GameObject.Find(connectionId).GetComponent<WebGLWebSocketBridge>();
                if (ThrowOnConnect)
                    throw new InvalidOperationException("native failure details");
            }
            public int Send(string connectionId, string message)
            {
                Assert.That(connectionId, Is.EqualTo(Id));
                Sent.Add(message);
                return SendResult;
            }
            public void Close(string connectionId) => Closed.Add(connectionId);
        }

        private sealed class SilentLogger : ILogger
        {
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }
}
