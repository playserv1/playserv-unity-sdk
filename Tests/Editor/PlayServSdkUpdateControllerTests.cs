using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Editor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSdkUpdateControllerTests
    {
        private const string Core = "com.playserv.sdk";
        private const string Schema = "com.playserv.schema-tool";
        private FakeClient _client;
        private Store _store;
        private PlayServPackageOperationGate _gate;
        private DateTime _now;
        private bool _idle;
        private PlayServSdkUpdateController _controller;

        [SetUp]
        public void SetUp()
        {
            _client = new FakeClient();
            _store = new Store();
            _gate = new PlayServPackageOperationGate();
            _now = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
            _idle = true;
            _controller = Controller();
        }

        [UnityTest]
        public IEnumerator Check_UsesCompatibleVersionsWithoutInstallingAnything() => Run(async () =>
        {
            await _controller.CheckAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Available));
            Assert.That(_controller.AvailableVersion, Is.EqualTo("0.6.2"));
            Assert.That(_client.Searches, Is.EqualTo(new[] { Core }));
            Assert.That(_client.Updates, Is.Empty);
        });

        [UnityTest]
        public IEnumerator NoNewerCompatibleRelease_ShowsCurrent() => Run(async () =>
        {
            _client.Compatible = new[] { "0.6.0", "0.6.1", "0.7.0-preview.1" };
            await _controller.CheckAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Current));
            Assert.That(_controller.AvailableVersion, Is.Null.Or.Empty);
        });

        [UnityTest]
        public IEnumerator Cache_SurvivesReloadExpiresAt24HoursAndManualCheckBypassesIt() => Run(async () =>
        {
            await _controller.CheckAsync(false);
            _now = _now.AddHours(23);
            await Controller().CheckAsync(false);
            Assert.That(_client.Searches.Count, Is.EqualTo(1));
            _now = _now.AddHours(1);
            await Controller().CheckAsync(false);
            Assert.That(_client.Searches.Count, Is.EqualTo(2));
            await _controller.CheckAsync(true);
            Assert.That(_client.Searches.Count, Is.EqualTo(3));
        });

        [UnityTest]
        public IEnumerator ChangedInstalledVersionOrRegistry_InvalidatesCache() => Run(async () =>
        {
            await _controller.CheckAsync(false);
            _client.Installed[0].Version = "0.6.0";
            await Controller().CheckAsync(false);
            _client.Installed[0].RegistryUrl = "https://registry.example";
            await Controller().CheckAsync(false);
            Assert.That(_client.Searches.Count, Is.EqualTo(3));
        });

        [UnityTest]
        public IEnumerator Offline_IsAnErrorNotUpToDateAndDoesNotLeakException() => Run(async () =>
        {
            _client.SearchError = new Exception("https://secret@registry token=private");
            await _controller.CheckAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_controller.Message, Does.Not.Contain("secret").And.Not.Contain("private"));
            await Controller().CheckAsync(false);
            Assert.That(_client.Searches.Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator LocalCore_NoRegistryRequestOrSourceConversion() => Run(async () =>
        {
            _client.Installed[0].Source = PackageSource.Local;
            await _controller.CheckAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Unsupported));
            Assert.That(_client.Searches, Is.Empty);
            Assert.That(_client.Updates, Is.Empty);
        });

        [UnityTest]
        public IEnumerator BusyEditorOrOtherPackageOperation_NoRequests() => Run(async () =>
        {
            _idle = false;
            await _controller.CheckAsync(true);
            await _controller.UpdateAsync("0.6.2", _ => true);
            _idle = true;
            var owner = new object();
            Assert.That(_gate.TryAcquire(owner), Is.True);
            await _controller.CheckAsync(true);
            await _controller.UpdateAsync("0.6.2", _ => true);
            Assert.That(_client.ListCount, Is.Zero);
            _gate.Release(owner);
        });

        [UnityTest]
        public IEnumerator RepeatedClick_UsesOneInFlightSearch() => Run(async () =>
        {
            _client.SearchWait = new TaskCompletionSource<bool>();
            var first = _controller.CheckAsync(true);
            await _controller.CheckAsync(true);
            Assert.That(_controller.IsBusy, Is.True);
            Assert.That(_client.Searches.Count, Is.EqualTo(1));
            _client.SearchWait.SetResult(true);
            await first;
            Assert.That(_gate.IsBusy, Is.False);
        });

        [UnityTest]
        public IEnumerator Update_RefreshesInventoryConfirmsAndSubmitsOneBatchIncludingSchema() => Run(async () =>
        {
            _client.Installed.Add(Package(Schema));
            var confirmed = false;
            await _controller.UpdateAsync("0.6.2", plan =>
            {
                confirmed = true;
                Assert.That(plan.Describe(), Does.Contain(Schema).And.Contain("0.6.1 → 0.6.2"));
                Assert.That(_client.Updates, Is.Empty);
                return true;
            });
            Assert.That(confirmed, Is.True);
            Assert.That(_client.Updates.Count, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(new[] { Core + "@0.6.2", Schema + "@0.6.2" }, _client.Updates[0]);
            CollectionAssert.AreEquivalent(new[] { Core + "@0.6.2", Schema + "@0.6.2" }, _client.Searches);
            Assert.That(_client.ListCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Updated));
            Assert.That(_store.Pending, Is.Null.Or.Empty);
        });

        [UnityTest]
        public IEnumerator CancelConfirmation_DoesNotWriteJournalOrMutatePackages() => Run(async () =>
        {
            var confirmed = false;
            await _controller.UpdateAsync("0.6.2", _ => { confirmed = true; return false; });
            Assert.That(confirmed, Is.True);
            Assert.That(_client.Updates, Is.Empty);
            Assert.That(_store.Pending, Is.Null.Or.Empty);
            Assert.That(_gate.IsBusy, Is.False);
        });

        [UnityTest]
        public IEnumerator RegistryTargetMissing_BlocksWholeUpdateBeforeConfirmation() => Run(async () =>
        {
            _client.Installed.Add(Package(Schema));
            _client.MissingPackage = Schema;
            await _controller.UpdateAsync("0.6.2", _ => { Assert.Fail("Cannot confirm an unavailable release."); return true; });
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_client.Updates, Is.Empty);
        });

        [UnityTest]
        public IEnumerator IncomingUninstalledCompanion_IsNotSilentlyAddedAsDependency() => Run(async () =>
        {
            _client.Dependencies = new[] { Schema };
            await _controller.UpdateAsync("0.6.2", _ => true);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_client.Updates, Is.Empty);
        });

        [UnityTest]
        public IEnumerator InventoryChangedDuringConfirmation_RequiresNewReview() => Run(async () =>
        {
            await _controller.UpdateAsync("0.6.2", _ => { _client.Installed.Add(Package(Schema)); return true; });
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_client.Updates, Is.Empty);
        });

        [UnityTest]
        public IEnumerator UpdateFailure_ReportsErrorWithoutRetryOrRawPayload() => Run(async () =>
        {
            _client.UpdateError = new Exception("password=secret");
            await _controller.UpdateAsync("0.6.2", _ => true);
            Assert.That(_client.Updates.Count, Is.EqualTo(1));
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_controller.Message, Does.Not.Contain("secret"));
            Assert.That(_gate.IsBusy, Is.False);
        });

        [UnityTest]
        public IEnumerator UpmSuccessWithoutExpectedInstalledVersion_IsNotReportedAsSuccess() => Run(async () =>
        {
            _client.ApplyUpdate = false;
            await _controller.UpdateAsync("0.6.2", _ => true);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_client.Updates.Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator DomainReload_VerifiesCompletedUpdate() => Run(() => VerifyReload(true));

        [UnityTest]
        public IEnumerator DomainReload_ReportsInterruptedUpdateWithoutReplaying() => Run(() => VerifyReload(false));

        private async Task VerifyReload(bool completed)
        {
            _store.Pending = JsonUtility.ToJson(PlayServSdkUpdatePlan.Create(_client.Installed.ToArray(), "0.6.2"));
            if (completed) _client.Installed[0] = Package(Core, "0.6.2");
            await Controller().RecoverAsync();
            Assert.That(_store.Pending, Is.Null.Or.Empty);
            Assert.That(_client.ListCount, Is.EqualTo(1));
            Assert.That(_client.Updates, Is.Empty);
            var controller = Controller();
            _store.Pending = JsonUtility.ToJson(PlayServSdkUpdatePlan.Create(new[] { Package(Core) }, "0.6.2"));
            await controller.RecoverAsync();
            Assert.That(controller.State, Is.EqualTo(completed ? PlayServSdkUpdateState.Updated : PlayServSdkUpdateState.Error));
        }

        [UnityTest]
        public IEnumerator FailedPostUpdateInventoryRead_RetainsJournalForLaterVerification() => Run(async () =>
        {
            _client.FailVerification = true;
            await _controller.UpdateAsync("0.6.2", _ => true);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_store.Pending, Is.Not.Null.And.Not.Empty);
            _client.FailVerification = false;
            await Controller().RecoverAsync();
            Assert.That(_store.Pending, Is.Null.Or.Empty);
            Assert.That(_client.Updates.Count, Is.EqualTo(1));
        });

        private static IEnumerator Run(Func<Task> body)
        {
            var task = body();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator GitTargetWithNewCompanionDependency_BlocksBeforeConfirmation() => Run(async () =>
        {
            AddGitSchema();
            _client.GitDependencies = new[] { "com.playserv.analytics" };
            var confirmations = 0;
            await _controller.UpdateAsync("0.6.2", _ => { confirmations++; return true; });
            Assert.That(confirmations, Is.Zero);
            Assert.That(_client.Updates, Is.Empty);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
        });

        [UnityTest]
        public IEnumerator GitTargetIncompatibleWithEditor_BlocksBeforeConfirmation() => Run(async () =>
        {
            AddGitSchema();
            _client.GitCompatible = false;
            var confirmations = 0;
            await _controller.UpdateAsync("0.6.2", _ => { confirmations++; return true; });
            Assert.That(confirmations, Is.Zero);
            Assert.That(_client.Updates, Is.Empty);
        });

        private void AddGitSchema()
        {
            var package = Package(Schema);
            package.Source = PackageSource.Git;
            package.PackageId = Schema + "@https://github.com/playserv1/playserv-unity-sdk.git?path=/CompanionPackages~/" + Schema + "#0.6.1";
            _client.Installed.Add(package);
        }

        [UnityTest]
        public IEnumerator RecoveredPartialUpdate_RemainsVisibleAfterQueuedWindowChecks() => Run(async () =>
        {
            _client.Installed.Add(Package(Schema));
            _store.Pending = JsonUtility.ToJson(PlayServSdkUpdatePlan.Create(_client.Installed.ToArray(), "0.6.2"));
            _client.Installed[0] = Package(Core, "0.6.2");
            var schedule = new PlayServSdkUpdateSchedule(_controller);
            schedule.WindowOpened();
            await schedule.TickAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            schedule.PackagesChanged();
            await schedule.TickAsync(false);
            schedule.WindowClosed();
            schedule.WindowOpened();
            await schedule.TickAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_client.Searches, Is.Empty);
            await schedule.CheckAsync();
            Assert.That(_client.Searches.Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator BatchModeAndClosedWindow_DoNotAutomaticallyCheck() => Run(async () =>
        {
            var schedule = new PlayServSdkUpdateSchedule(_controller);
            await schedule.TickAsync(false);
            await schedule.TickAsync(false);
            schedule.WindowOpened();
            await schedule.TickAsync(true);
            Assert.That(_client.ListCount, Is.Zero);
            await schedule.TickAsync(false);
            Assert.That(_client.Searches.Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator CancelledConfirmation_DoesNotDisableFutureAutomaticChecks() => Run(async () =>
        {
            var schedule = new PlayServSdkUpdateSchedule(_controller);
            schedule.WindowOpened();
            await schedule.TickAsync(false);
            await schedule.TickAsync(false);
            await _controller.UpdateAsync("0.6.2", _ => false);
            var searches = _client.Searches.Count;
            _now = _now.AddHours(25);
            schedule.WindowClosed();
            schedule.WindowOpened();
            await schedule.TickAsync(false);
            Assert.That(_client.Searches.Count, Is.EqualTo(searches + 1));
            Assert.That(_client.Updates, Is.Empty);
        });

        [UnityTest]
        public IEnumerator ImportStartsDuringCheck_DoesNotCacheAnOfflineFailure() => Run(async () =>
        {
            _client.AfterList = () => _idle = false;
            await _controller.CheckAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Error));
            Assert.That(_controller.Message, Does.Contain("import"));
            Assert.That(_store.Cache, Is.Null.Or.Empty);
            Assert.That(_client.Searches, Is.Empty);
            _client.AfterList = null;
            _idle = true;
            await _controller.CheckAsync(false);
            Assert.That(_controller.State, Is.EqualTo(PlayServSdkUpdateState.Available));
        });

        private PlayServSdkUpdateController Controller() =>
            new PlayServSdkUpdateController(_client, _store, _gate, () => _idle, () => _now, "2021.3");

        internal static PlayServPackageSnapshot Package(string name, string version = "0.6.1") =>
            new PlayServPackageSnapshot { Name = name, Version = version, Source = PackageSource.Registry,
                IsDirect = true, PackageId = name + "@" + version, RegistryUrl = "https://package.openupm.com" };

        private sealed class Store : IPlayServSdkUpdateStore
        {
            public string Cache { get; set; }
            public string Pending { get; set; }
        }

        private sealed class FakeClient : IPlayServPackageClient
        {
            public readonly List<PlayServPackageSnapshot> Installed = new List<PlayServPackageSnapshot> { Package(Core) };
            public readonly List<string> Searches = new List<string>();
            public readonly List<string[]> Updates = new List<string[]>();
            public int ListCount;
            public string[] Compatible = { "0.6.1", "0.6.2", "0.7.0-preview.1" };
            public string[] Dependencies = Array.Empty<string>();
            public string MissingPackage;
            public Exception SearchError;
            public Exception UpdateError;
            public TaskCompletionSource<bool> SearchWait;
            public bool ApplyUpdate = true;
            public bool FailVerification;
            public Action AfterList;
            public bool GitCompatible = true;
            public string[] GitDependencies = Array.Empty<string>();

            public Task<PlayServPackageRelease> ReadGitReleaseAsync(string name, string version)
            {
                return Task.FromResult(new PlayServPackageRelease { Name = name, Version = version,
                    CompatibleVersions = GitCompatible ? new[] { version } : Array.Empty<string>(), DependencyNames = GitDependencies });
            }

            public Task<PlayServPackageSnapshot[]> ListAsync()
            {
                ListCount++;
                AfterList?.Invoke();
                if (FailVerification && Updates.Count > 0) throw new Exception("private registry response");
                return Task.FromResult(Installed.Select(p => JsonUtility.FromJson<PlayServPackageSnapshot>(JsonUtility.ToJson(p))).ToArray());
            }

            public async Task<PlayServPackageRelease> SearchAsync(string identifier)
            {
                Searches.Add(identifier);
                if (SearchWait != null) await SearchWait.Task;
                if (SearchError != null) throw SearchError;
                var name = identifier.Split('@')[0];
                if (name == MissingPackage) return null;
                return new PlayServPackageRelease { Name = name, Version = "0.6.2", CompatibleVersions = Compatible,
                    RegistryUrl = Installed.First(p => p.Name == name).RegistryUrl, DependencyNames = Dependencies };
            }

            public Task UpdateAsync(string[] references)
            {
                Updates.Add(references);
                if (UpdateError != null) throw UpdateError;
                if (ApplyUpdate)
                    foreach (var reference in references)
                    {
                        var parts = reference.Split('@');
                        var index = Installed.FindIndex(p => p.Name == parts[0]);
                        Installed[index] = Package(parts[0], parts[1]);
                    }
                return Task.CompletedTask;
            }
        }
    }
}
