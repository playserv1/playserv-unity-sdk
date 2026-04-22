#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Playserv.Deploy.Editor;
using Playserv.Deploy.Editor.Analysis;
using Playserv.Wrapper;

namespace Playserv.Editor
{
    public sealed partial class PlayServWindow
    {
        private void DrawDeploymentFoldout()
        {
            var expanded = BeginSectionCard(
                ref _foldDeployment,
                "Release",
                "Deployment",
                "Preview RPC code closure, sync deployed version, and ship the ZIP package to the active deployment endpoint.");

            if (expanded)
            {
                DrawNotice(
                    "Create a ZIP from selected files and upload it to your Deployment API endpoint.",
                    MessageType.Info);

                if (_so != null)
                {
                    _so.Update();

                    if (_pDeployTimeoutSeconds != null)
                        EditorGUILayout.PropertyField(_pDeployTimeoutSeconds, new GUIContent("Timeout Seconds"));

                    if (_pDeployAuthToken != null)
                    {
                        var updatedToken = EditorGUILayout.PasswordField("Deploy Auth Token", _pDeployAuthToken.stringValue);
                        if (!string.Equals(updatedToken, _pDeployAuthToken.stringValue, StringComparison.Ordinal))
                            _pDeployAuthToken.stringValue = updatedToken;
                    }

                    if (_so.ApplyModifiedProperties())
                        EditorUtility.SetDirty(_config);
                }

                GUILayout.Space(4);
                _deployFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    "Folder",
                    _deployFolder,
                    typeof(DefaultAsset),
                    false);

                _deployIncludeSubfolders = EditorGUILayout.ToggleLeft("Include subfolders", _deployIncludeSubfolders);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Pattern", GUILayout.Width(EditorGUIUtility.labelWidth));
                    _deployPattern = EditorGUILayout.TextField(_deployPattern, PlayServWindowTheme.InputStyle);
                }

                _deployKeepRelativePaths = EditorGUILayout.ToggleLeft(
                    "Keep relative paths in ZIP (recommended)",
                    _deployKeepRelativePaths);

                GUILayout.Space(6);

                using (new EditorGUI.DisabledScope(_deployRunning || _versionSyncRunning))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (DrawActionButton("Preview Files", ButtonTone.Secondary, GUILayout.Width(120f), GUILayout.Height(30f)))
                        {
                            _deployFilesPreview = BuildDeployFileList(out var err);
                            if (!string.IsNullOrEmpty(err))
                            {
                                _deployStatus = err;
                                _deployShowFileList = false;
                                _deployFilesPreview.Clear();
                            }
                            else
                            {
                                _deployStatus = "Preview ready.";
                                _deployShowFileList = true;
                            }
                        }

                        GUILayout.Space(8f);

                        if (DrawActionButton("Clear Preview", ButtonTone.Ghost, GUILayout.Width(120f), GUILayout.Height(30f)))
                        {
                            _deployFilesPreview.Clear();
                            _deployShowFileList = false;
                        }
                    }

