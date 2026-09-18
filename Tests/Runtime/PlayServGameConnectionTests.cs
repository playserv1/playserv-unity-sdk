using System;
using System.Collections;
using UnityEngine.TestTools;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Matchmaking;
using Playserv.Proxy.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServGameConnectionTests
    {
        private static IEnumerator RunGameConnectionCheck(Func<Task> action)
        {
            var task = action();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Direct_connection_sends_existing_CSharp_handshake_once_without_claiming_admission()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Direct_connection_sends_existing_CSharp_handshake_once_without_claiming_admissionAsync();
            });
        }

        private async Task Direct_connection_sends_existing_CSharp_handshake_once_without_claiming_admissionAsync()
        {
            var type = typeof(PlayServMatchmaking).Assembly.GetType("Playserv.Matchmaking.PlayServGameConnection");
            Assert.That(type, Is.Not.Null, "The connector must implement the existing C# game server handshake.");
            var transport = new FakeTransport();
            var settings = new PlayServSettings { PlayerId = "player-1", PlayerAccessToken = "player-secret" };
            Func<TransportModuleContext, ITransportImplementation> factory = context => transport;
            var connection = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { null, (Func<PlayServSettings>)(() => settings), factory,
                    new NewtonsoftJsonCodec(), SynchronizationContext.Current }, null);
            using ((IDisposable)connection)
            {
                await (Task)type.GetMethod("ConnectAsync").Invoke(connection, new object[] { Reservation(), CancellationToken.None });
                Assert.That(transport.Sent, Has.Count.EqualTo(1));
                Assert.That(transport.Sent[0], Is.EqualTo("{\"playerId\":\"player-1\",\"token\":\"player-secret\",\"reservationToken\":\"reservation-secret\"}"));
                Assert.That(type.GetProperty("State").GetValue(connection).ToString(), Is.EqualTo("HandshakeSent"));
            }
        }

        private static PlayServMatchReservation Reservation() => new PlayServMatchReservation("room-code", "reservation-secret",
            DateTimeOffset.MinValue, new PlayServRoomConnect("game.example", 443, "wss", "wss://game.example/", null),
            null, null, 60, () => 0, 0);

        [UnityTest]
        public IEnumerator Connection_snapshots_options_and_receives_messages_before_handshake_completion()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Connection_snapshots_options_and_receives_messages_before_handshake_completionAsync();
            });
        }

        private async Task Connection_snapshots_options_and_receives_messages_before_handshake_completionAsync()
        {
            var socket = new FakeTransport();
            var options = new PlayServGameConnectionOptions { DisplayName = "Test player" };
            using var connection = Create(socket, options);
            options.DisplayName = "changed";
            var messages = new List<string>();
            connection.MessageReceived += messages.Add;
            socket.OnSend = () => socket.Receive("game-owned welcome");
            await connection.ConnectAsync(Reservation());
            Assert.That(socket.Sent[0], Does.Contain("\"displayName\":\"Test player\""));
            Assert.That(socket.Sent[0], Does.Not.Contain("changed"));
            Assert.That(messages, Is.EqualTo(new[] { "game-owned welcome" }));
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.HandshakeSent));
        }

        [UnityTest]
        public IEnumerator Invalid_endpoint_never_resolves_credentials_or_opens_socket()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("udp", "", false);
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("wss", "ws://game.example:443/", true);
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("wss", "wss://other.example/", false);
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("wss", "wss://game.example/?token=secret", false);
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("wss", "wss://user:secret@game.example/", false);
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("wss", "wss://game.example/#secret", false);
                await Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync("ws", "ws://game.example:443/", false);
            });
        }

        private async Task Invalid_endpoint_never_resolves_credentials_or_opens_socketAsync(string scheme, string url, bool allowInsecure)
        {
            var socket = new FakeTransport();
            var provider = new TokenProvider();
            using var connection = Create(socket, new PlayServGameConnectionOptions { AllowInsecureWebSocket = allowInsecure },
                new PlayServSettings { PlayerId = "player-1", RuntimeTokenProvider = provider });
            var reservation = WithConnect(new PlayServRoomConnect("game.example", 443, scheme, url, null));
            await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(reservation));
            Assert.That(provider.Calls, Is.Zero);
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Host_port_endpoint_is_explicitly_resolved()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Host_port_endpoint_is_explicitly_resolvedAsync("ws", 7777, true, "ws://game.example:7777/");
                await Host_port_endpoint_is_explicitly_resolvedAsync("wss", 443, false, "wss://game.example/");
            });
        }

        private async Task Host_port_endpoint_is_explicitly_resolvedAsync(string scheme, int port, bool allowInsecure, string expected)
        {
            var socket = new FakeTransport();
            string endpoint = null;
            using var connection = new PlayServGameConnection(new PlayServGameConnectionOptions { AllowInsecureWebSocket = allowInsecure },
                () => _settings, context => { endpoint = context.Endpoint; return socket; }, new NewtonsoftJsonCodec(), new InlineContext());
            await connection.ConnectAsync(WithConnect(new PlayServRoomConnect("game.example", port, scheme, null, null)));
            Assert.That(endpoint, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator Null_connect_and_precancellation_perform_no_io()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Null_connect_and_precancellation_perform_no_ioAsync();
            });
        }

        private async Task Null_connect_and_precancellation_perform_no_ioAsync()
        {
            var socket = new FakeTransport();
            using var connection = Create(socket);
            await Throws<OperationCanceledException>(() => connection.ConnectAsync(Reservation(), new CancellationToken(true)));
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.Created));
            await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(WithConnect(null)));
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Expiry_is_checked_again_after_opening_without_wall_clock_comparison()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Expiry_is_checked_again_after_opening_without_wall_clock_comparisonAsync();
            });
        }

        private async Task Expiry_is_checked_again_after_opening_without_wall_clock_comparisonAsync()
        {
            double clock = 0;
            var socket = new FakeTransport { OnConnect = () => clock = 2 };
            var r = new PlayServMatchReservation("room", "reservation-secret", DateTimeOffset.MaxValue,
                Reservation().Connect, null, null, 1, () => clock, 0);
            using var connection = Create(socket);
            var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(r));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("reservation_expired"));
            Assert.That(socket.Sent, Is.Empty);
            Assert.That(socket.Disposed, Is.True);
            using var unknown = Create(new FakeTransport());
            await unknown.ConnectAsync(new PlayServMatchReservation("room", "ticket", DateTimeOffset.MinValue,
                Reservation().Connect, null, null, null, null, 0));
            Assert.That(unknown.State, Is.EqualTo(PlayServGameConnectionState.HandshakeSent));
        }

        [UnityTest]
        public IEnumerator Changing_identity_while_credentials_are_pending_never_sends_the_ticket()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Changing_identity_while_credentials_are_pending_never_sends_the_ticketAsync();
            });
        }

        private async Task Changing_identity_while_credentials_are_pending_never_sends_the_ticketAsync()
        {
            var token = new TaskCompletionSource<string>();
            var provider = new TokenProvider { Pending = token.Task };
            var settings = new PlayServSettings { PlayerId = "player-1", RuntimeTokenProvider = provider };
            var socket = new FakeTransport();
            using var connection = Create(socket, settings: settings);
            var pending = connection.ConnectAsync(Reservation());
            settings.PlayerId = "player-2";
            token.SetResult("new-player-secret");
            try { await pending; Assert.Fail(); } catch (PlayServGameConnectionException e)
            { Assert.That(e.UnifiedError.SourceCode, Is.EqualTo("player_session_changed")); }
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Overall_timeout_bounds_dependencies_that_ignore_cancellation()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Overall_timeout_bounds_dependencies_that_ignore_cancellationAsync("auth");
                await Overall_timeout_bounds_dependencies_that_ignore_cancellationAsync("connect");
                await Overall_timeout_bounds_dependencies_that_ignore_cancellationAsync("send");
            });
        }

        private async Task Overall_timeout_bounds_dependencies_that_ignore_cancellationAsync(string phase)
        {
            var socket = new FakeTransport();
            var settings = new PlayServSettings { PlayerId = "player-1", PlayerAccessToken = "player-secret" };
            if (phase == "auth") settings.RuntimeTokenProvider = new TokenProvider { Pending = new TaskCompletionSource<string>().Task };
            if (phase == "connect") socket.ConnectTask = new TaskCompletionSource<bool>().Task;
            if (phase == "send") socket.SendTask = new TaskCompletionSource<bool>().Task;
            using var connection = Create(socket, new PlayServGameConnectionOptions { Timeout = TimeSpan.FromMilliseconds(50) }, settings);
            try { await connection.ConnectAsync(Reservation()); Assert.Fail(); } catch (PlayServGameConnectionException e)
            { Assert.That(e.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout)); }
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.Closed));
            Assert.That(socket.ConnectCalls, Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator Cancellation_closes_only_own_connection_and_late_completion_does_not_reopen_it()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Cancellation_closes_only_own_connection_and_late_completion_does_not_reopen_itAsync();
            });
        }

        private async Task Cancellation_closes_only_own_connection_and_late_completion_does_not_reopen_itAsync()
        {
            var opening = new TaskCompletionSource<bool>();
            var socket = new FakeTransport { ConnectTask = opening.Task };
            using var connection = Create(socket);
            using var cancellation = new CancellationTokenSource();
            var pending = connection.ConnectAsync(Reservation(), cancellation.Token);
            cancellation.Cancel();
            try { await pending; Assert.Fail(); } catch (OperationCanceledException) { }
            opening.SetResult(true);
            Assert.That(socket.Disposed, Is.True);
            Assert.That(socket.Sent, Is.Empty);
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.Closed));
        }

        [UnityTest]
        public IEnumerator Serial_sends_do_not_overlap_and_invalid_or_precancelled_send_keeps_connection_open()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Serial_sends_do_not_overlap_and_invalid_or_precancelled_send_keeps_connection_openAsync();
            });
        }

        private async Task Serial_sends_do_not_overlap_and_invalid_or_precancelled_send_keeps_connection_openAsync()
        {
            var socket = new FakeTransport();
            using var connection = Create(socket);
            await connection.ConnectAsync(Reservation());
            await Throws<OperationCanceledException>(() => connection.SendTextAsync("ignored", new CancellationToken(true)));
            await Throws<PlayServGameConnectionException>(() => connection.SendTextAsync(new string('x', 1024 * 1024 + 1)));
            var firstSend = new TaskCompletionSource<bool>();
            socket.SendTask = firstSend.Task;
            var first = connection.SendTextAsync("first");
            var second = connection.SendTextAsync("second");
            Assert.That(socket.Sent, Has.Count.EqualTo(2));
            firstSend.SetResult(true);
            await Task.WhenAll(first, second);
            Assert.That(socket.Sent[1], Is.EqualTo("first"));
            Assert.That(socket.Sent[2], Is.EqualTo("second"));
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.HandshakeSent));
        }

        [UnityTest]
        public IEnumerator Error_and_close_diagnostics_do_not_echo_peer_payloads_or_credentials()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Error_and_close_diagnostics_do_not_echo_peer_payloads_or_credentialsAsync();
            });
        }

        private async Task Error_and_close_diagnostics_do_not_echo_peer_payloads_or_credentialsAsync()
        {
            var socket = new FakeTransport();
            using var connection = Create(socket);
            PlayServError closed = null;
            connection.Closed += error => closed = error;
            await connection.ConnectAsync(Reservation());
            socket.RemoteClose(1008, "player-secret reservation-secret arbitrary-private-data");
            Assert.That(closed.TransportCode, Is.EqualTo(1008));
            Assert.That(closed.ToString(), Does.Not.Contain("secret"));
            Assert.That(closed.RawDetails, Is.Empty);
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.Closed));
            await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
            Assert.That(socket.ConnectCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Handshake_utf8_limit_is_enforced_before_socket_creation()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Handshake_utf8_limit_is_enforced_before_socket_creationAsync();
            });
        }

        private async Task Handshake_utf8_limit_is_enforced_before_socket_creationAsync()
        {
            var socket = new FakeTransport();
            using var connection = Create(socket, new PlayServGameConnectionOptions { DisplayName = new string('界', 1400) });
            var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("handshake_too_large"));
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Throwing_provider_cancellation_callback_cannot_prevent_cleanup()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Throwing_provider_cancellation_callback_cannot_prevent_cleanupAsync();
            });
        }

        private async Task Throwing_provider_cancellation_callback_cannot_prevent_cleanupAsync()
        {
            var provider = new TokenProvider { Pending = new TaskCompletionSource<string>().Task, ThrowOnCancellation = true };
            using var connection = Create(new FakeTransport(), settings: new PlayServSettings { PlayerId = "p", RuntimeTokenProvider = provider });
            int closed = 0;
            connection.Closed += error => closed++;
            var pending = connection.ConnectAsync(Reservation());
            Assert.DoesNotThrow(connection.Disconnect);
            try { await pending; Assert.Fail(); } catch (PlayServGameConnectionException) { }
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.Closed));
        }

        [UnityTest]
        public IEnumerator Throwing_provider_callback_on_timeout_does_not_escape_timer_thread()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Throwing_provider_callback_on_timeout_does_not_escape_timer_threadAsync();
            });
        }

        private async Task Throwing_provider_callback_on_timeout_does_not_escape_timer_threadAsync()
        {
            var provider = new TokenProvider { Pending = new TaskCompletionSource<string>().Task, ThrowOnCancellation = true };
            using var connection = Create(new FakeTransport(), new PlayServGameConnectionOptions { Timeout = TimeSpan.FromMilliseconds(50) },
                new PlayServSettings { PlayerId = "p", RuntimeTokenProvider = provider });
            var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout));
            Assert.That(connection.State, Is.EqualTo(PlayServGameConnectionState.Closed));
        }

        [UnityTest]
        public IEnumerator Anonymous_identity_is_not_inferred_from_an_unverified_custom_token()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Anonymous_identity_is_not_inferred_from_an_unverified_custom_tokenAsync();
            });
        }

        private async Task Anonymous_identity_is_not_inferred_from_an_unverified_custom_tokenAsync()
        {
            var provider = new TokenProvider();
            var socket = new FakeTransport();
            using var connection = Create(socket, settings: new PlayServSettings { RuntimeTokenProvider = provider });
            var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Unauthorized));
            Assert.That(provider.Calls, Is.Zero);
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Send_timeout_reports_timeout_to_caller_and_closed_callback()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Send_timeout_reports_timeout_to_caller_and_closed_callbackAsync();
            });
        }

        private async Task Send_timeout_reports_timeout_to_caller_and_closed_callbackAsync()
        {
            var socket = new FakeTransport();
            using var connection = Create(socket, new PlayServGameConnectionOptions { Timeout = TimeSpan.FromMilliseconds(50) });
            await connection.ConnectAsync(Reservation());
            PlayServError closed = null;
            connection.Closed += error => closed = error;
            socket.SendTask = new TaskCompletionSource<bool>().Task;
            try { await connection.SendTextAsync("game payload"); Assert.Fail(); }
            catch (PlayServGameConnectionException error) { Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Timeout)); }
            Assert.That(closed.Code, Is.EqualTo(PlayServErrorCode.Timeout));
        }

        [UnityTest]
        public IEnumerator Client_context_change_while_token_pending_blocks_handshake()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Client_context_change_while_token_pending_blocks_handshakeAsync();
            });
        }

        private async Task Client_context_change_while_token_pending_blocks_handshakeAsync()
        {
            var token = new TaskCompletionSource<string>();
            var settings = new PlayServSettings { PlayerId = "p", ClientToken = "pk_dev", RuntimeTokenProvider = new TokenProvider { Pending = token.Task } };
            var socket = new FakeTransport();
            using var connection = Create(socket, settings: settings);
            var pending = connection.ConnectAsync(Reservation());
            settings.ClientToken = "pk_prod";
            token.SetResult("player-secret");
            try { await pending; Assert.Fail(); } catch (PlayServGameConnectionException error)
            { Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("player_session_changed")); }
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Exact_handshake_utf8_byte_boundary()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Exact_handshake_utf8_byte_boundaryAsync(4096, true);
                await Exact_handshake_utf8_byte_boundaryAsync(4097, false);
            });
        }

        private async Task Exact_handshake_utf8_byte_boundaryAsync(int length, bool allowed)
        {
            var empty = new NewtonsoftJsonCodec().Serialize(new Dictionary<string, object>
            {
                ["playerId"] = "player-1", ["displayName"] = "", ["token"] = "player-secret", ["reservationToken"] = "reservation-secret"
            });
            var count = length - Encoding.UTF8.GetByteCount(empty);
            var name = new string('界', count / 3) + new string('x', count % 3);
            var socket = new FakeTransport();
            using var connection = Create(socket, new PlayServGameConnectionOptions { DisplayName = name });
            if (allowed)
            {
                await connection.ConnectAsync(Reservation());
                Assert.That(Encoding.UTF8.GetByteCount(socket.Sent[0]), Is.EqualTo(length));
            }
            else await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
            Assert.That(socket.ConnectCalls, Is.EqualTo(allowed ? 1 : 0));
        }

        [UnityTest]
        public IEnumerator Known_expired_ticket_never_resolves_credentials()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Known_expired_ticket_never_resolves_credentialsAsync();
            });
        }

        private async Task Known_expired_ticket_never_resolves_credentialsAsync()
        {
            var provider = new TokenProvider();
            var socket = new FakeTransport();
            using var connection = Create(socket, settings: new PlayServSettings { PlayerId = "p", RuntimeTokenProvider = provider });
            var expired = new PlayServMatchReservation("r", "ticket", DateTimeOffset.MaxValue, Reservation().Connect, null, null, 0, () => 0, 0);
            var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(expired));
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("reservation_expired"));
            Assert.That(provider.Calls, Is.Zero);
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Non_player_bearer_is_never_sent()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Non_player_bearer_is_never_sentAsync("");
                await Non_player_bearer_is_never_sentAsync("pk_public");
                await Non_player_bearer_is_never_sentAsync("sk_server");
            });
        }

        private async Task Non_player_bearer_is_never_sentAsync(string token)
        {
            var socket = new FakeTransport();
            using var connection = Create(socket, settings: new PlayServSettings { PlayerId = "p", PlayerAccessToken = token });
            var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
            Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Unauthorized));
            Assert.That(socket.ConnectCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Receive_and_close_callbacks_use_captured_context_and_do_not_deliver_stale_messages()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await Receive_and_close_callbacks_use_captured_context_and_do_not_deliver_stale_messagesAsync();
            });
        }

        private async Task Receive_and_close_callbacks_use_captured_context_and_do_not_deliver_stale_messagesAsync()
        {
            var context = new QueuedContext();
            var socket = new FakeTransport();
            using var connection = new PlayServGameConnection(null, () => _settings, _ => socket, new NewtonsoftJsonCodec(), context);
            int messages = 0, closes = 0;
            int callbackThread = 0;
            connection.MessageReceived += text => { messages++; callbackThread = Thread.CurrentThread.ManagedThreadId; };
            connection.Closed += error => closes++;
            await connection.ConnectAsync(Reservation());
            await Task.Run(() => socket.Receive("game reply"));
            Assert.That(messages, Is.Zero);
            context.Drain();
            Assert.That(messages, Is.EqualTo(1));
            Assert.That(callbackThread, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
            socket.Receive("late queued reply");
            connection.Disconnect();
            Assert.That(closes, Is.Zero);
            context.Drain();
            Assert.That(messages, Is.EqualTo(1));
            Assert.That(closes, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator One_MiB_text_is_allowed_and_oversized_response_closes_safely()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                await One_MiB_text_is_allowed_and_oversized_response_closes_safelyAsync();
            });
        }

        private async Task One_MiB_text_is_allowed_and_oversized_response_closes_safelyAsync()
        {
            var socket = new FakeTransport();
            using var connection = Create(socket);
            await connection.ConnectAsync(Reservation());
            await connection.SendTextAsync(new string('x', 1024 * 1024));
            PlayServError closed = null;
            connection.Closed += error => closed = error;
            socket.Receive(new string('x', 1024 * 1024 + 1));
            Assert.That(closed.SourceCode, Is.EqualTo("message_too_large"));
            Assert.That(socket.Disposed, Is.True);
        }

        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly Queue<Action> _work = new Queue<Action>();
            public override void Post(SendOrPostCallback d, object state) { lock (_work) _work.Enqueue(() => d(state)); }
            internal void Drain() { while (true) { Action next; lock (_work) { if (_work.Count == 0) return; next = _work.Dequeue(); } next(); } }
        }

        private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
        {
            try { await action(); }
            catch (T exception) { return exception; }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

        [UnityTest]
        public IEnumerator Receive_subscription_failure_detaches_remote_close_handler()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                var socket = new FakeTransport { FailSubscription = true };
                using var connection = Create(socket);
                var error = await Throws<PlayServGameConnectionException>(() => connection.ConnectAsync(Reservation()));
                Assert.That(error.UnifiedError.Code, Is.EqualTo(PlayServErrorCode.Transport));
                Assert.That(socket.Disposed, Is.True);
                Assert.That(socket.CloseHandlers, Is.Zero, "Failed setup must not retain the connector on its transport.");
            });
        }

        [UnityTest]
        public IEnumerator Ticket_expiring_during_credentials_never_creates_a_socket()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                double clock = 0;
                var token = new TaskCompletionSource<string>();
                var settings = new PlayServSettings { PlayerId = "p", RuntimeTokenProvider = new TokenProvider { Pending = token.Task } };
                int created = 0;
                using var connection = new PlayServGameConnection(null, () => settings,
                    _ => { created++; return new FakeTransport(); }, new NewtonsoftJsonCodec(), new InlineContext());
                var reservation = new PlayServMatchReservation("room", "ticket", DateTimeOffset.MaxValue,
                    Reservation().Connect, null, null, 1, () => clock, 0);
                var pending = connection.ConnectAsync(reservation);
                clock = 2;
                token.SetResult("player-secret");
                var error = await Throws<PlayServGameConnectionException>(() => pending);
                Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("reservation_expired"));
                Assert.That(created, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator Browser_wait_cancels_a_noncooperative_task_without_worker_threads()
        {
            yield return RunGameConnectionCheck(async () =>
            {
                var operation = new TaskCompletionSource<bool>();
                using var cancellation = new CancellationTokenSource();
                var waiting = PlayServGameConnection.AwaitWithoutWorkerThreads(operation.Task, cancellation.Token);
                Assert.That(waiting.IsCompleted, Is.False);
                cancellation.Cancel();
                await Throws<OperationCanceledException>(() => waiting);
                operation.SetException(new InvalidOperationException("late private operation error"));
                var completed = new TaskCompletionSource<bool>();
                var success = PlayServGameConnection.AwaitWithoutWorkerThreads(completed.Task, CancellationToken.None);
                completed.SetResult(true);
                await success;
            });
        }

        private readonly PlayServSettings _settings = new PlayServSettings { PlayerId = "player-1", PlayerAccessToken = "player-secret" };
        private static PlayServMatchReservation WithConnect(PlayServRoomConnect connect) => new PlayServMatchReservation("room", "ticket",
            DateTimeOffset.MinValue, connect, null, null, 60, () => 0, 0);
        private PlayServGameConnection Create(FakeTransport socket, PlayServGameConnectionOptions options = null, PlayServSettings settings = null) =>
            new PlayServGameConnection(options, () => settings ?? _settings, context => socket, new NewtonsoftJsonCodec(), new InlineContext());
        private sealed class InlineContext : SynchronizationContext { public override void Post(SendOrPostCallback d, object state) => d(state); }
        private sealed class TokenProvider : IPlayServRuntimeTokenProvider
        {
            internal int Calls;
            internal Task<string> Pending;
            internal bool ThrowOnCancellation;
            public Task<string> GetTokenAsync(CancellationToken ct = default)
            {
                Calls++;
                if (ThrowOnCancellation) ct.Register(() => throw new InvalidOperationException("private provider text"));
                return Pending ?? Task.FromResult("player-secret");
            }
        }

        private sealed class FakeTransport : ITransportImplementation, IObservable<byte[]>, IPlayServTransportCloseInfoSource
        {
            internal readonly List<string> Sent = new List<string>();
            internal int ConnectCalls;
            internal bool Disposed;
            internal bool FailSubscription;
            internal int CloseHandlers => Closed?.GetInvocationList().Length ?? 0;
            internal Action OnConnect, OnSend;
            internal Task<bool> ConnectTask;
            internal Task SendTask;
            private IObserver<byte[]> _observer;
            public event Action<PlayServTransportCloseInfo> Closed;
            public Task<bool> Connect() { ConnectCalls++; OnConnect?.Invoke(); return ConnectTask ?? Task.FromResult(true); }
            public Task Send(byte[] data) { Sent.Add(Encoding.UTF8.GetString(data)); OnSend?.Invoke(); return SendTask ?? Task.CompletedTask; }
            public void ResetConnection() { }
            public IObservable<byte[]> OnReceive() => this;
            public IDisposable Subscribe(IObserver<byte[]> observer)
            {
                if (FailSubscription) throw new InvalidOperationException("private failure text");
                _observer = observer;
                return new Subscription(() => _observer = null);
            }
            public void Dispose() { Disposed = true; }
            internal void Receive(string value) => _observer?.OnNext(Encoding.UTF8.GetBytes(value));
            internal void RemoteClose(int code, string reason) => Closed?.Invoke(new PlayServTransportCloseInfo(code, reason));
            private sealed class Subscription : IDisposable
            {
                private readonly Action _close;
                internal Subscription(Action close) => _close = close;
                public void Dispose() => _close();
            }
        }
        [Test]
        public void Direct_game_connection_can_be_created_before_subscribing_and_connecting()
        {
            var factory = typeof(PlayServMatchmaking).GetMethod("CreateGameConnection");
            Assert.That(factory, Is.Not.Null, "Matchmaking must expose the opt-in game connection factory.");
            var connection = factory.Invoke(null, new object[] { null });
            try
            {
                Assert.That(connection.GetType().GetProperty("State").GetValue(connection).ToString(), Is.EqualTo("Created"));
                Assert.That(connection.GetType().GetEvent("MessageReceived"), Is.Not.Null);
                Assert.That(connection.GetType().GetEvent("Closed"), Is.Not.Null);
            }
            finally { (connection as IDisposable)?.Dispose(); }
        }
    }
}
