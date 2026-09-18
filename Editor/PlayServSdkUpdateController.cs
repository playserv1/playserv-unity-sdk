using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServPackageRelease
    {
        public string Name;
        public string Version;
        public string RegistryUrl;
        public string[] CompatibleVersions = Array.Empty<string>();
        public string[] DependencyNames = Array.Empty<string>();
    }

    internal interface IPlayServPackageClient
    {
        Task<PlayServPackageSnapshot[]> ListAsync();
        Task<PlayServPackageRelease> SearchAsync(string identifier);
        Task<PlayServPackageRelease> ReadGitReleaseAsync(string name, string version);
        Task UpdateAsync(string[] references);
    }

    internal interface IPlayServSdkUpdateStore
    {
        string Cache { get; set; }
        string Pending { get; set; }
    }

    internal sealed class PlayServPackageOperationGate
    {
        public static readonly PlayServPackageOperationGate Shared = new PlayServPackageOperationGate();
        private object _owner;
        public bool IsBusy => _owner != null;
        public bool TryAcquire(object owner)
        {
            if (IsBusy) return false;
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            return true;
        }
        public void Release(object owner) { if (ReferenceEquals(_owner, owner)) _owner = null; }
    }

    internal enum PlayServSdkUpdateState { Idle, Checking, Current, Available, Preparing, Updating, Verifying, Updated, Error, Unsupported }

    internal sealed class PlayServSdkUpdateController
    {
        private readonly IPlayServPackageClient _client;
        private readonly IPlayServSdkUpdateStore _store;
        private readonly PlayServPackageOperationGate _gate;
        private readonly Func<bool> _canOperate;
        private readonly Func<DateTime> _utcNow;
        private readonly string _editorVersion;
        private bool _busy;

        public PlayServSdkUpdateController(IPlayServPackageClient client, IPlayServSdkUpdateStore store,
            PlayServPackageOperationGate gate, Func<bool> canOperate, Func<DateTime> utcNow, string editorVersion)
        {
            _client = client;
            _store = store;
            _gate = gate;
            _canOperate = canOperate;
            _utcNow = utcNow;
            _editorVersion = editorVersion;
        }
        public PlayServSdkUpdateState State { get; private set; }
        public string Message { get; private set; }
        public string AvailableVersion { get; private set; }
        public string InstalledVersion { get; private set; }
        public bool IsBusy => _busy;
        public event Action Changed;

        public async Task CheckAsync(bool force)
        {
            if (!Begin()) return;
            string identity = null;
            try
            {
                if (!string.IsNullOrEmpty(_store.Pending)) { await VerifyPendingAsync(); return; }
                SetState(PlayServSdkUpdateState.Checking, "Checking for updates…");
                var installed = await _client.ListAsync();
                var core = installed.SingleOrDefault(p => p.Name == PlayServSdkUpdatePlan.CoreId);
                InstalledVersion = core?.Version;
                if (core == null || core.Source != PackageSource.Registry || !core.IsDirect)
                {
                    AvailableVersion = null;
                    SetState(PlayServSdkUpdateState.Unsupported, "Use Package Manager for Git, local, embedded or indirect SDK installations.");
                    return;
                }
                identity = _editorVersion + ":" + PlayServSdkUpdatePlan.Fingerprint(installed);
                var cached = ReadCache();
                if (!force && cached != null && cached.Identity == identity &&
                    _utcNow().Ticks >= cached.CheckedUtcTicks &&
                    _utcNow().Ticks - cached.CheckedUtcTicks < TimeSpan.TicksPerDay)
                {
                    ApplyCache(cached);
                    return;
                }
                RequireIdle();
                var release = await _client.SearchAsync(PlayServSdkUpdatePlan.CoreId);
                if (release == null || release.Name != core.Name || !PlayServSdkUpdatePlan.SameRegistry(release.RegistryUrl, core.RegistryUrl))
                    throw new UpdateFailure("The configured registry did not return PlayServ SDK. Check Package Manager and try again.");
                AvailableVersion = PlayServSdkUpdatePlan.SelectLatest(core.Version, release.CompatibleVersions);
                _store.Cache = JsonUtility.ToJson(new CheckCache
                {
                    Identity = identity, CheckedUtcTicks = _utcNow().Ticks, AvailableVersion = AvailableVersion
                });
                ShowAvailable();
            }
            catch (EditorBusyException ex)
            {
                AvailableVersion = null;
                SetState(PlayServSdkUpdateState.Error, ex.Message);
            }
            catch (Exception)
            {
                AvailableVersion = null;
                SetState(PlayServSdkUpdateState.Error, "Could not check for updates. Check your registry connection in Package Manager, then retry.");
                if (identity != null)
                {
                    try { _store.Cache = JsonUtility.ToJson(new CheckCache { Identity = identity, CheckedUtcTicks = _utcNow().Ticks, Failed = true }); }
                    catch (Exception) { /* The failure remains visible even when the local cache cannot be saved. */ }
                }
            }
            finally { End(); }
        }

        public async Task UpdateAsync(string target, Func<PlayServSdkUpdatePlan, bool> confirm)
        {
            if (!Begin()) return;
            try
            {
                if (!string.IsNullOrEmpty(_store.Pending)) { await VerifyPendingAsync(); return; }
                SetState(PlayServSdkUpdateState.Preparing, "Checking installed packages and release availability…");
                var installed = await _client.ListAsync();
                PlayServSdkUpdatePlan plan;
                try { plan = PlayServSdkUpdatePlan.Create(installed, target); }
                catch (InvalidOperationException ex) { throw new UpdateFailure(ex.Message); }
                foreach (var package in plan.Packages)
                {
                    RequireIdle();
                    var release = package.Source == PackageSource.Git
                        ? await _client.ReadGitReleaseAsync(package.Name, target)
                        : await _client.SearchAsync(package.Name + "@" + target);
                    if (release == null || release.Name != package.Name || release.Version != target ||
                        !release.CompatibleVersions.Contains(target) ||
                        (package.Source == PackageSource.Registry && !PlayServSdkUpdatePlan.SameRegistry(release.RegistryUrl, package.RegistryUrl)))
                        throw new UpdateFailure(package.Name + " has no verified compatible target release at its current source. Nothing was updated.");
                    if (release.DependencyNames.Any(name => name.StartsWith("com.playserv.", StringComparison.Ordinal) &&
                        !installed.Any(p => p.Name == name)))
                        throw new UpdateFailure(package.Name + " requires another PlayServ package. Review dependencies in Package Manager first.");
                }
                RequireIdle();
                if (confirm == null || !confirm(plan)) { ShowAvailable(); return; }
                RequireIdle();
                var refreshed = await _client.ListAsync();
                if (PlayServSdkUpdatePlan.Fingerprint(refreshed) != plan.InventoryFingerprint)
                    throw new UpdateFailure("Installed packages changed. Check for updates again to review the new package set.");
                RequireIdle();
                // Persist before submitting: self-update can unload this assembly before the request completes.
                _store.Pending = JsonUtility.ToJson(plan);
                SetState(PlayServSdkUpdateState.Updating, "Updating SDK packages. Unity may recompile and reload scripts…");
                await _client.UpdateAsync(plan.References);
                await VerifyPendingAsync();
            }
            catch (UpdateFailure ex) { SetState(PlayServSdkUpdateState.Error, ex.Message); }
            catch (EditorBusyException ex) { SetState(PlayServSdkUpdateState.Error, ex.Message); }
            catch (Exception)
            {
                SetState(PlayServSdkUpdateState.Error, "Package update could not be confirmed. Open Package Manager for details, then check again. No automatic retry was made.");
            }
            finally { End(); }
        }

        public async Task RecoverAsync()
        {
            if (!Begin()) return;
            try
            {
                if (!string.IsNullOrEmpty(_store.Pending)) await VerifyPendingAsync();
            }
            catch (Exception)
            {
                SetState(PlayServSdkUpdateState.Error, "Could not verify the previous update. Open Package Manager or check again. The update was not repeated.");
            }
            finally { End(); }
        }

        private async Task VerifyPendingAsync()
        {
            PlayServSdkUpdatePlan plan;
            try { plan = JsonUtility.FromJson<PlayServSdkUpdatePlan>(_store.Pending); }
            catch (Exception)
            {
                _store.Pending = null;
                throw new UpdateFailure("The previous update record is unreadable. Review installed packages in Package Manager.");
            }
            SetState(PlayServSdkUpdateState.Verifying, "Verifying installed package versions…");
            var installed = await _client.ListAsync();
            InstalledVersion = installed.SingleOrDefault(p => p.Name == PlayServSdkUpdatePlan.CoreId)?.Version;
            var matches = plan != null && plan.MatchesInstalled(installed);
            _store.Pending = null;
            _store.Cache = null;
            AvailableVersion = null;
            SetState(matches ? PlayServSdkUpdateState.Updated : PlayServSdkUpdateState.Error,
                matches ? "SDK and installed PlayServ packages updated successfully." :
                "Update incomplete or interrupted. Installed SDK: " + (InstalledVersion ?? "not found") +
                ". Review packages in Package Manager. No automatic retry was made.");
        }

        private bool Begin()
        {
            if (_busy || !_canOperate() || !_gate.TryAcquire(this)) return false;
            _busy = true;
            return true;
        }

        private void End() { _busy = false; _gate.Release(this); Notify(); }
        private void RequireIdle()
        {
            if (!_canOperate()) throw new EditorBusyException();
        }

        private CheckCache ReadCache()
        {
            try { return string.IsNullOrEmpty(_store.Cache) ? null : JsonUtility.FromJson<CheckCache>(_store.Cache); }
            catch (Exception) { return null; }
        }

        private void ApplyCache(CheckCache cache)
        {
            AvailableVersion = cache.AvailableVersion;
            if (cache.Failed)
                SetState(PlayServSdkUpdateState.Error, "The last update check failed. Use Check for updates to try again.");
            else ShowAvailable();
        }

        private void ShowAvailable() => SetState(string.IsNullOrEmpty(AvailableVersion)
                ? PlayServSdkUpdateState.Current : PlayServSdkUpdateState.Available,
            string.IsNullOrEmpty(AvailableVersion) ? "No newer stable compatible release found." : "New version available: " + AvailableVersion);

        private void SetState(PlayServSdkUpdateState state, string message) { State = state; Message = message; Notify(); }
        private void Notify()
        {
            foreach (var callback in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { ((Action)callback)(); }
                catch (Exception) { /* UI observers must not interrupt package resolution or release of the operation gate. */ }
            }
        }

        [Serializable]
        private sealed class CheckCache
        {
            public string Identity;
            public long CheckedUtcTicks;
            public string AvailableVersion;
            public bool Failed;
        }

        private sealed class UpdateFailure : Exception
        {
            public UpdateFailure(string message) : base(message) { }
        }

        private sealed class EditorBusyException : Exception
        {
            public EditorBusyException() : base("Wait for Play Mode, build, compilation and import to finish, then try again.") { }
        }
    }
}
