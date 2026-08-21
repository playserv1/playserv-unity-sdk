using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Data;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServUnifiedErrorAndSubscriptionTests
    {
        [Test]
        public void Unified_error_classifies_http_transport_and_legacy_subscription_codes()
        {
            var http = PlayServError.FromHttp(
                429,
                "too_many_requests",
                "Slow down.",
                false,
                false,
                "{\"code\":\"too_many_requests\"}");
            Assert.That(http.Code, Is.EqualTo(PlayServErrorCode.RateLimited));
            Assert.That(http.HttpStatus, Is.EqualTo(429));
            Assert.That(http.Retryable, Is.True);
            Assert.That(http.SourceCode, Is.EqualTo("too_many_requests"));

            var transport = new TransportError(TransportErrorCode.ConnectionLimitReached, "Busy.");
            Assert.That(transport.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Transport));
            Assert.That(transport.UnifiedError.TransportCode, Is.EqualTo(2001));

            var terminated = new Playserv.DataSubscription.Exceptions.SubscriptionTerminatedException(42);
            Assert.That(terminated.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.SubscriptionTerminated));
            Assert.That(terminated.UnifiedError.TransportCode, Is.EqualTo(49001));
        }

        [Test]
        public void Unified_error_redacts_credentials_and_respects_retryability_policy()
        {
            const string jwt = "eyJheader.payload.signature";
            var error = new PlayServError(
                PlayServErrorCode.ServerError,
                "test",
                "failed",
                rawDetails: $"{{\"authorization\":\"Bearer secret-token\",\"provider_token\":\"provider-secret\",\"trace\":\"{jwt}\"}}; refresh_token=opaque-refresh");

            Assert.That(error.RawDetails, Does.Not.Contain("secret-token"));
            Assert.That(error.RawDetails, Does.Not.Contain("provider-secret"));
            Assert.That(error.RawDetails, Does.Not.Contain("opaque-refresh"));
            Assert.That(error.RawDetails, Does.Not.Contain(jwt));
            Assert.That(error.RawDetails, Does.Contain("[REDACTED]"));

            var retryable = new PlayServDataException(
                "Try later.",
                418,
                "temporary_backend_condition",
                extensions: new Dictionary<string, object> { ["retryable"] = true });
            Assert.That(retryable.UnifiedError.Retryable, Is.True);

            var terminal = PlayServError.FromHttp(
                401,
                "invalid_token",
                "Unauthorized.",
                false,
                false,
                retryable: true);
            Assert.That(terminal.Retryable, Is.False);
        }

        [Test]
        public void Identical_transport_subscriptions_share_open_snapshot_and_close_last_lease()
        {
            var bus = new LifecycleCommandBus();
            bus.InitialCollectionData = new Dictionary<string, object>
            {
                ["Inventory"] = new List<object>
                {
                    new Dictionary<string, object> { ["Code"] = "sword" }
                }
            };
            using var adapter = CreateAdapter(bus);

            var first = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                new Dictionary<string, object> { ["b"] = 2, ["a"] = 1 },
                CancellationToken.None).GetAwaiter().GetResult();
            var second = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 },
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(bus.OpenCount, Is.EqualTo(1));
            Assert.That(first.Items, Has.Count.EqualTo(1));
            Assert.That(second.Items, Has.Count.EqualTo(1));
            Assert.That(second.Items[0].Code, Is.EqualTo("sword"));

            first.Dispose();
            Assert.That(bus.CloseCount, Is.EqualTo(0));
            var result = second.CloseAsync().GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(bus.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public void Collection_refresh_updates_all_shared_leases_and_uses_current_server_id()
        {
            var bus = new LifecycleCommandBus
            {
                InitialCollectionData = CollectionData("sword")
            };
            using var adapter = CreateAdapter(bus);
            var first = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            var second = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            var firstChanges = 0;
            var secondChanges = 0;
            first.Changed += _ => firstChanges++;
            second.Changed += _ => secondChanges++;
            bus.RefreshCollectionData = CollectionData("axe");

            first.RefreshAsync().GetAwaiter().GetResult();

            Assert.That(bus.RefreshCount, Is.EqualTo(1));
            Assert.That(bus.LastRefresh.SubscriptionId, Is.EqualTo(bus.LastSubscriptionId));
            Assert.That(first.Items[0].Code, Is.EqualTo("axe"));
            Assert.That(second.Items[0].Code, Is.EqualTo("axe"));
            Assert.That(firstChanges, Is.EqualTo(1));
            Assert.That(secondChanges, Is.EqualTo(1));
        }

        [Test]
        public void Collection_refresh_cancellation_keeps_handle_active_and_late_update_applies()
        {
            var bus = new LifecycleCommandBus
            {
                InitialCollectionData = CollectionData("sword"),
                RefreshCollectionData = CollectionData("shield"),
                HoldRefreshResponses = true
            };
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            using var cancellation = new CancellationTokenSource();

            var refresh = handle.RefreshAsync(cancellation.Token);
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => refresh.GetAwaiter().GetResult());
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(bus.CloseCount, Is.Zero);
            bus.CompletePendingRefresh();
            Assert.That(handle.Items[0].Code, Is.EqualTo("shield"));
        }

        [Test]
        public void Collection_refresh_rejects_inactive_handles_before_transport_io()
        {
            var bus = new LifecycleCommandBus { InitialCollectionData = CollectionData("sword") };
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            using var preCanceled = new CancellationTokenSource();
            preCanceled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                handle.RefreshAsync(preCanceled.Token).GetAwaiter().GetResult());
            Assert.That(bus.RefreshCount, Is.Zero);

            bus.State = PlayServState.Offline;

            var offline = Assert.Throws<Playserv.DataSubscription.Exceptions.DataSubscriptionException>(
                () => handle.RefreshAsync().GetAwaiter().GetResult());
            Assert.That(offline.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Network));
            Assert.That(offline.UnifiedError.Retryable, Is.True);
            Assert.That(bus.RefreshCount, Is.Zero);

            handle.Dispose();
            Assert.Throws<InvalidOperationException>(() =>
                handle.RefreshAsync().GetAwaiter().GetResult());
            Assert.That(bus.RefreshCount, Is.Zero);
        }

        [Test]
        public void Collection_refresh_rejects_terminated_handle_before_transport_io()
        {
            var bus = new LifecycleCommandBus { InitialCollectionData = CollectionData("sword") };
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            bus.Emit(new DataSubscriptionUpdate
            {
                DataSubscriptionId = bus.LastSubscriptionId,
                UpdateType = "Terminated",
                IsCollection = true,
                ErrorCode = 49001,
                ErrorMessage = "terminated"
            });

            Assert.Throws<InvalidOperationException>(() =>
                handle.RefreshAsync().GetAwaiter().GetResult());
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(bus.RefreshCount, Is.Zero);
        }

        [Test]
        public void Collection_refresh_after_reconnect_uses_remapped_server_id()
        {
            var bus = new LifecycleCommandBus { InitialCollectionData = CollectionData("sword") };
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            bus.NextSubscriptionId = 77;
            adapter.OnConnected();
            bus.RefreshCollectionData = CollectionData("bow");

            handle.RefreshAsync().GetAwaiter().GetResult();

            Assert.That(bus.LastRefresh.SubscriptionId, Is.EqualTo(77));
            Assert.That(handle.Items[0].Code, Is.EqualTo("bow"));
        }

        [Test]
        public void All_public_realtime_handle_interfaces_are_refreshable()
        {
            Assert.That(
                typeof(IPlayServRefreshableSubscription).IsAssignableFrom(typeof(ISharedCollection<SubscriptionItem>)),
                Is.True);
            Assert.That(
                typeof(IPlayServRefreshableSubscription).IsAssignableFrom(typeof(ISharedEntity<SubscriptionItem>)),
                Is.True);
            Assert.That(
                typeof(IPlayServRefreshableSubscription).IsAssignableFrom(
                    typeof(IPlayServRecordSubscription<SubscriptionItem>)),
                Is.True);
        }

        [Test]
        public void Close_not_found_is_idempotent_success_and_local_handle_stays_closed()
        {
            var bus = new LifecycleCommandBus { CloseErrorCode = 41001 };
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            var result = handle.CloseAsync().GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.WasAlreadyClosed, Is.True);
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Closed));
        }

        [Test]
        public void Offline_close_returns_retryable_failure_but_closes_local_handle()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            bus.State = PlayServState.Offline;

            var result = handle.CloseAsync().GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PlayServErrorCode.Network));
            Assert.That(result.Error.Retryable, Is.True);
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            Assert.That(bus.CloseCount, Is.EqualTo(0));
        }

        [Test]
        public void Pre_canceled_close_does_not_release_handle()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                handle.CloseAsync(cts.Token).GetAwaiter().GetResult());
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            Assert.That(bus.CloseCount, Is.EqualTo(0));
            handle.Dispose();
        }

        [Test]
        public void Handle_disposed_while_offline_is_not_replayed()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            bus.State = PlayServState.Offline;
            handle.Dispose();
            bus.State = PlayServState.Reconnecting;

            adapter.OnConnected();

            Assert.That(bus.OpenCount, Is.EqualTo(1));
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Closed));
        }

        [Test]
        public void Reconnect_replays_logical_subscription_and_releases_restored_reference_once()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            bus.NextSubscriptionId = 77;
            bus.Emit(new DataSubscriptionUpdate
            {
                RequestId = 0,
                DataSubscriptionId = 77,
                UpdateType = "Overwrite",
                IsCollection = true,
                EntityType = "Inventory",
                Data = new Dictionary<string, object> { ["Inventory"] = new List<object>() }
            });

            adapter.OnConnected();

            Assert.That(bus.OpenCount, Is.EqualTo(2));
            Assert.That(bus.ClosedIds.FindAll(id => id == 77), Has.Count.EqualTo(1));
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
            handle.Dispose();
            Assert.That(bus.ClosedIds.FindAll(id => id == 77), Has.Count.EqualTo(2));
        }

        [Test]
        public void Terminal_reconnect_rejection_terminates_and_stops_future_replay()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            var failures = 0;
            var terminated = 0;
            handle.Failure += _ => failures++;
            handle.Terminated += () => terminated++;
            bus.FailNextOpen = true;
            bus.NextOpenErrorCode = 31001;
            bus.NextOpenErrorMessage = "access denied";

            adapter.OnConnected();

            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(handle.TerminalError.Code, Is.EqualTo(PlayServErrorCode.Forbidden));
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(terminated, Is.EqualTo(1));
            var openCount = bus.OpenCount;
            adapter.OnConnected();
            Assert.That(bus.OpenCount, Is.EqualTo(openCount));
        }

        [Test]
        public void Retryable_reconnect_failure_remains_registered_for_next_reconnect()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            PlayServError failure = null;
            handle.Failure += error => failure = error;
            bus.FailNextOpen = true;
            bus.NextOpenErrorCode = 0;
            bus.NextOpenErrorMessage = "Timed out waiting for reconnect response.";

            adapter.OnConnected();

            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Reconnecting));
            Assert.That(failure?.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(failure?.Retryable, Is.True);

            bus.NextSubscriptionId = 91;
            adapter.OnConnected();
            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Active));
        }

        [UnityTest]
        public IEnumerator Dispose_during_replay_closes_stale_server_id_and_does_not_rebind()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            bus.HoldOpenResponses = true;
            bus.NextSubscriptionId = 88;

            adapter.OnConnected();
            handle.Dispose();
            bus.CompletePendingOpen();
            yield return null;

            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Closed));
            Assert.That(bus.ClosedIds, Does.Contain(88));
            var openCount = bus.OpenCount;
            adapter.OnConnected();
            Assert.That(bus.OpenCount, Is.EqualTo(openCount));
        }

        [Test]
        public void Server_termination_completes_all_shared_handles_once()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var first = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            var second = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            var terminated = 0;
            var failures = 0;
            first.Terminated += () => terminated++;
            second.Terminated += () => terminated++;
            first.Failure += error =>
            {
                if (error.Code == PlayServErrorCode.SubscriptionTerminated)
                    failures++;
            };
            second.Failure += error =>
            {
                if (error.Code == PlayServErrorCode.SubscriptionTerminated)
                    failures++;
            };

            bus.Emit(new DataSubscriptionUpdate
            {
                DataSubscriptionId = bus.LastSubscriptionId,
                UpdateType = "Terminated",
                IsCollection = true,
                ErrorCode = 49001,
                ErrorMessage = "record deleted"
            });
            bus.Emit(new DataSubscriptionUpdate
            {
                DataSubscriptionId = bus.LastSubscriptionId,
                UpdateType = "Terminated",
                ErrorCode = 49001
            });

            Assert.That(terminated, Is.EqualTo(2));
            Assert.That(failures, Is.EqualTo(2));
            Assert.That(first.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(second.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
        }

        [Test]
        public void Backend_terminated_frame_ends_handle_even_for_nonlegacy_error_code()
        {
            var bus = new LifecycleCommandBus();
            using var adapter = CreateAdapter(bus);
            var handle = adapter.SelectTypedCollectionAsync<SubscriptionItem>(
                "Inventory { Code }",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            var terminated = 0;
            handle.Terminated += () => terminated++;

            bus.Emit(new DataSubscriptionUpdate
            {
                DataSubscriptionId = bus.LastSubscriptionId,
                UpdateType = "Terminated",
                IsCollection = true,
                ErrorCode = 39002,
                Message = "subscription became too broad"
            });

            Assert.That(handle.State, Is.EqualTo(PlayServSubscriptionState.Terminated));
            Assert.That(handle.TerminalError.Code, Is.EqualTo(PlayServErrorCode.Validation));
            Assert.That(terminated, Is.EqualTo(1));
        }

        private static PlayServDataSubscriptionAdapter CreateAdapter(LifecycleCommandBus bus) =>
            new PlayServDataSubscriptionAdapter(bus, new SilentLogger(), new NewtonsoftJsonCodec());

        private static object CollectionData(string code) =>
            new Dictionary<string, object>
            {
                ["Inventory"] = new List<object>
                {
                    new Dictionary<string, object> { ["Code"] = code }
                }
            };

        [Serializable]
        private sealed class SubscriptionItem
        {
            public string Code;
        }

        private sealed class LifecycleCommandBus : IPlayServCommandBus
        {
            private readonly Dictionary<Type, List<Delegate>> _typed = new Dictionary<Type, List<Delegate>>();
            public PlayServState State { get; set; } = PlayServState.Online;
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }
            public long NextSubscriptionId { get; set; } = 10;
            public long LastSubscriptionId { get; private set; }
            public int CloseErrorCode { get; set; }
            public object InitialCollectionData { get; set; }
            public List<long> ClosedIds { get; } = new List<long>();
            public bool HoldOpenResponses { get; set; }
            public bool FailNextOpen { get; set; }
            public int NextOpenErrorCode { get; set; }
            public string NextOpenErrorMessage { get; set; }
            private DataSubscriptionRequest _pendingOpen;
            private DataSubscriptionRefreshRequest _pendingRefresh;
            public int RefreshCount { get; private set; }
            public DataSubscriptionRefreshRequest LastRefresh { get; private set; }
            public object RefreshCollectionData { get; set; }
            public bool HoldRefreshResponses { get; set; }

            public IDisposable On<T>(Action<T> onNext)
            {
                var type = typeof(T);
                if (!_typed.TryGetValue(type, out var listeners))
                {
                    listeners = new List<Delegate>();
                    _typed[type] = listeners;
                }
                listeners.Add(onNext);
                return new CallbackDisposable(() => listeners.Remove(onNext));
            }

            public IDisposable OnCommand(string commandName, Action<object> onNext) =>
                CallbackDisposable.Empty;

            public Task SendAsync<T>(T command, string moduleName = null)
            {
                if (command is DataSubscriptionRequest open)
                {
                    OpenCount++;
                    if (HoldOpenResponses)
                    {
                        _pendingOpen = open;
                        return Task.CompletedTask;
                    }
                    if (FailNextOpen)
                    {
                        FailNextOpen = false;
                        Emit(new DataSubscriptionResponse
                        {
                            RequestId = open.RequestId,
                            Error = new DataSubscriptionError
                            {
                                ErrorCode = NextOpenErrorCode,
                                Message = NextOpenErrorMessage
                            }
                        });
                        return Task.CompletedTask;
                    }
                    LastSubscriptionId = NextSubscriptionId;
                    Emit(new DataSubscriptionResponse
                    {
                        RequestId = open.RequestId,
                        Result = new DataSubscriptionResult { SubscriptionId = LastSubscriptionId }
                    });
                    if (InitialCollectionData != null)
                    {
                        Emit(new DataSubscriptionUpdate
                        {
                            DataSubscriptionId = LastSubscriptionId,
                            UpdateType = "Overwrite",
                            IsCollection = true,
                            Data = InitialCollectionData
                        });
                    }
                }
                else if (command is DataSubscriptionCloseRequest close)
                {
                    CloseCount++;
                    ClosedIds.Add(close.SubscriptionId);
                    Emit(new DataSubscriptionCloseResponse
                    {
                        RequestId = close.RequestId,
                        Result = CloseErrorCode == 0
                            ? new DataSubscriptionCloseResult { Success = true }
                            : null,
                        Error = CloseErrorCode == 0
                            ? null
                            : new DataSubscriptionError
                            {
                                ErrorCode = CloseErrorCode,
                                Message = "not found"
                            }
                    });
                }
                else if (command is DataSubscriptionRefreshRequest refresh)
                {
                    RefreshCount++;
                    LastRefresh = refresh;
                    if (HoldRefreshResponses)
                    {
                        _pendingRefresh = refresh;
                        return Task.CompletedTask;
                    }
                    EmitRefresh(refresh);
                }
                return Task.CompletedTask;
            }

            public void CompletePendingRefresh()
            {
                var refresh = _pendingRefresh;
                _pendingRefresh = null;
                HoldRefreshResponses = false;
                if (refresh == null)
                    throw new InvalidOperationException("No subscription refresh is pending.");
                EmitRefresh(refresh);
            }

            private void EmitRefresh(DataSubscriptionRefreshRequest refresh)
            {
                Emit(new DataSubscriptionUpdate
                {
                    RequestId = refresh.RequestId,
                    DataSubscriptionId = refresh.SubscriptionId,
                    UpdateType = "Overwrite",
                    IsCollection = true,
                    Data = RefreshCollectionData ?? InitialCollectionData
                });
            }

            public void CompletePendingOpen()
            {
                var open = _pendingOpen;
                _pendingOpen = null;
                HoldOpenResponses = false;
                if (open == null)
                    throw new InvalidOperationException("No subscription open is pending.");
                LastSubscriptionId = NextSubscriptionId;
                Emit(new DataSubscriptionResponse
                {
                    RequestId = open.RequestId,
                    Result = new DataSubscriptionResult { SubscriptionId = LastSubscriptionId }
                });
            }

            public void Emit<T>(T value)
            {
                if (!_typed.TryGetValue(typeof(T), out var listeners))
                    return;
                foreach (var listener in listeners.ToArray())
                    ((Action<T>)listener)(value);
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _dispose;
            public static readonly CallbackDisposable Empty = new CallbackDisposable(null);
            public CallbackDisposable(Action dispose) => _dispose = dispose;
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }

        private sealed class SilentLogger : ILogger
        {
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }
}
