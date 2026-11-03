using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Logging;

namespace Playserv.Tests.Runtime
{
    public sealed class OutboundPacketDiagnosticsTests
    {
        [Test]
        public void ResolvePacketName_UsesRpcMethodName()
        {
            var envelope = new MessageEnvelope(
                "rpc.InvokeRpc",
                "{\"MethodName\":\"Fallback\"}");
            var command = new RpcCommand { MethodName = "Respawn" };

            var packetName = OutboundPacketDiagnostics.ResolvePacketName(envelope, command);

            Assert.That(packetName, Is.EqualTo("rpc:Respawn"));
        }

        [Test]
        public void ResolvePacketName_UsesEnvelopeCommandForNonRpcPackets()
        {
            var envelope = new MessageEnvelope(
                "events.PublishEvent",
                "{\"EventType\":\"ChatMessage\"}");

            var packetName = OutboundPacketDiagnostics.ResolvePacketName(envelope, new object());

            Assert.That(packetName, Is.EqualTo("events.PublishEvent"));
        }

        [Test]
        public void Register_PreservesStructuredPacketNameForSocketWrite()
        {
            var envelope = new MessageEnvelope("rpc.InvokeRpc", "{}");
            var command = new RpcCommand { MethodName = "SetInput" };
            var data = Encoding.UTF8.GetBytes("not-json");

            OutboundPacketDiagnostics.Register(envelope, command, data);

            Assert.That(
                OutboundPacketDiagnostics.ResolvePacketName(data),
                Is.EqualTo("rpc:SetInput"));
        }

        [Test]
        public void ResolvePacketName_FallsBackToWirePayload()
        {
            var data = Encoding.UTF8.GetBytes(
                "{\"Command\":\"rpc.InvokeRpc\",\"Payload\":{\"MethodName\":\"Shoot\"}}");

            Assert.That(
                OutboundPacketDiagnostics.ResolvePacketName(data),
                Is.EqualTo("rpc:Shoot"));
        }

        [Test]
        public void SocketWrite_LogsRespawnEnterAndDone()
        {
            var logger = new RecordingLogger();
            var data = Encoding.UTF8.GetBytes("{}");
            OutboundPacketDiagnostics.Register(
                new MessageEnvelope("rpc.InvokeRpc", "{}"),
                new RpcCommand { MethodName = "Respawn" },
                data);

            var isRespawn = OutboundPacketDiagnostics.BeginSocketWrite(data, logger);
            OutboundPacketDiagnostics.CompleteSocketWrite(isRespawn, logger);

            Assert.That(isRespawn, Is.True);
            Assert.That(logger.Warnings.Any(message => message.StartsWith("[RESP-WIRE] enter")), Is.True);
            Assert.That(logger.Warnings.Any(message => message.StartsWith("[RESP-WIRE] done")), Is.True);
        }

        private sealed class RpcCommand
        {
            public string MethodName { get; set; }
        }

        private sealed class RecordingLogger : ILogger
        {
            public List<string> Warnings { get; } = new List<string>();

            public void Log(string message)
            {
            }

            public void LogWarning(string message)
            {
                Warnings.Add(message);
            }

            public void LogError(string message)
            {
            }
        }
    }
}
