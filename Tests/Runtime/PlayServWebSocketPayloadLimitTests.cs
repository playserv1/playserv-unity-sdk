using System;
using NUnit.Framework;
using Playserv.Proxy.Implementation;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServWebSocketPayloadLimitTests
    {
        [Test]
        public void PayloadGuardAllowsExactBoundAndRejectsNextByte()
        {
            Assert.DoesNotThrow(() => PlayServWebSocketPayloadLimits.EnsureAllowed(
                PlayServWebSocketPayloadLimits.MaxMessageBytes,
                PlayServWebSocketPayloadDirection.Inbound));

            var exception = Assert.Throws<PlayServWebSocketPayloadException>(() =>
                PlayServWebSocketPayloadLimits.EnsureAllowed(
                    PlayServWebSocketPayloadLimits.MaxMessageBytes + 1L,
                    PlayServWebSocketPayloadDirection.Inbound));

            Assert.That(exception.Direction, Is.EqualTo(PlayServWebSocketPayloadDirection.Inbound));
            Assert.That(exception.ActualBytes,
                Is.EqualTo(PlayServWebSocketPayloadLimits.MaxMessageBytes + 1L));
            Assert.That(exception.UnifiedError.TransportCode, Is.EqualTo(1009));
            Assert.That(exception.UnifiedError.SourceCode,
                Is.EqualTo("websocket_payload_too_large"));
            Assert.That(exception.UnifiedError.Retryable, Is.False);
        }

        [Test]
        public void DesktopTransportRejectsOversizedOutboundPayloadBeforeConnectionCheck()
        {
            using var transport = new WebSocketTransportImplementation("ws://localhost");
            var payload = new byte[PlayServWebSocketPayloadLimits.MaxMessageBytes + 1];

            var exception = Assert.Throws<PlayServWebSocketPayloadException>(() =>
                transport.Send(payload).GetAwaiter().GetResult());

            Assert.That(exception.Direction,
                Is.EqualTo(PlayServWebSocketPayloadDirection.Outbound));
        }

        [Test]
        public void DesktopTransportStillUsesConnectionErrorForAllowedPayload()
        {
            using var transport = new WebSocketTransportImplementation("ws://localhost");
            LogAssert.Expect(
                LogType.Error,
                "[PlayServ][transport] Attempted to send data but WebSocket is not connected.");

            Assert.Throws<InvalidOperationException>(() =>
                transport.Send(Array.Empty<byte>()).GetAwaiter().GetResult());
        }
    }
}
