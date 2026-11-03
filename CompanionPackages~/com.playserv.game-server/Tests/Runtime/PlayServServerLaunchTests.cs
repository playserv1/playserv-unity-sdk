using System;
using System.Collections.Generic;
using NUnit.Framework;
using Playserv.GameServer;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServServerLaunchTests
    {
        [TestCase("{\"7777\":9000}", "udp")]
        [TestCase("{\"7777/udp\":\"9000\"}", "udp")]
        [TestCase("{\"7777/tcp\":{\"external_port\":9000}}", "tcp")]
        [TestCase("[{\"internal_port\":7777,\"external_port\":9000,\"protocol\":\"tcp\"}]", "tcp")]
        public void NeutralMappingsAreCompatible(string mapping, string protocol)
        {
            var read = new List<string>();
            var result = PlayServServerLaunch.ResolveConnect(7777, null, name =>
            { read.Add(name); return name == "PLAYSERV_PUBLIC_IP" ? "game.example.test" : mapping; });
            Assert.That(result.Host, Is.EqualTo("game.example.test")); Assert.That(result.Port, Is.EqualTo(9000));
            Assert.That(result.Transport, Is.EqualTo(protocol));
            Assert.That(read, Is.EqualTo(new[] { "PLAYSERV_PUBLIC_IP", "PLAYSERV_PORTS_MAPPING" }));
        }
        [TestCase("not-json")][TestCase("{\"7777\":1.5}")][TestCase("{\"7777\":65536}")][TestCase("true")]
        public void InvalidMappingFailsWithoutPrintingInput(string mapping)
        {
            var error = Assert.Throws<ArgumentException>(() => PlayServServerLaunch.ResolveConnect(7777, null,
                name => name == "PLAYSERV_PUBLIC_IP" ? "host" : mapping));
            Assert.That(error.Message, Does.Not.Contain(mapping));
        }
        [Test] public void ExplicitValuesWinAndMissingEnvironmentIsNormal()
        {
            var expected = new PlayServGameRoomConnect("host", 9, "custom");
            Assert.That(PlayServServerLaunch.ResolveConnect(7777, expected, _ => throw new Exception()), Is.SameAs(expected));
            Assert.That(PlayServServerLaunch.ResolveConnect(7777, null, _ => null), Is.Null);
            Assert.That(PlayServServerLaunch.GetRoomName("room:one", _ => throw new Exception()), Is.EqualTo("room:one"));
            Assert.That(PlayServServerLaunch.GetRoomName(null, _ => null), Is.Null);
            Assert.Throws<ArgumentException>(() => PlayServServerLaunch.GetRoomName(null, _ => "invalid/name"));
        }
    }
}
