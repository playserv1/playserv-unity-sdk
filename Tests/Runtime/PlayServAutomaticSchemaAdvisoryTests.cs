using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using Playserv.Data;
using Playserv.Schema;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServAuthSessionTests
    {
        [PlayServSchema] private sealed class AutoAdvisoryEntity { }
        [PlayServSchema(Kind = PlayServSchemaKind.Part)] private sealed class AdvisoryPart { }
        [PlayServSchema] private struct AdvisoryAutoPart { }
        [PlayServSchema(Kind = PlayServSchemaKind.Entity)] private struct AdvisoryEntityStruct { }
        [PlayServSchema] private enum AdvisoryEnum { Value }
        [PlayServSchema] private sealed class AdvisoryGeneric<T> { }

        [Test]
        public void AutomaticSchemaAdvisoryDiscoveryUsesEntityKindsAndSurvivesPartialLoads()
        {
            var assembly = new PartialContractAssembly();
            Assert.That(PlayServSchemaContractDiscovery.Discover(new[] { assembly, assembly }),
                Is.EquivalentTo(new[] { typeof(AutoAdvisoryEntity), typeof(AdvisoryEntityStruct) }));
            Assert.That(PlayServSchemaContractDiscovery.Discover(new[] { GetType().Assembly }), Is.Empty,
                "Test assemblies must not contribute player contracts.");
        }

        private sealed class PartialContractAssembly : Assembly
        {
            public override bool IsDynamic => false;
            public override AssemblyName[] GetReferencedAssemblies() => Array.Empty<AssemblyName>();
            public override Type[] GetTypes() => throw new ReflectionTypeLoadException(new[]
            {
                typeof(AutoAdvisoryEntity), typeof(AdvisoryPart), typeof(AdvisoryAutoPart),
                typeof(AdvisoryEntityStruct), typeof(AdvisoryEnum), typeof(AdvisoryGeneric<>), null
            }, new Exception[] { new TypeLoadException("An unrelated optional dependency is missing.") });
        }

        [Test]
        public void AutomaticSchemaAdvisoryDiscoversMixedRuntimeAssemblyInEditor()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("MixedRuntimeAdvisoryContract") != null);
            if (assembly == null) Assert.Ignore("Mixed runtime contract is supplied by the SDK CI project.");
            Assert.That(assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("UnityEditor")), Is.True);
            Assert.That(PlayServSchemaContractDiscovery.Discover().Any(t => t.Name == "MixedRuntimeAdvisoryContract"), Is.True);
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryWorkerLoginRunsDiscoveryAndDiagnosticsOnUnityThread()
        {
            using var scope = new AdvisoryScope();
            var expectedThread = Thread.CurrentThread.ManagedThreadId;
            var discoveryThread = 0;
            var diagnosticThread = 0;
            PlayServAuthenticationObservers.Create = () => scope.Observer = new PlayServAutomaticSchemaAdvisory(
                () => { discoveryThread = Thread.CurrentThread.ManagedThreadId; return new[] { typeof(AutoAdvisoryEntity) }; },
                _ => diagnosticThread = Thread.CurrentThread.ManagedThreadId);
            var http = new FakeHttpClient();
            http.DataResponses.Enqueue(AdvisoryCatalog(true));
            var settings = AdvisorySettings();
            settings.EnableAutomaticSchemaAdvisory = false;
            using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            settings.EnableAutomaticSchemaAdvisory = true;
            var login = Task.Run(() => coordinator.LoginExternalAsync(Proof(), PlayServExternalLoginMode.PreserveCurrentPlayer));
            while (!login.IsCompleted) yield return null;
            Assert.That(login.Result.IsSuccess, Is.True);
            while (!scope.Observer.Completion.IsCompleted) yield return null;
            Assert.That(discoveryThread, Is.EqualTo(expectedThread));
            Assert.That(http.LastDataThread, Is.EqualTo(expectedThread));
            Assert.That(diagnosticThread, Is.EqualTo(expectedThread));
        }

        private sealed class DelayedAdvisoryTokenProvider : Playserv.Runtime.Abstractions.IPlayServRuntimeTokenProvider
        {
            internal readonly TaskCompletionSource<string> Token = new TaskCompletionSource<string>();
            internal bool Started;
            public Task<string> GetTokenAsync(CancellationToken ct = default) { Started = true; return Token.Task; }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryInvalidationDuringTokenAcquisitionSendsNoRequest()
        {
            foreach (var changeScope in new[] { false, true })
            {
                using var scope = new AdvisoryScope();
                var provider = new DelayedAdvisoryTokenProvider();
                var http = new FakeHttpClient();
                http.DataResponses.Enqueue(AdvisoryCatalog(true));
                var settings = AdvisorySettings();
                settings.RuntimeTokenProvider = provider;
                settings.PlayerId = "external-player";
                using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                    () => settings, () => PlayServState.Online, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
                coordinator.HandleConnected(settings);
                while (!provider.Started) yield return null;
                if (changeScope) settings.BackendServerAddress = "https://another-project.test";
                else coordinator.InvalidateAdvisory();
                provider.Token.SetResult("late-access");
                while (!scope.Observer.Completion.IsCompleted) yield return null;
                for (var i = 0; i < 3; i++) yield return null;
                Assert.That(http.DataRequests, Is.Empty, "Obsolete credentials must never be sent to the captured endpoint.");
                Assert.That(scope.Warnings, Is.Empty);
            }
        }

        private sealed class AdvisoryScope : IDisposable
        {
            private readonly Func<IPlayServAuthenticationObserver> _previous = PlayServAuthenticationObservers.Create;
            internal readonly List<string> Warnings = new List<string>();
            internal PlayServAutomaticSchemaAdvisory Observer;
            internal int Discoveries;
            internal AdvisoryScope()
            {
                PlayServAuthenticationObservers.Create = () => Observer = new PlayServAutomaticSchemaAdvisory(
                    () => { Discoveries++; return new[] { typeof(AutoAdvisoryEntity) }; }, Warnings.Add);
            }
            public void Dispose() { Observer?.Dispose(); PlayServAuthenticationObservers.Create = _previous; }
        }

        private static PlayServSettings AdvisorySettings()
        {
            var settings = Settings();
            settings.EnableAutomaticSchemaAdvisory = true;
            settings.BackendServerAddress = "https://advisory.test/" + Guid.NewGuid().ToString("N");
            return settings;
        }

        private static PlayServRuntimeDataResponse AdvisoryCatalog(bool singleton = false) => new PlayServRuntimeDataResponse(200,
            "{\"data\":[{\"entity_id\":\"ent_auto\",\"name\":\"AutoAdvisoryEntity\",\"singleton\":" + (singleton ? "true" : "false") + "}]}", null, null);

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryMatchingCatalogIsSilentAndLoginDoesNotRepeatIt()
        {
            using var scope = new AdvisoryScope();
            var http = new FakeHttpClient();
            http.DataResponses.Enqueue(AdvisoryCatalog());
            var settings = AdvisorySettings();
            using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            while (!scope.Observer.Completion.IsCompleted) yield return null;
            Assert.That(scope.Warnings, Is.Empty);
            var login = coordinator.LoginExternalAsync(Proof(), PlayServExternalLoginMode.PreserveCurrentPlayer);
            while (!login.IsCompleted) yield return null;
            Assert.That(login.Result.IsSuccess, Is.True);
            yield return null;
            Assert.That(scope.Discoveries, Is.EqualTo(1));
            Assert.That(http.DataRequests.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryExplicitLoginStartsCheckWithoutConnect()
        {
            using var scope = new AdvisoryScope();
            var http = new FakeHttpClient();
            http.DataResponses.Enqueue(AdvisoryCatalog(true));
            var settings = AdvisorySettings();
            using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
            var login = coordinator.LoginExternalAsync(Proof(), PlayServExternalLoginMode.PreserveCurrentPlayer);
            while (!login.IsCompleted) yield return null;
            Assert.That(login.Result.IsSuccess, Is.True);
            Assert.That(scope.Observer, Is.Not.Null);
            while (!scope.Observer.Completion.IsCompleted) yield return null;
            Assert.That(scope.Warnings, Has.Count.EqualTo(1));
            StringAssert.Contains("singleton_mismatch", scope.Warnings[0]);
            StringAssert.Contains("AutoAdvisoryEntity", scope.Warnings[0]);
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryDiscardsLateIdentityAndConfigurationResults()
        {
            foreach (var changeIdentity in new[] { true, false })
            {
                using var scope = new AdvisoryScope();
                var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
                var http = new FakeHttpClient { DataTask = pending.Task };
                var settings = AdvisorySettings();
                using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                    () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                while (http.DataRequests.Count == 0) yield return null;
                if (changeIdentity) settings.PlayerId = "another-player";
                else settings.BackendServerAddress = "https://changed.test";
                pending.SetResult(AdvisoryCatalog(true));
                while (!scope.Observer.Completion.IsCompleted) yield return null;
                Assert.That(scope.Warnings, Is.Empty);
            }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryCatalogFailuresAreQualifiedAndNeverThrow()
        {
            foreach (var response in new[] { "hidden", "permission", "network", "ambiguous" })
            {
                using var scope = new AdvisoryScope();
                var http = new FakeHttpClient();
                if (response == "permission") http.DataExceptions.Enqueue(new PlayServDataException("secret body", 403, "forbidden"));
                else if (response == "network") http.DataExceptions.Enqueue(new InvalidOperationException("secret credential"));
                else http.DataResponses.Enqueue(new PlayServRuntimeDataResponse(200, response == "hidden" ? "{\"data\":[]}" :
                    "{\"data\":[{\"entity_id\":\"ent_a\",\"name\":\"AUTOADVISORYENTITY\"},{\"entity_id\":\"ent_b\",\"name\":\"autoadvisoryentity\"}]}", null, null));
                var settings = AdvisorySettings();
                using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                    () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                while (!scope.Observer.Completion.IsCompleted) yield return null;
                Assert.That(scope.Observer.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(scope.Warnings, Has.Count.EqualTo(1));
                StringAssert.DoesNotContain("secret", scope.Warnings[0]);
                if (response == "hidden") StringAssert.Contains("hidden by read permissions", scope.Warnings[0]);
                Assert.That(http.DataRequests.All(request => request.Method == "GET"), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryIdentityReplacementCancelsPendingCatalog()
        {
            using var scope = new AdvisoryScope();
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var http = new FakeHttpClient { DataTask = pending.Task };
            var settings = AdvisorySettings();
            using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
            try
            {
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                while (http.DataRequests.Count == 0) yield return null;
                var login = coordinator.LoginExternalAsync(Proof(), PlayServExternalLoginMode.PreserveCurrentPlayer);
                while (!login.IsCompleted) yield return null;
                Assert.That(login.Result.IsSuccess, Is.True);
                Assert.That(http.LastDataCancellation.IsCancellationRequested, Is.True);
                Assert.That(scope.Discoveries, Is.EqualTo(1));
            }
            finally { pending.TrySetResult(AdvisoryCatalog(true)); }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryRestoredAndUnmanagedSessionsAreEligible()
        {
            foreach (var unmanaged in new[] { false, true })
            {
                using var scope = new AdvisoryScope();
                var http = new FakeHttpClient();
                http.DataResponses.Enqueue(AdvisoryCatalog());
                var settings = AdvisorySettings();
                var store = new FakeSessionStore { Session = new Playserv.Runtime.Abstractions.PlayServPlayerSessionData(
                    "restored-player", "refresh-restored", PlayServSessionKind.Registered, null) };
                if (unmanaged) { settings.PlayerAccessToken = "unmanaged-access"; settings.PlayerId = "unmanaged-player"; }
                using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, store,
                    () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
                coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                if (unmanaged) Assert.That(scope.Observer, Is.Null, "Unmanaged credentials need a successful handshake first.");
                coordinator.HandleConnected(settings);
                while (!scope.Observer.Completion.IsCompleted) yield return null;
                Assert.That(http.AnonymousSignInCount, Is.Zero);
                Assert.That(http.DataRequests.Count, Is.EqualTo(1));
                Assert.That(http.DataRequests[0].BearerToken, Is.EqualTo(unmanaged ? "unmanaged-access" : "access-refreshed"));
                Assert.That(scope.Warnings, Is.Empty);
            }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryNoEntitiesDoesNotReadCatalog()
        {
            var http = new FakeHttpClient();
            using var observer = new PlayServAutomaticSchemaAdvisory(() => Array.Empty<Type>(), _ => Assert.Fail("No warning expected."));
            observer.OnAuthenticated(AdvisorySettings(), () => http, () => true);
            while (!observer.Completion.IsCompleted) yield return null;
            Assert.That(observer.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(http.DataRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryTimeoutAndShutdownCancelTheRequest()
        {
            foreach (var shutdown in new[] { false, true })
            {
                using var scope = new AdvisoryScope();
                var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
                var http = new FakeHttpClient { DataTask = pending.Task };
                var settings = AdvisorySettings();
                settings.TimeoutSeconds = 1;
                using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                    () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
                try
                {
                    coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
                    while (http.DataRequests.Count == 0) yield return null;
                    if (shutdown)
                    {
                        PlayServRuntimeShutdownState.MarkShuttingDown();
                        Assert.That(http.LastDataCancellation.IsCancellationRequested, Is.True, "Shutdown must cancel in-flight advisory I/O.");
                    }
                    while (!scope.Observer.Completion.IsCompleted) yield return null;
                    Assert.That(http.LastDataCancellation.IsCancellationRequested, Is.True);
                    Assert.That(scope.Warnings.Count, Is.EqualTo(shutdown ? 0 : 1));
                    if (!shutdown) StringAssert.Contains("timed out", scope.Warnings[0]);
                }
                finally
                {
                    PlayServRuntimeShutdownState.Reset();
                    pending.TrySetResult(AdvisoryCatalog(true));
                }
            }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryDoesNotDelayAuthenticationOrRepeatOnReconnect()
        {
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            var http = new FakeHttpClient { DataTask = pending.Task };
            var settings = Settings();
            settings.BackendServerAddress = "https://advisory.test/" + Guid.NewGuid().ToString("N");
            settings.EnableAutomaticSchemaAdvisory = true;
            using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
            try
            {
                var auth = coordinator.PrepareSettingsForConnectAsync(settings);
                for (var frame = 0; frame < 10 && !auth.IsCompleted; frame++) yield return null;
                Assert.That(auth.IsCompletedSuccessfully, Is.True, "Catalog latency must not delay authentication.");
                for (var frame = 0; frame < 10 && http.DataRequests.Count == 0; frame++) yield return null;
                Assert.That(http.DataRequests.Count, Is.EqualTo(1), "Opt-in authentication should start the advisory.");
                Assert.That(pending.Task.IsCompleted, Is.False);
                coordinator.HandleConnected(settings);
                coordinator.HandleConnected(settings);
                yield return null;
                Assert.That(http.DataRequests.Count, Is.EqualTo(1));
                Assert.That(http.DataRequests[0].RelativePath, Is.EqualTo("data/tables"));
                Assert.That(http.DataRequests[0].Method, Is.EqualTo("GET"));
                Assert.That(http.DataRequests[0].BearerToken, Is.EqualTo("access-anonymous-1"));
            }
            finally { pending.TrySetResult(new PlayServRuntimeDataResponse(200, "{\"data\":[]}", null, null)); }
        }

        [UnityTest]
        public IEnumerator AutomaticSchemaAdvisoryDisabledMakesNoCatalogRequest()
        {
            using var scope = new AdvisoryScope();
            var http = new FakeHttpClient();
            var settings = Settings();
            using var coordinator = new PlayServPlayerAuthCoordinator(_ => http, new FakeSessionStore(),
                () => settings, () => PlayServState.Offline, (_, __) => Task.FromResult(true), () => { }, () => Task.FromResult(true));
            coordinator.PrepareSettingsForConnectAsync(settings).GetAwaiter().GetResult();
            coordinator.HandleConnected(settings);
            yield return null;
            yield return null;
            Assert.That(http.DataRequests, Is.Empty);
            Assert.That(scope.Observer, Is.Null);
            Assert.That(scope.Discoveries, Is.Zero);
        }
    }
}