                    GUILayout.Space(8f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (DrawActionButton("Sync Version", ButtonTone.Secondary, GUILayout.Width(120f), GUILayout.Height(30f)))
                            _ = StartVersionSyncAsync();

                        GUILayout.Space(8f);

                        if (DrawActionButton("Deploy Now", ButtonTone.Primary, GUILayout.Width(140f), GUILayout.Height(30f)))
                            _ = StartDeployAsync();
                    }
                }

                using (new EditorGUI.DisabledScope(!_deployRunning))
                {
                    if (DrawActionButton("Cancel", ButtonTone.Danger, GUILayout.Width(120f), GUILayout.Height(28f)))
                        _deployCts?.Cancel();
                }

                if (_deployShowFileList && _deployFilesPreview.Count > 0)
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField($"Files ({_deployFilesPreview.Count})", PlayServWindowTheme.MiniHeadingStyle);

                    using (new EditorGUILayout.VerticalScope(PlayServWindowTheme.LogContainerStyle))
                    {
                        _deployFilesScroll = EditorGUILayout.BeginScrollView(_deployFilesScroll, GUILayout.Height(140));
                        foreach (var f in _deployFilesPreview.Take(300))
                            EditorGUILayout.LabelField(f, PlayServWindowTheme.LogLineStyle);
                        if (_deployFilesPreview.Count > 300)
                            EditorGUILayout.LabelField($"...and {_deployFilesPreview.Count - 300} more", PlayServWindowTheme.EmptyStateStyle);
                        EditorGUILayout.EndScrollView();
                    }
                }

                if (_deployRunning || !string.IsNullOrWhiteSpace(_deployStatus))
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Status", PlayServWindowTheme.MiniHeadingStyle);
                    var deployMessageType = !_deployRunning && _deployStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                        ? MessageType.Warning
                        : MessageType.Info;
                    DrawNotice(string.IsNullOrEmpty(_deployStatus) ? "Working..." : _deployStatus, deployMessageType);

                    if (_deployRunning)
                        EditorGUILayout.Slider("Progress", _deployProgress, 0f, 1f);
                }

                if (_versionSyncRunning || !string.IsNullOrWhiteSpace(_versionSyncStatus))
                {
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Version Sync", PlayServWindowTheme.MiniHeadingStyle);
                    DrawNotice(_versionSyncStatus, _versionSyncRunning ? MessageType.Info : MessageType.Warning);
                }
            }

            EndSectionCard(expanded);
            EditorPrefs.SetBool(Const.PrefFoldDeployment, _foldDeployment);
        }

        private List<string> BuildDeployFileList(out string error)
        {
            error = null;

            if (_deployFolder == null)
            {
                error = "Please select a Folder to deploy.";
                return new List<string>();
            }

            var folderPath = AssetDatabase.GetAssetPath(_deployFolder);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                error = "Selected asset is not a folder.";
                return new List<string>();
            }

            var absoluteFolderPath = Path.GetFullPath(folderPath);

            var option = _deployIncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var pattern = string.IsNullOrWhiteSpace(_deployPattern) ? "*" : _deployPattern.Trim();

            string[] files;
            try
            {
                files = Directory.GetFiles(absoluteFolderPath, pattern, option);
            }
            catch (Exception e)
            {
                error = $"Failed to list files: {e.Message}";
                return new List<string>();
            }

            var list = files
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFiles = list;

            AnalysisResult analysisResult;
            try
            {
                var analyzer = new FunctionAnalyzerService();
                analysisResult = analyzer.AnalyzeFiles(candidateFiles);
            }
            catch (Exception ex)
            {
                error = $"Failed to analyze files: {ex.Message}";
                return new List<string>();
            }

            if (!analysisResult.Success)
            {
                var distinctErrors = analysisResult.Errors
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var errorsToShow = distinctErrors.Take(20).ToList();
                var hiddenErrorsCount = Math.Max(0, distinctErrors.Count - errorsToShow.Count);

                var details = errorsToShow.Count == 0
                    ? "  - Unknown validation error."
                    : string.Join(Environment.NewLine, errorsToShow.Select(e => $"  - {e}"));

                if (hiddenErrorsCount > 0)
                    details += Environment.NewLine + $"  - ...and {hiddenErrorsCount} more";

                error = "Code analysis failed:" + Environment.NewLine + details;
                return new List<string>();
            }

            var analyzedFileSet = new HashSet<string>(
                analysisResult.FilesToCompile.Where(f => !string.IsNullOrWhiteSpace(f)),
                StringComparer.OrdinalIgnoreCase);

            list = candidateFiles
                .Where(analyzedFileSet.Contains)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var excludedByAnalysis = candidateFiles
                .Where(f => !analyzedFileSet.Contains(f))
                .ToList();

            if (excludedByAnalysis.Count > 0)
            {
                Debug.LogWarning(
                    $"[PlayServ] Excluded {excludedByAnalysis.Count} file(s) not required by analyzer RPC dependency closure: " +
                    string.Join(", ", excludedByAnalysis.Take(15)));
            }

            if (list.Count == 0)
                error = "No files matched the current pattern.";
            else
                Debug.Log($"[PlayServ] Final deploy file count: {list.Count}. Files: {string.Join(", ", list)}");

            return list;
        }

        private async Task StartDeployAsync()
        {
            if (_deployRunning || _versionSyncRunning)
                return;

            if (_config == null)
            {
                Debug.LogError("[PlayServ] Please assign DeploymentSettings asset.");
                return;
            }

            if (_config == null || _so == null)
            {
                Debug.LogError("[PlayServ] Config is not loaded.");
                return;
            }

            var gameId = _pGameId?.stringValue;
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
                    $"Upload {files.Count} file(s) for GameId '{gameId}'?\n\nEndpoint:\n{ResolveDeployEndpointForDisplay()}",
                    "Deploy",
                    "Cancel"))
            {
                return;
            }

            _deployRunning = true;
            _deployProgress = 0.05f;
            _deployStatus = "Preparing files...";
            _deployCts = new CancellationTokenSource();

            try
            {
                EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);
                var api = new DeploymentApiClient(_config);

                if (_deployKeepRelativePaths)
                {
                    try
                    {
                        await DeployWithRelativePathsAsync(api, gameId, files, _deployFolder, _deployCts.Token);
                    }
                    catch (InvalidOperationException e) when (IsNativeAotFailure(e) || IsInvalidZipUploadFailure(e))
                    {
                        Debug.LogWarning(
                            "[PlayServ] Relative-path ZIP upload failed. Retrying with flat ZIP packaging.");

                        var service = new DeploymentService(api);
                        await service.DeployAsync(gameId, files, _deployCts.Token);
                    }
                }
                else
                {
                    var service = new DeploymentService(api);
                    await service.DeployAsync(gameId, files, _deployCts.Token);
                }

                _deployProgress = 1f;
                _deployStatus = "Done.";
                Debug.Log("[PlayServ] Deployment completed successfully.");
            }
            catch (OperationCanceledException)
            {
                _deployStatus = "Cancelled.";
                Debug.LogWarning("[PlayServ] Deployment cancelled.");
            }
            catch (Exception e)
            {
                _deployStatus = $"Failed: {e.Message}";
                Debug.LogError($"[PlayServ] Deployment failed: {e}");
            }
            finally
            {
                _deployRunning = false;
                EditorUtility.ClearProgressBar();

                _deployCts?.Dispose();
                _deployCts = null;
            }
        }

        private async Task StartVersionSyncAsync()
        {
            if (_versionSyncRunning || _deployRunning)
                return;

            if (_config == null || _so == null)
            {
                Debug.LogError("[PlayServ] Config is not loaded.");
                return;
            }

            var gameId = _pGameId?.stringValue;
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
                    $"Compare local RPC code hash against remote for GameId '{gameId}'?",
                    "Sync",
                    "Cancel"))
            {
                return;
            }

            _versionSyncRunning = true;
            _versionSyncStatus = "Fetching schemas...";
            Repaint();

            try
            {
                var api = new DeploymentApiClient(_config);

                await api.FetchLatestSchemasAsync(gameId);
                _versionSyncStatus = "Computing local hash...";
                Repaint();

                var localHash = ComputeCodeHash(CreateZipArchiveBytes(files));

                _versionSyncStatus = "Fetching remote hash...";
                Repaint();

                var remoteHash = await api.GetRemoteCodeHashAsync(gameId);

                if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                {
                    _versionSyncStatus = "Hashes match. Fetching latest version...";
                    Repaint();

                    var latestVersion = await api.GetLatestVersionAsync(gameId);

                    _so.Update();
                    if (_pGameVersion != null)
                    {
                        _pGameVersion.stringValue = latestVersion;
                        _so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_config);
                    }

                    _versionSyncStatus = $"Synced to version {latestVersion}.";
                    Debug.Log($"[PlayServ] Version synchronized to {latestVersion}.");
                }
                else
                {
                    _versionSyncStatus = "Hash mismatch. Downloading archive...";
                    Repaint();

                    var archiveOutputDir = Path.Combine(Path.GetTempPath(), "playserv-sync");
                    var archivePath = await api.DownloadCodeArchiveAsync(gameId, archiveOutputDir);

                    _versionSyncStatus = "Hash mismatch. Archive downloaded.";
                    Debug.LogWarning($"[PlayServ] Code hash mismatch. Archive saved to: {archivePath}");
                }
            }
            catch (Exception e)
            {
                _versionSyncStatus = $"Sync failed: {e.Message}";
                Debug.LogError($"[PlayServ] Version sync failed: {e}");
            }
            finally
            {
                _versionSyncRunning = false;
                Repaint();
            }
        }

        private static byte[] CreateZipArchiveBytes(List<string> filePaths)
        {
            using var memoryStream = new MemoryStream();

            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in filePaths)
                {
                    if (!File.Exists(filePath))
                        continue;

                    archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                }
            }

            return memoryStream.ToArray();
        }

        private static string ComputeCodeHash(byte[] zipBytes)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(zipBytes);
            return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
        }

        private string ResolveDeployEndpointForDisplay()
        {
            var endpoint = _config?.DeployApiServerAddress?.Trim();
            return PlayServPackageDefaultsProvider.ResolveDeployApiServerAddress(endpoint);
        }

        private async Task DeployWithRelativePathsAsync(
            DeploymentApiClient api,
            string gameId,
            List<string> absoluteFiles,
            DefaultAsset rootFolderAsset,
            CancellationToken ct)
        {
            var rootPath = Path.GetFullPath(AssetDatabase.GetAssetPath(rootFolderAsset));

            _deployStatus = "Creating ZIP...";
            _deployProgress = 0.15f;
            EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);

            var zipPath = CreateZipWithRelativePaths(rootPath, absoluteFiles);

            try
            {
                _deployStatus = "Uploading ZIP...";
                _deployProgress = 0.55f;
                EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);

                await api.UploadDeploymentAsync(gameId, zipPath, ct);

                _deployStatus = "Upload finished.";
                _deployProgress = 0.95f;
                EditorUtility.DisplayProgressBar("PlayServ Deployment", _deployStatus, _deployProgress);
            }
            finally
            {
                TryDeleteTemp(zipPath);
            }
        }

        private static string CreateZipWithRelativePaths(string rootFolderPath, List<string> absoluteFiles)
        {
            var zipPath = Path.Combine(Path.GetTempPath(), $"playserv_deploy_{Guid.NewGuid():N}.zip");

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var root = rootFolderPath.Replace('\\', '/').TrimEnd('/');

                foreach (var absFile in absoluteFiles)
                {
                    if (!File.Exists(absFile))
                        continue;

                    var normalized = absFile.Replace('\\', '/');

                    var entry = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        ? normalized.Substring(root.Length).TrimStart('/')
                        : Path.GetFileName(absFile);

                    if (string.IsNullOrWhiteSpace(entry))
                        entry = Path.GetFileName(absFile);

                    archive.CreateEntryFromFile(absFile, entry);
                }
            }

            return zipPath;
        }

        private static void TryDeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayServ] Failed to delete temp zip: {e.Message}");
            }
        }

        private static bool IsNativeAotFailure(Exception exception)
        {
            if (exception == null)
                return false;

            var message = exception.Message ?? string.Empty;
            return message.IndexOf("Native AOT compilation failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInvalidZipUploadFailure(Exception exception)
        {
            if (exception == null)
                return false;

            var message = exception.Message ?? string.Empty;
            return message.IndexOf("Request body is empty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("valid ZIP archive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> FilterFilesWithUnsupportedUsingNamespaces(
            List<string> files,
            out List<string> excludedFiles)
        {
            excludedFiles = new List<string>();
            var validFiles = new List<string>(files.Count);

            foreach (var file in files)
            {
                if (HasUnsupportedUsingNamespace(file))
                {
                    excludedFiles.Add(file);
                    continue;
                }

                validFiles.Add(file);
            }

            return validFiles;
        }

        private static bool HasUnsupportedUsingNamespace(string filePath)
        {
            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch
            {
                return true;
            }

            var usingMatches = Regex.Matches(text, @"^\s*using\s+([^;]+);", RegexOptions.Multiline);
            foreach (Match match in usingMatches)
            {
                var rawTarget = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(rawTarget))
                    continue;

                if (rawTarget.StartsWith("static ", StringComparison.Ordinal))
                    rawTarget = rawTarget.Substring("static ".Length).Trim();

                var aliasIndex = rawTarget.IndexOf('=');
                if (aliasIndex >= 0 && aliasIndex < rawTarget.Length - 1)
                    rawTarget = rawTarget.Substring(aliasIndex + 1).Trim();

                if (rawTarget.StartsWith("global::", StringComparison.Ordinal))
                    rawTarget = rawTarget.Substring("global::".Length);

                if (!IsAllowedDeployUsingNamespace(rawTarget))
                    return true;
            }

            return false;
        }

        private static bool IsAllowedDeployUsingNamespace(string namespaceName)
        {
            foreach (var allowed in AllowedDeployUsingNamespaces)
            {
                if (string.Equals(namespaceName, allowed, StringComparison.Ordinal) ||
                    namespaceName.StartsWith(allowed + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidateRpcConstructors(List<string> files, out string error)
        {
            error = null;
            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception e)
                {
                    error = $"Failed to read '{file}': {e.Message}";
                    return false;
                }

                if (text.IndexOf("[Rpc]", StringComparison.Ordinal) < 0)
                    continue;

                var classMatch = Regex.Match(text, @"class\s+([A-Za-z_][A-Za-z0-9_]*)");
                if (!classMatch.Success)
                    continue;

                var className = classMatch.Groups[1].Value;
                var ctorPattern = @"\b" + Regex.Escape(className) + @"\s*\(([^)]*)\)";
                var ctorMatches = Regex.Matches(text, ctorPattern);
                var constructorCount = ctorMatches.Count;

                if (constructorCount > 1)
                {
                    error = $"RPC class '{className}' in '{file}' cannot define multiple constructors.";
                    return false;
                }
            }

            return true;
        }

        private static List<string> ReduceToRpcDependencyClosure(List<string> files, out List<string> excludedFiles)
        {
            excludedFiles = new List<string>();
            if (files == null || files.Count == 0)
                return new List<string>();

            var contentByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var typesByFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var fileByType = new Dictionary<string, string>(StringComparer.Ordinal);
            var rpcRootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                contentByFile[file] = text;
                if (ContainsRpcAttribute(text))
                    rpcRootFiles.Add(file);

                var declaredTypes = ExtractDeclaredTypes(text);
                typesByFile[file] = declaredTypes;

                foreach (var typeName in declaredTypes)
                {
                    if (!fileByType.ContainsKey(typeName))
                        fileByType[typeName] = file;
                }
            }

            if (rpcRootFiles.Count == 0)
                return files.ToList();

            var dependencyGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (!contentByFile.TryGetValue(file, out var text))
                    continue;

                var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var typeName in fileByType.Keys)
                {
                    if (typesByFile.TryGetValue(file, out var ownTypes) && ownTypes.Contains(typeName))
                        continue;

                    if (ContainsTypeReference(text, typeName))
                    {
                        var depFile = fileByType[typeName];
                        if (!string.Equals(depFile, file, StringComparison.OrdinalIgnoreCase))
                            dependencies.Add(depFile);
                    }
                }

                dependencyGraph[file] = dependencies;
            }

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(rpcRootFiles);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!selected.Add(current))
                    continue;

                if (!dependencyGraph.TryGetValue(current, out var deps))
                    continue;

                foreach (var dep in deps)
                {
                    if (!selected.Contains(dep))
                        queue.Enqueue(dep);
                }
            }

            var result = files.Where(f => selected.Contains(f)).ToList();
            excludedFiles = files.Where(f => !selected.Contains(f)).ToList();
            return result.Count > 0 ? result : files.ToList();
        }

        private static bool ContainsRpcAttribute(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"\[\s*Rpc(\s*\(|\s*\])", RegexOptions.Multiline);
        }

        private static HashSet<string> ExtractDeclaredTypes(string text)
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text))
                return types;

            var matches = Regex.Matches(text, @"\b(class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)");
            foreach (Match match in matches)
            {
                var typeName = match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(typeName))
                    types.Add(typeName);
            }

            return types;
        }

        private static bool ContainsTypeReference(string text, string typeName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(typeName))
                return false;

            var pattern = $@"\b{Regex.Escape(typeName)}\b";
            return Regex.IsMatch(text, pattern);
        }
    }
}
#endif
