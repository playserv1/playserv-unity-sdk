using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using Playserv.RPC;

namespace Playserv.Tests.Runtime.RPC
{
    public sealed class PlayServRpcRoutingLifecycleTests
    {
        [TestCase("InvokeRpcResponse")]
        [TestCase("rpc.InvokeRpcResponse")]
        [TestCase("rpc.InvokeRpc.InvokeRpcResponse")]
        public void FirstResponse_AndRecreatedSession_DeliverExactlyOnce(string command)
        {
            // A previous session can populate this static registry and conceal the cold-start bug.
            using var registry = new ColdRpcRouteRegistry();
            var deliveries = 0;
            for (var generation = 1; generation <= 2; generation++)
            {
                var transport = new TestTransport();
                using (var session = new PlayServImplementation("ws://example.test", (_, __) => transport, host =>
                {
                    host.Register(new PlayServRpcCoreModule());
                    host.Register(new PlayServClientRpcModule());
                }))
                {
                    session.OnModuleCommand += (name, value) =>
                    {
                        Assert.That(name, Is.EqualTo("InvokeRpcResponse"));
                        var response = value as InvokeRpcResponse;
                        Assert.That(response, Is.Not.Null);
                        Assert.That(response.Request.MethodName, Is.EqualTo("FindMatch"));
                        deliveries++;
                    };
                    Assert.That(session.ModuleServices.Get<ITransport>().Connect().GetAwaiter().GetResult(), Is.True);
                    transport.Emit("{\"Command\":\"" + command + "\",\"Payload\":{\"Status\":\"ok\",\"Request\":{\"ServiceName\":\"eggie-room\",\"MethodName\":\"FindMatch\"},\"Result\":\"{}\"}}");
                    Assert.That(deliveries, Is.EqualTo(generation));
                }
                Assert.That(transport.ObserverCount, Is.Zero);
                Assert.That(transport.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void RpcRemainsOptional()
        {
            using var registry = new ColdRpcRouteRegistry();
            var transport = new TestTransport();
            using (var session = new PlayServImplementation("ws://example.test", (_, __) => transport))
            {
                Assert.That(session.HasModule(PlayServModuleIds.ClientRpc), Is.False);
                Assert.That(session.ModuleServices.Get<ITransport>().Connect().GetAwaiter().GetResult(), Is.True);
            }
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void FailedInitialization_DisposesTransportAndInitializedModules_PreservingOriginalError()
        {
            var transport = new TestTransport();
            var initialized = new TrackingModule();
            var error = Assert.Throws<InvalidOperationException>(() =>
                new PlayServImplementation("ws://example.test", (_, __) => transport, host =>
                {
                    host.Register(initialized);
                    host.Register(new FailingModule());
                }));
            Assert.That(error.Message, Is.EqualTo("test initialization failure"));
            Assert.That(initialized.ShutdownCount, Is.EqualTo(1));
            Assert.That(transport.DisposeCount, Is.EqualTo(1));
        }

        private sealed class ColdRpcRouteRegistry : IDisposable
        {
            private readonly List<ICommandRouteProvider> _providers;
            private readonly HashSet<Type> _types;
            private readonly ICommandRouteProvider[] _saved;
            private readonly Type[] _savedTypes;
            private readonly object _sync;

            public ColdRpcRouteRegistry()
            {
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                var registryType = typeof(CommandRouteProviderRegistry);
                _providers = (List<ICommandRouteProvider>)registryType.GetField("Providers", flags).GetValue(null);
                _types = (HashSet<Type>)registryType.GetField("ProviderTypes", flags).GetValue(null);
                _sync = registryType.GetField("Sync", flags).GetValue(null);
                lock (_sync)
                {
                    _saved = _providers.ToArray();
                    _savedTypes = new Type[_types.Count];
                    _types.CopyTo(_savedTypes);
                    _providers.RemoveAll(provider => provider.GetType().FullName == "Playserv.RPC.RpcCommandRouteProvider");
                    _types.RemoveWhere(type => type.FullName == "Playserv.RPC.RpcCommandRouteProvider");
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _providers.Clear();
                    _providers.AddRange(_saved);
                    _types.Clear();
                    _types.UnionWith(_savedTypes);
                }
            }
        }

        private sealed class TrackingModule : IPlayServModule
        {
            public int ShutdownCount;
            public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor("test-cleanup", false);
            public void Initialize(PlayServModuleContext context) { }
            public void Shutdown() => ShutdownCount++;
        }

        private sealed class FailingModule : IPlayServModule
        {
            public PlayServModuleDescriptor Descriptor { get; } = new PlayServModuleDescriptor("test-failure", false, "test-cleanup");
            public void Initialize(PlayServModuleContext context) => throw new InvalidOperationException("test initialization failure");
            public void Shutdown() { }
        }

        private sealed class TestTransport : ITransportImplementation, IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            public int ObserverCount => _observers.Count;
            public int DisposeCount { get; private set; }
            public Task<bool> Connect() => Task.FromResult(true);
            public Task Send(byte[] data) => Task.CompletedTask;
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => this;
            public void Dispose() => DisposeCount++;
            public void Emit(string json)
            {
                foreach (var observer in _observers.ToArray())
                    observer.OnNext(Encoding.UTF8.GetBytes(json));
            }
            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                _observers.Add(observer);
                return new Subscription(() => _observers.Remove(observer));
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }
}
