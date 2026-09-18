using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Playserv.Http.Interfaces;
using Playserv.Runtime.Abstractions;
using Playserv.Wrapper;
using Playserv.Proxy.Common;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServBrowserSessionTests
    {
        private static IEnumerator Run(Func<Task> check)
        { var task = check(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        [UnityTest]
        public IEnumerator Switching_player_requires_opt_in_and_success_reconnects_and_persists() => Run(async () =>
        {
            foreach (var allow in new[] { false, true })
            {
                var http = ReadyHttp(); var store = new Store();
                var settings = new PlayServSettings { ClientToken = "pk_test", BackendServerAddress = "https://platform.example" };
                var connects = 0;
                using var coordinator = Coordinator(http, store, () => settings, () => { connects++; return Task.FromResult(true); });
                await coordinator.PrepareSettingsForConnectAsync(settings);
                var result = await coordinator.LoginBrowserAsync("google", new PlayServBrowserLoginOptions { AllowPlayerSwitch = allow });
                Assert.That(result.IsSuccess, Is.EqualTo(allow));
                Assert.That(coordinator.CurrentSession.PlayerId, Is.EqualTo(allow ? "player-2" : "player-1"));
                Assert.That(store.Value.PlayerId, Is.EqualTo(allow ? "player-2" : "player-1"));
                Assert.That(connects, Is.EqualTo(allow ? 1 : 0));
                if (!allow) { Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.BrowserPlayerSwitchRequired)); Assert.That(http.Revoked, Does.Contain("refresh-new")); }
            }
        });
        [UnityTest]
        public IEnumerator Popup_is_reserved_before_start_and_duplicate_click_does_not_start_second_flow() => Run(async () =>
        {
            var http = new PlayServBrowserOAuthTests.Http(); var store = new Store();
            var settings = new PlayServSettings { ClientToken = "pk_test", BackendServerAddress = "https://platform.example" };
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var opened = false;
            http.Send = request => { Assert.That(opened, Is.True); return pending.Task; };
            using var coordinator = Coordinator(http, store, () => settings);
            coordinator.BrowserFactory = () => { opened = true; return new PlayServBrowserOAuthTests.Browser(); };
            using var cancel = new CancellationTokenSource();
            var first = coordinator.LoginBrowserAsync("google", null, cancel.Token);
            Assert.That(opened, Is.True);
            var second = await coordinator.LoginBrowserAsync("google");
            Assert.That(second.Error.Code, Is.EqualTo(PlayServAuthErrorCode.BrowserFlowInProgress));
            Assert.That(http.Requests.Count, Is.EqualTo(1));
            cancel.Cancel();
            try { await first; Assert.Fail("Cancellation expected"); } catch (OperationCanceledException) { }
            pending.SetResult(PlayServBrowserOAuthTests.Response(200, "{}"));
        });
        [UnityTest]
        public IEnumerator Changed_configuration_discards_late_claim_and_keeps_old_player() => Run(async () =>
        {
            var http = ReadyHttp(); var store = new Store();
            var settings = new PlayServSettings { ClientToken = "pk_test", BackendServerAddress = "https://platform.example" };
            var claim = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Send = request => request.RelativePath.EndsWith(":start")
                ? Task.FromResult(http.Replies.Dequeue()) : claim.Task;
            using var coordinator = Coordinator(http, store, () => settings);
            await coordinator.PrepareSettingsForConnectAsync(settings);
            var flow = coordinator.LoginBrowserAsync("google", new PlayServBrowserLoginOptions { AllowPlayerSwitch = true });
            Assert.That(flow.IsCompleted, Is.False);
            settings = new PlayServSettings { ClientToken = "pk_other", BackendServerAddress = "https://platform.example" };
            claim.SetResult(PlayServBrowserOAuthTests.Response(200, PlayServBrowserOAuthTests.BundleJson));
            try { await flow; } catch (OperationCanceledException) { }
            Assert.That(store.Value.PlayerId, Is.EqualTo("player-1"));
            Assert.That(http.Revoked, Does.Contain("refresh-new"));
        });
        [UnityTest]
        public IEnumerator Blocked_popup_never_sends_start() => Run(async () =>
        {
            var http = ReadyHttp(); var settings = new PlayServSettings { ClientToken = "pk_test", BackendServerAddress = "https://platform.example" };
            using var coordinator = Coordinator(http, new Store(), () => settings);
            coordinator.BrowserFactory = () => throw new PlayServBrowserAuthException(PlayServAuthErrorCode.BrowserPopupBlocked, "Popup blocked");
            var result = await coordinator.LoginBrowserAsync("google");
            Assert.That(result.Error.Code, Is.EqualTo(PlayServAuthErrorCode.BrowserPopupBlocked));
            Assert.That(http.Requests, Is.Empty);
        });
        [UnityTest]
        public IEnumerator Cancelling_persistence_restores_old_session_and_automatic_refresh() => Run(async () =>
        {
            var http = ReadyHttp(); var store = new Store();
            var settings = new PlayServSettings { ClientToken = "pk_test", BackendServerAddress = "https://platform.example" };
            using var coordinator = Coordinator(http, store, () => settings);
            await coordinator.PrepareSettingsForConnectAsync(settings);
            coordinator.HandleConnected(settings);
            var writing = new TaskCompletionSource<bool>();
            store.Save = async value => { if (value.PlayerId == "player-2") await writing.Task; };
            using var cancel = new CancellationTokenSource();
            var flow = coordinator.LoginBrowserAsync("google", new PlayServBrowserLoginOptions { AllowPlayerSwitch = true }, cancel.Token);
            Assert.That(flow.IsCompleted, Is.False);
            cancel.Cancel(); writing.SetResult(true);
            try { await flow; Assert.Fail("Cancellation expected"); } catch (OperationCanceledException) { }
            Assert.That(coordinator.CurrentSession.PlayerId, Is.EqualTo("player-1"));
            Assert.That(store.Value.PlayerId, Is.EqualTo("player-1"));
            var field = typeof(PlayServPlayerAuthCoordinator).GetField("_refreshLoopCancellation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var loop = (CancellationTokenSource)field.GetValue(coordinator);
            Assert.That(loop, Is.Not.Null, "A preserved online session must retain automatic refresh.");
            Assert.That(loop.IsCancellationRequested, Is.False);
        });
        private static PlayServBrowserOAuthTests.Http ReadyHttp()
        {
            var http = new PlayServBrowserOAuthTests.Http();
            http.Replies.Enqueue(PlayServBrowserOAuthTests.Response(200, "{\"authorize_url\":\"https://provider.example/login\",\"state\":\"s\"}"));
            http.Replies.Enqueue(PlayServBrowserOAuthTests.Response(200, PlayServBrowserOAuthTests.BundleJson));
            return http;
        }
        private static PlayServPlayerAuthCoordinator Coordinator(PlayServBrowserOAuthTests.Http http, Store store, Func<PlayServSettings> settings, Func<Task<bool>> connect = null)
        {
            var coordinator = new PlayServPlayerAuthCoordinator(_ => http, store, settings, () => PlayServState.Online, (_, ct) => Task.FromResult(true), () => {}, connect ?? (() => Task.FromResult(true)));
            coordinator.BrowserFactory = () => new PlayServBrowserOAuthTests.Browser();
            return coordinator;
        }
        private sealed class Store : IPlayServPlayerSessionStore
        {
            public PlayServPlayerSessionData Value;
            public Func<PlayServPlayerSessionData,Task> Save;
            public Task<PlayServPlayerSessionData> LoadAsync(string key, CancellationToken ct = default) => Task.FromResult(Value);
            public async Task SaveAsync(string key, PlayServPlayerSessionData value, CancellationToken ct = default) { if (Save != null) await Save(value); Value = value; }
            public Task ClearAsync(string key, CancellationToken ct = default) { Value = null; return Task.CompletedTask; }
        }
    }
}
