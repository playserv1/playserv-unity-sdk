using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Commerce;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Http.Interfaces;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Implementation;
using Playserv.Proxy.Interfaces;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServPurchasedItemsTests
    {
        [Test]
        public void Read_returns_current_players_counts_and_archived_item_metadata()
        {
            using var fixture = new Fixture();
            var result = fixture.Client.GetPurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.PlayerId, Is.EqualTo("player-a"));
            Assert.That(result.Items.Select(x => x.Count), Is.EqualTo(new long[] { 7, 1 }));
            Assert.That(result.Items[1].Status, Is.EqualTo("archived"));
            Assert.That(result.Items[1].Name, Is.EqualTo("Retired skin"));
            Assert.That(result.Items[1].ImageUrl, Is.Null);
            Assert.That(result.Items[0].FirstPurchasedAt.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            var request = fixture.HttpRequests.Single();
            Assert.That(request.RelativePath, Is.EqualTo("catalog/purchased-items"));
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.ClientToken, Is.EqualTo("pk_fixture"));
            Assert.That(request.BearerToken, Is.EqualTo("player.jwt"));
            Assert.That(request.RequiresClientToken, Is.True);
        }

        [Test]
        public void Subscription_sends_purchased_items_root_from_the_platform_contract()
        {
            using var fixture = new Fixture { HoldOpen = true };
            using var cancellation = new CancellationTokenSource();
            var pending = fixture.Api.SubscribePurchasedItemsAsync(cancellation.Token);
            try
            {
                Assert.That(fixture.Payload("DataSubscriptionRequest")["Query"].Value<string>(),
                    Is.EqualTo("_PurchasedItems { * }"));
            }
            finally
            {
                cancellation.Cancel();
            }
        }

        [Test]
        public void Read_requires_player_authentication_before_sending()
        {
            using var fixture = new Fixture();
            fixture.Settings.PlayerAccessToken = null;
            var error = Assert.Throws<PlayServCommerceException>(() => fixture.Client.GetPurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Unauthorized));
            Assert.That(fixture.HttpRequests, Is.Empty);
        }

        [Test]
        public void Read_resolves_refreshed_player_token_without_an_acting_player_override()
        {
            using var fixture = new Fixture();
            fixture.Settings.RuntimeTokenProvider = new PlayServDelegateRuntimeTokenProvider(_ => Task.FromResult("refreshed.jwt"));
            fixture.Client.GetPurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(fixture.HttpRequests.Single().BearerToken, Is.EqualTo("refreshed.jwt"));
            Assert.That(fixture.HttpRequests.Single().RelativePath, Does.Not.Contain("player"));
        }

        [Test]
        public void Read_empty_inventory_is_a_valid_complete_list()
        {
            using var fixture = new Fixture { Empty = true };
            var result = fixture.Client.GetPurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Items, Is.Empty);
        }

        [TestCase("{}")]
        [TestCase("{\"player_id\":\"player-a\",\"items\":null}")]
        [TestCase("{\"player_id\":\"player-a\",\"items\":[{\"item_id\":\"x\",\"count\":0}]}")]
        public void Read_rejects_invalid_inventory_instead_of_silently_losing_items(string body)
        {
            using var fixture = new Fixture { HttpBody = body };
            Assert.Throws<PlayServCommerceException>(() => fixture.Client.GetPurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void Read_cancellation_does_not_send_a_request()
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Client.GetPurchasedItemsAsync(cancellation.Token).GetAwaiter().GetResult());
            Assert.That(fixture.HttpRequests, Is.Empty);
        }

        [Test]
        public void Subscription_uses_reserved_root_and_replaces_whole_list_on_purchase_and_refund()
        {
            using var fixture = new Fixture();
            using var subscription = fixture.Api.SubscribePurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(fixture.Payload("DataSubscriptionRequest")["Query"].Value<string>(), Is.EqualTo("_PurchasedItems { * }"));
            Assert.That(subscription.Items.Select(x => x.Count), Is.EqualTo(new long[] { 7, 1 }));
            var changes = new List<long>();
            subscription.Changed += items => changes.Add(items.Sum(x => x.Count));
            fixture.Count = 10;
            fixture.Update();
            fixture.Empty = true;
            fixture.Update();
            Assert.That(changes, Is.EqualTo(new long[] { 11, 0 }));
            Assert.That(subscription.Items, Is.Empty);
            Assert.That(fixture.HttpRequests, Is.Empty, "Subscriptions must not poll HTTP or resolve a studio table.");
        }

        [Test]
        public void Reconnect_resubscribes_and_ignores_old_subscription_frames()
        {
            using var fixture = new Fixture();
            using var subscription = fixture.Api.SubscribePurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            fixture.ServerId = 20;
            fixture.Count = 9;
            fixture.HoldOpen = true;
            fixture.Adapter.OnConnected();
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Reconnecting));
            fixture.FinishOpen();
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(subscription.Items[0].Count, Is.EqualTo(9));
            fixture.Update(10, 99);
            Assert.That(subscription.Items[0].Count, Is.EqualTo(9));
            Assert.That(fixture.Commands.Count(x => x == "DataSubscriptionRequest"), Is.EqualTo(2));
        }

        [Test]
        public void Disposed_subscription_ignores_updates_and_is_not_reopened()
        {
            using var fixture = new Fixture();
            var subscription = fixture.Api.SubscribePurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            var changes = 0;
            subscription.Changed += _ => changes++;
            subscription.Dispose();
            fixture.Update();
            fixture.Adapter.OnConnected();
            Assert.That(changes, Is.Zero);
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            Assert.That(fixture.Commands.Count(x => x == "DataSubscriptionRequest"), Is.EqualTo(1));
            Assert.That(fixture.Commands, Does.Contain("DataSubscriptionCloseRequest"));
        }

        [UnityTest]
        public IEnumerator Cancel_pending_open_ignores_late_reply_and_closes_server_subscription()
        {
            using var fixture = new Fixture { HoldOpen = true };
            using var cancellation = new CancellationTokenSource();
            var pending = fixture.Api.SubscribePurchasedItemsAsync(cancellation.Token);
            Assert.That(pending.IsCompleted, Is.False);
            cancellation.Cancel();
            for (var i = 0; i < 60 && !pending.IsCompleted; i++) yield return null;
            Assert.That(pending.IsCanceled, Is.True);
            fixture.FinishOpen();
            for (var i = 0; i < 60 && !fixture.Commands.Contains("DataSubscriptionCloseRequest"); i++) yield return null;
            Assert.That(fixture.Commands, Does.Contain("DataSubscriptionCloseRequest"));
        }

        [Test]
        public void Changing_player_terminates_old_inventory_subscription()
        {
            using var fixture = new Fixture();
            using var subscription = fixture.Api.SubscribePurchasedItemsAsync(CancellationToken.None).GetAwaiter().GetResult();
            var terminated = 0;
            subscription.Terminated += () => terminated++;
            fixture.PlayerId = "player-b";
            fixture.Adapter.OnConnected();
            fixture.Update();
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(terminated, Is.EqualTo(1));
            Assert.That(fixture.Commands.Count(x => x == "DataSubscriptionRequest"), Is.EqualTo(1));
        }

        private sealed class Fixture : IPlayServCommandBus, IPlayServRuntimeIdentity, ITransportImplementation,
            IPlayServRuntimeHttpClient, IPlayServDataRuntimeAccess, IDisposable
        {
            private readonly Incoming _incoming = new Incoming();
            private readonly NewtonsoftJsonCodec _json = new NewtonsoftJsonCodec();
            private readonly Transport _transport;
            private readonly List<JObject> _frames = new List<JObject>();
            private long _pendingRequest;
            private int _sequence;
            public readonly PlayServSettings Settings = new PlayServSettings
            {
                ClientToken = "pk_fixture", PlayerAccessToken = "player.jwt", BackendServerAddress = "https://purchased-items.test"
            };
            public PlayServCommerceClient Client { get; }
            public PlayServApiDataFacade Api { get; }
            public PlayServDataSubscriptionAdapter Adapter { get; }
            public IPlayServModuleServiceProvider RequiredServices { get; }
            public readonly List<PlayServRuntimeDataRequest> HttpRequests = new List<PlayServRuntimeDataRequest>();
            public bool Empty, HoldOpen;
            public string HttpBody;
            public long Count = 7, ServerId = 10;
            public string PlayerId { get; set; } = "player-a";
            public PlayServState State => PlayServState.Online;
            public List<string> Commands => _frames.Select(x => ((string)x["Command"]).Split('.').Last()).ToList();

            public Fixture()
            {
                CommandTypeProviderRegistry.Register(new DataSubscriptionCommandTypeProvider());
                var logger = new SilentLogger();
                _transport = new Transport(this, new Playserv.Proxy.Implementation.JsonSerializer(_json, new NewtonsoftCommandPayloadMapper()), new RequestIdGenerator(), logger);
                Adapter = new PlayServDataSubscriptionAdapter(this, logger, _json);
                var services = new PlayServModuleServiceRegistry();
                services.Register(Adapter);
                RequiredServices = services;
                Api = new PlayServApiDataFacade(this);
                Client = new PlayServCommerceClient(Settings, this, _json);
            }

            private object[] Items(long count) => Empty ? Array.Empty<object>() : new object[]
            {
                new { item_id = "itm_coins", sku = "coins", name = "Coins", image_url = "https://cdn.test/coins.png", product_type = "consumable", status = "published", count, first_purchased_at = "2026-11-13T09:00:00Z", last_purchased_at = "2026-11-13T12:00:00+03:00" },
                new { item_id = "itm_skin", sku = "skin", name = "Retired skin", image_url = (string)null, product_type = "non_consumable", status = "archived", count = 1 }
            };
            public JToken Payload(string command) => _frames.Last(x => ((string)x["Command"]).EndsWith("." + command))["Payload"];
            public IDisposable On<T>(Action<T> onNext) => _transport.OnReceive<T>().Subscribe(new Observer<T>(onNext));
            public IDisposable OnCommand(string command, Action<object> onNext) => _transport.OnReceive(command).Subscribe(new Observer<object>(onNext));
            public Task SendAsync<T>(T command, string moduleName = null) => _transport.Send(command, moduleName);
            public Task<bool> Connect() => Task.FromResult(true);
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => _incoming;
            public Task Send(byte[] bytes)
            {
                var frame = JObject.Parse(Encoding.UTF8.GetString(bytes));
                _frames.Add(frame);
                var command = ((string)frame["Command"]).Split('.').Last();
                var request = (long)frame["Payload"]["RequestId"];
                if (command == "DataAuthorityContextRequest")
                    Emit("DataAuthorityContextResponse", new { RequestId = request, Result = new { Capability = "authoritative_state_v1", Epoch = "epoch-a", RetrySession = "fixture-session", RetryWindowMs = 120000 } });
                if (command == "DataSubscriptionRequest")
                {
                    _pendingRequest = request;
                    if (!HoldOpen) FinishOpen();
                }
                if (command == "DataSubscriptionCloseRequest")
                    Emit("DataSubscriptionCloseResponse", new { RequestId = request, Result = new { Success = true } });
                return Task.CompletedTask;
            }
            public void FinishOpen()
            {
                Emit("DataSubscriptionResponse", new { RequestId = _pendingRequest, Result = new { SubscriptionId = ServerId, Authority = new { Epoch = "epoch-a" } } });
                Update(requestId: _pendingRequest);
            }
            public void Update(long? id = null, long? count = null, long requestId = 0) =>
                Emit("DataSubscriptionUpdate", new { RequestId = requestId, DataSubscriptionId = id ?? ServerId, UpdateType = "Overwrite", IsCollection = true, EntityType = "_PurchasedItems", Data = new { _PurchasedItems = Items(count ?? Count) }, Authority = new { Epoch = "epoch-a", Sequence = (++_sequence).ToString(), Revision = "1", Complete = true } });
            private void Emit(string command, object payload) => _incoming.Emit(Encoding.UTF8.GetBytes(_json.Serialize(new { Command = "module_dataflow." + command, Payload = payload })));
            public Task<PlayServRuntimeDataResponse> SendDataAsync(PlayServRuntimeDataRequest request, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                HttpRequests.Add(request);
                if (request.RelativePath != "catalog/purchased-items")
                    return Task.FromResult(new PlayServRuntimeDataResponse(404, "{}", null, null));
                return Task.FromResult(new PlayServRuntimeDataResponse(200, HttpBody ?? _json.Serialize(new { player_id = PlayerId, items = Items(Count) }), null, null));
            }
            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> SignInAnonAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerRefreshResponseDto> RefreshAsync(string token, string refresh, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> LoginExternalAsync(string token, PlayerExternalLoginRequestDto request, string access = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task SignOutAsync(string token, string refresh, CancellationToken ct = default) => throw new NotSupportedException();
            public void Dispose() { Adapter.Dispose(); _transport.Dispose(); }
        }

        private sealed class Observer<T> : IObserver<T>
        {
            private readonly Action<T> _next;
            public Observer(Action<T> next) => _next = next;
            public void OnNext(T value) => _next(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
        private sealed class Incoming : IObservable<byte[]>
        {
            private readonly List<IObserver<byte[]>> _observers = new List<IObserver<byte[]>>();
            public IDisposable Subscribe(IObserver<byte[]> observer) { _observers.Add(observer); return new DisposeAction(() => _observers.Remove(observer)); }
            public void Emit(byte[] bytes) { foreach (var observer in _observers.ToArray()) observer.OnNext(bytes); }
        }
        private sealed class DisposeAction : IDisposable
        {
            private Action _action;
            public DisposeAction(Action action) => _action = action;
            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
        private sealed class SilentLogger : ILogger
        {
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }
}
