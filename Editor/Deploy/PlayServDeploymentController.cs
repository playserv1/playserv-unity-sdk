using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.Deploy.Editor;

namespace Playserv.Editor
{
    internal sealed class PlayServDeploymentController : IDisposable
    {
        private readonly PlayServWindowState _state;
        private readonly DeploymentClosureFilter _deploymentClosureFilter;
        private readonly DeploymentUploadAction _deploymentUploadAction;
        private readonly VersionSyncAction _versionSyncAction;
        private readonly Action _repaint;

        public PlayServDeploymentController(
            PlayServWindowState state,
            DeploymentClosureFilter deploymentClosureFilter,
            DeploymentUploadAction deploymentUploadAction,
            VersionSyncAction versionSyncAction,
            Action repaint)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _deploymentClosureFilter = deploymentClosureFilter ?? throw new ArgumentNullException(nameof(deploymentClosureFilter));
            _deploymentUploadAction = deploymentUploadAction ?? throw new ArgumentNullException(nameof(deploymentUploadAction));
            _versionSyncAction = versionSyncAction ?? throw new ArgumentNullException(nameof(versionSyncAction));
            _repaint = repaint ?? throw new ArgumentNullException(nameof(repaint));
        }

        public List<string> BuildDeployFileList(out string error)
        {
            return _deploymentClosureFilter.CollectDeployFiles(
                _state.DeployFolder,
                _state.DeployIncludeSubfolders,
                _state.DeployPattern,
                out error);
        }

        public async Task StartDeployAsync(PlayServWindowContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (_state.DeployRunning || _state.VersionSyncRunning)
                return;

            if (context.Config == null || context.SerializedObject == null)
            {
                Debug.LogError("[PlayServ] Config is not loaded.");
                return;
            }

            var gameId = context.GameIdProperty != null ? context.GameIdProperty.stringValue : null;
            if (string.IsNullOrWhiteSpace(gameId))
            {
                Debug.LogError("[PlayServ] GameId is empty. Please set it in PlayServ Config.");
                return;
            }

            var files = BuildDeployFileList(out var err);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError($"[PlayServ] {err}");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Deploy",
                    $"Upload {files.Count} file(s) for GameId '{gameId}'?\n\nEndpoint:\n{ResolveDeployEndpointForDisplay(context.Config)}",
                    "Deploy",
                    "Cancel"))
            {
                return;
            }

            _state.DeployRunning = true;
            _state.DeployProgress = 0.05f;
            _state.DeployStatus = "Preparing files...";
            _state.DeployCts = new CancellationTokenSource();

            try
            {
                UpdateDeployProgress(_state.DeployStatus, _state.DeployProgress);

                var rootPath = Path.GetFullPath(AssetDatabase.GetAssetPath(_state.DeployFolder));
                await _deploymentUploadAction.ExecuteAsync(
                    context.Config,
                    gameId,
                    files,
                    rootPath,
                    _state.DeployKeepRelativePaths,
                    UpdateDeployProgress,
                    _state.DeployCts.Token);

                _state.DeployProgress = 1f;
                _state.DeployStatus = "Done.";
                Debug.Log("[PlayServ] Deployment completed successfully.");
            }
            catch (OperationCanceledException)
            {
                _state.DeployStatus = "Cancelled.";
                Debug.LogWarning("[PlayServ] Deployment cancelled.");
            }
            catch (Exception e)
            {
                _state.DeployStatus = $"Failed: {e.Message}";
                Debug.LogError($"[PlayServ] Deployment failed: {e}");
            }
            finally
            {
                _state.DeployRunning = false;
                EditorUtility.ClearProgressBar();
                _state.DeployCts?.Dispose();
                _state.DeployCts = null;
                _repaint();
            }
        }

        public async Task StartVersionSyncAsync(PlayServWindowContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (_state.VersionSyncRunning || _state.DeployRunning)
                return;

            if (context.Config == null || context.SerializedObject == null)
            {
                Debug.LogError("[PlayServ] Config is not loaded.");
                return;
            }

            var gameId = context.GameIdProperty != null ? context.GameIdProperty.stringValue : null;
            if (string.IsNullOrWhiteSpace(gameId))
            {
                Debug.LogError("[PlayServ] GameId is empty. Please set it in PlayServ Config.");
                return;
            }

            var files = BuildDeployFileList(out var err);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError($"[PlayServ] {err}");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Sync Version",
                    $"Compare RPC code hash against remote for GameId '{gameId}'?",
                    "Sync",
                    "Cancel"))
            {
                return;
            }

            _state.VersionSyncRunning = true;
            _state.VersionSyncStatus = "Fetching schemas...";
            _repaint();

            try
            {
                var result = await _versionSyncAction.ExecuteAsync(
                    context.Config,
                    gameId,
                    files,
                    SetVersionSyncStatus,
                    CancellationToken.None);

                if (result.HashesMatch)
                {
                    context.SerializedObject.Update();
                    if (context.GameVersionProperty != null)
                    {
                        context.GameVersionProperty.stringValue = result.LatestVersion;
                        context.SerializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(context.Config);
                    }

                    _state.VersionSyncStatus = $"Synced to version {result.LatestVersion}.";
                    Debug.Log($"[PlayServ] Version synchronized to {result.LatestVersion}.");
                }
                else
                {
                    _state.VersionSyncStatus = "Hash mismatch. Archive downloaded.";
                    Debug.LogWarning($"[PlayServ] Code hash mismatch. Archive saved to: {result.ArchivePath}");
                }
            }
            catch (Exception e)
            {
                _state.VersionSyncStatus = $"Sync failed: {e.Message}";
                Debug.LogError($"[PlayServ] Version sync failed: {e}");
            }
            finally
            {
                _state.VersionSyncRunning = false;
                _repaint();
            }
        }

        public void CancelDeploy()
        {
            _state.DeployCts?.Cancel();
        }

        public void Dispose()
        {
            _state.DeployCts?.Cancel();
            _state.DeployCts?.Dispose();
            _state.DeployCts = null;

            if (_state.DeployRunning)
            {
                _state.DeployRunning = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private void UpdateDeployProgress(string status, float progress)
        {
            _state.DeployStatus = status;
            _state.DeployProgress = progress;
            EditorUtility.DisplayProgressBar("PlayServ Deployment", _state.DeployStatus, _state.DeployProgress);
            _repaint();
        }

        private void SetVersionSyncStatus(string status)
        {
            _state.VersionSyncStatus = status;
            _repaint();
        }

        private static string ResolveDeployEndpointForDisplay(Playserv.Wrapper.PlayServConfig config)
        {
            var endpoint = config == null ? null : config.DeployApiServerAddress == null ? string.Empty : config.DeployApiServerAddress.Trim();
            return Playserv.Wrapper.PlayServPackageDefaultsProvider.ResolveDeployApiServerAddress(endpoint);
        }
    }
}
