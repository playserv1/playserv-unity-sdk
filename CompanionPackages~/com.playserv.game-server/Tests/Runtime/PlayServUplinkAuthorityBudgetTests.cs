using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.GameServer;
using UnityEngine.TestTools;
using H = Playserv.Tests.Runtime.GameServer.PlayServGameServerUplinkTests;

namespace Playserv.Tests.Runtime.GameServer
{
    public sealed class PlayServUplinkAuthorityBudgetTests
    {
        private PlayServGameServerUplink _uplink;

        [UnityTest] public IEnumerator ResolveRefusalPreservesCodeWithoutRepeatingAmbiguousWrite() => H.Run(async () =>
        {
            PlayServGameServer.CancelForModuleShutdown();
            var socket = new H.Socket(PlayServUplinkAuthorityTests.Ack(PlayServUplinkAuthorityTests.Authority));
            var http = new H.Http { Handler = (_, __) => Task.FromResult(new PlayServGameServerHttpResponse
            { StatusCode = 200, Body = "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profiles\",\"singleton\":false}]}" }) };
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(), HttpTimeout = TimeSpan.FromMilliseconds(500) },
                http, () => DateTimeOffset.UtcNow, (delay, ct) => Task.Delay(delay, ct));
            _uplink = PlayServGameServer.Uplink; _uplink.SocketFactory = () => socket;
            await _uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource();
            var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one", ct: cancel.Token);
            await H.Until(() => socket.Sent.Any(frame => frame.Contains("\"data_write\"")));
            cancel.Cancel();
            var unknown = await write;
            var resolve = unknown.ResolveAsync();
            await H.Until(() => socket.Sent.Any(frame => frame.Contains("\"authority_status_request\"")));
            var status = GameServerServiceValues.Object(socket.Sent.Single(frame => frame.Contains("\"authority_status_request\"")));
            socket.Push(PlayServGameServerJson.Serialize(new { type = "authority_status", request_id = status["request_id"], error_code = 32103, error = "retry_session_expired" }));
            var result = await resolve;
            Assert.That(result.State, Is.EqualTo(PlayServUplinkWriteState.Unknown));
            Assert.That(result.Error.TransportCode, Is.EqualTo(32103));
            Assert.That(socket.Sent.Count(frame => frame.Contains("\"data_write\"")), Is.EqualTo(1));
        });

        [UnityTearDown] public IEnumerator Cleanup() => H.Run(async () =>
        {
            try { if (_uplink != null) await _uplink.DisconnectAsync(); }
            finally { PlayServGameServer.CancelForModuleShutdown(); }
        });

        [UnityTest] public IEnumerator ResolveBudgetIncludesWaitingForAnotherResolver() => H.Run(async () =>
        {
            PlayServGameServer.CancelForModuleShutdown();
            var socket = new H.Socket(PlayServUplinkAuthorityTests.Ack(PlayServUplinkAuthorityTests.Authority));
            var http = new H.Http { Handler = (_, __) => Task.FromResult(new PlayServGameServerHttpResponse
            { StatusCode = 200, Body = "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profiles\",\"singleton\":false,\"acl\":{\"server\":{\"read\":true,\"write\":true}}}]}" }) };
            PlayServGameServer.ConfigureForTesting(new PlayServGameServerOptions
            { BackendServerAddress = "https://api.playserv.test", ExecutorSlug = "arena", ServerKeyProvider = new H.Key(), HttpTimeout = TimeSpan.FromMilliseconds(250) },
                http, () => DateTimeOffset.UtcNow, (delay, ct) => Task.Delay(delay, ct));
            _uplink = PlayServGameServer.Uplink; _uplink.SocketFactory = () => socket;
            await _uplink.ConnectAsync();
            using var cancel = new CancellationTokenSource();
            var write = _uplink.Data.DeleteWithOutcomeAsync("Profiles", "one", ct: cancel.Token);
            await H.Until(() => socket.Sent.Any(frame => frame.Contains("\"data_write\"")));
            cancel.Cancel();
            var unknown = await write;
            var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
            var resolver = (Delegate)typeof(PlayServUplinkWriteOutcome).GetField("_resolve", flags).GetValue(unknown);
            var mutationField = resolver.Target.GetType().GetFields(flags).Single(f => f.FieldType.Name == "DataMutation");
            var mutation = mutationField.GetValue(resolver.Target);
            var gate = (SemaphoreSlim)mutation.GetType().GetField("Gate", flags).GetValue(mutation);
            await gate.WaitAsync();
            Task<PlayServUplinkWriteOutcome> resolving = null;
            try
            {
                resolving = unknown.ResolveAsync();
                var completed = await Task.WhenAny(resolving, Task.Delay(1000));
                Assert.That(completed, Is.SameAs(resolving), "The HttpTimeout must include the serialized resolver wait.");
                Assert.That((await resolving).State, Is.EqualTo(PlayServUplinkWriteState.Unknown));
            }
            finally
            {
                gate.Release();
                if (resolving != null) await resolving;
            }
        });
    }
}
