using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Playserv.Editor
{
    /// <summary>Adapts async UPM requests to the editor update loop; never edits package manifests.</summary>
    internal sealed class PlayServUpmClient : IPlayServPackageClient
    {
        public async Task<PlayServPackageSnapshot[]> ListAsync()
        {
            var result = await Wait(Client.List(offlineMode: true, includeIndirectDependencies: true), r => r.Result);
            return result.Select(Snapshot).ToArray();
        }

        public async Task<PlayServPackageRelease> SearchAsync(string identifier)
        {
            var result = await Wait(Client.Search(identifier, offlineMode: false), r => r.Result);
            var name = identifier.Split('@')[0];
            var info = result.SingleOrDefault(p => p.name == name);
            return info == null ? null : new PlayServPackageRelease
            {
                Name = info.name, Version = info.version, RegistryUrl = info.registry?.url,
                CompatibleVersions = info.versions?.compatible ?? Array.Empty<string>(),
                DependencyNames = info.dependencies?.Select(d => d.name).ToArray() ?? Array.Empty<string>()
            };
        }

        public async Task UpdateAsync(string[] references)
        {
            await Wait(Client.AddAndRemove(references, Array.Empty<string>()), r => r.Result);
        }

        public async Task<PlayServPackageRelease> ReadGitReleaseAsync(string name, string version)
        {
            // Only planner-approved official tags are inspected, without credentials or arbitrary URLs.
            if (!PlayServSdkUpdatePlan.ManagedIds.Contains(name) || name == PlayServSdkUpdatePlan.CoreId ||
                PlayServSdkUpdatePlan.SelectLatest("0.0.0", new[] { version }) != version)
                throw new InvalidOperationException("Invalid official package release.");
            var url = "https://raw.githubusercontent.com/playserv1/playserv-unity-sdk/" + version +
                "/CompanionPackages~/" + name + "/package.json";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                var completion = new TaskCompletionSource<bool>();
                var operation = request.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);
                if (operation.isDone) completion.TrySetResult(true);
                await completion.Task;
                if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 262144)
                    throw new InvalidOperationException("Could not verify the official Git release. Check its tag in Package Manager.");
                return PlayServSdkGitRelease.Parse(request.downloadHandler.text, Application.unityVersion);
            }
        }

        internal static PlayServPackageSnapshot Snapshot(PackageInfo info) => new PlayServPackageSnapshot
        {
            Name = info.name, Version = info.version, Source = info.source, IsDirect = info.isDirectDependency,
            PackageId = info.packageId, RegistryUrl = info.registry?.url
        };

        private static Task<TResult> Wait<TRequest, TResult>(TRequest request, Func<TRequest, TResult> result)
            where TRequest : Request
        {
            var completion = new TaskCompletionSource<TResult>();
            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success)
                {
                    try { completion.TrySetResult(result(request)); }
                    catch (Exception) { completion.TrySetException(new InvalidOperationException("Package Manager returned an invalid result.")); }
                }
                else
                    completion.TrySetException(new InvalidOperationException("Package Manager operation failed (code " + request.Error?.errorCode + ")."));
            }
            EditorApplication.update += Poll;
            return completion.Task;
        }
    }

    internal sealed class PlayServSdkUpdateStore : IPlayServSdkUpdateStore
    {
        private readonly string _cacheKey;
        private readonly string _pendingPath;

        public PlayServSdkUpdateStore(string projectPath)
        {
            var root = Path.GetFullPath(projectPath);
            _cacheKey = "PlayServ.SdkUpdate.Check." + Hash128.Compute(root);
            _pendingPath = Path.Combine(root, "Library", "PlayServ", "sdk-update-pending.json");
        }

        public string Cache
        {
            get => EditorPrefs.GetString(_cacheKey, string.Empty);
            set
            {
                if (string.IsNullOrEmpty(value)) EditorPrefs.DeleteKey(_cacheKey);
                else EditorPrefs.SetString(_cacheKey, value);
            }
        }

        public string Pending
        {
            get => File.Exists(_pendingPath) ? File.ReadAllText(_pendingPath) : null;
            set
            {
                if (string.IsNullOrEmpty(value)) { if (File.Exists(_pendingPath)) File.Delete(_pendingPath); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(_pendingPath));
                var temporary = _pendingPath + ".tmp";
                try
                {
                    File.WriteAllText(temporary, value);
                    if (File.Exists(_pendingPath)) File.Replace(temporary, _pendingPath, null);
                    else File.Move(temporary, _pendingPath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }

    /// <summary>Editor-only update controls. Checks may be automatic; installation always requires confirmation.</summary>
    [InitializeOnLoad]
    internal static class PlayServSdkUpdates
    {
        private static readonly PlayServSdkUpdateStore Store = new PlayServSdkUpdateStore(Path.GetDirectoryName(Application.dataPath));
        internal static readonly PlayServSdkUpdateController Controller = new PlayServSdkUpdateController(
            new PlayServUpmClient(), Store, PlayServPackageOperationGate.Shared, () => CanOperate,
            () => DateTime.UtcNow, Application.unityVersion);
        private static readonly PlayServSdkUpdateSchedule Schedule = new PlayServSdkUpdateSchedule(Controller);
        private static bool _maintenanceQueued;

        static PlayServSdkUpdates()
        {
            EditorApplication.update += Tick;
            Events.registeredPackages += _ => Schedule.PackagesChanged();
            Controller.Changed += OnChanged;
        }

        public static bool CanOperate => !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer;
        public static bool CanStart => CanOperate && !PlayServPackageOperationGate.Shared.IsBusy;

        public static void WindowOpened()
        {
            Schedule.WindowOpened();
        }

        public static void WindowClosed() => Schedule.WindowClosed();
        public static void Check() { if (CanStart) _ = Schedule.CheckAsync(); }
        public static void Update()
        {
            if (!CanStart || string.IsNullOrEmpty(Controller.AvailableVersion)) return;
            _ = Controller.UpdateAsync(Controller.AvailableVersion, plan => EditorUtility.DisplayDialog(
                "Update PlayServ SDK",
                plan.Describe() + "\n\nOnly installed PlayServ packages are included. Unity will resolve dependencies and may recompile scripts. " +
                "Commit or back up your project before updating. There is no automatic rollback.",
                "Update", "Cancel"));
        }

        public static void OpenPackageManager() => UnityEditor.PackageManager.UI.Window.Open(PlayServSdkUpdatePlan.CoreId);

        private static void Tick()
        {
            if (CanStart) _ = Schedule.TickAsync(Application.isBatchMode);
        }

        private static void OnChanged()
        {
            if (Controller.State == PlayServSdkUpdateState.Updated && !_maintenanceQueued)
            {
                _maintenanceQueued = true;
                PlayServSdkCacheMaintenance.QueueTargetedMaintenance();
            }
            else if (Controller.State == PlayServSdkUpdateState.Updating) _maintenanceQueued = false;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
