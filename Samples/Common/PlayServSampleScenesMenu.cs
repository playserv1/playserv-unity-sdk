using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Playserv.Samples
{
    /// <summary>
    /// Simple scene switcher menu for PlayServ samples.
    /// Lists scenes from the same folder as the currently opened scene.
    /// </summary>
    public sealed class PlayServSampleScenesMenu : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool includeCurrentScene;
        [SerializeField] private bool autoRefreshOnEnable = true;

        private readonly List<SceneEntry> _sceneEntries = new List<SceneEntry>();
        private readonly List<string> _logs = new List<string>();
        private Vector2 _scroll;
        private Vector2 _logsScroll;
        private string _status = "Idle";
        private string _currentScenePath = string.Empty;
        private bool _showInfo;

        private sealed class SceneEntry
        {
            public string Name;
            public string Path;
            public int BuildIndex;
        }

        private void OnEnable()
        {
            if (autoRefreshOnEnable)
                RefreshSceneList();
        }

        [ContextMenu("Refresh Scene List")]
        public void RefreshSceneList()
        {
            _sceneEntries.Clear();

            var activeScene = SceneManager.GetActiveScene();
            _currentScenePath = NormalizePath(activeScene.path);
            if (string.IsNullOrWhiteSpace(_currentScenePath))
            {
                _status = "Active scene path is empty.";
                return;
            }

            var currentFolder = NormalizePath(Path.GetDirectoryName(_currentScenePath));
            var sceneIndexByPath = BuildSceneIndexByPath();
            var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scenePath in EnumerateScenesInFolder(currentFolder))
            {
                var normalizedPath = NormalizePath(scenePath);
                if (string.IsNullOrWhiteSpace(normalizedPath) ||
                    !knownPaths.Add(normalizedPath))
                {
                    continue;
                }

                if (!includeCurrentScene &&
                    string.Equals(normalizedPath, _currentScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _sceneEntries.Add(new SceneEntry
                {
                    Name = Path.GetFileNameWithoutExtension(normalizedPath),
                    Path = normalizedPath,
                    BuildIndex = sceneIndexByPath.TryGetValue(normalizedPath, out var index) ? index : -1
                });
            }

            if (_sceneEntries.Count == 0)
            {
                foreach (var kvp in sceneIndexByPath)
                {
                    if (!kvp.Key.StartsWith(currentFolder, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!includeCurrentScene &&
                        string.Equals(kvp.Key, _currentScenePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _sceneEntries.Add(new SceneEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(kvp.Key),
                        Path = kvp.Key,
                        BuildIndex = kvp.Value
                    });
                }
            }

            _sceneEntries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            _status = _sceneEntries.Count > 0
                ? $"Found {_sceneEntries.Count} scenes."
                : "No scenes found in folder.";
            AddLog(_status);
        }

        private void LoadScene(SceneEntry scene)
        {
            if (scene == null || string.IsNullOrWhiteSpace(scene.Path))
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorSceneManager.OpenScene(scene.Path);
                _status = $"Opened '{scene.Name}' in editor.";
                AddLog(_status);
                return;
            }
#endif
            try
            {
                AddLog($"Loading scene '{scene.Name}'.");
                if (scene.BuildIndex >= 0)
                {
                    SceneManager.LoadScene(scene.BuildIndex);
                    return;
                }

                SceneManager.LoadScene(scene.Path);
            }
            catch (Exception ex)
            {
                _status = $"Failed to load '{scene.Name}': {ex.Message}";
                AddLog(_status);
                Debug.LogError($"[PlayServ][Samples] {_status}");
            }
        }

        private async Task ConnectSdkAsync()
        {
            try
            {
                var connected = await PlayServ.Connect();
                _status = connected ? "SDK connected." : "SDK connection failed.";
                AddLog(_status);
            }
            catch (Exception ex)
            {
                _status = $"Connect error: {ex.Message}";
                AddLog(_status);
            }
        }

        private void DisconnectSdk()
        {
            PlayServ.Disconnect();
            _status = "SDK disconnected.";
            AddLog(_status);
        }

        private static Dictionary<string, int> BuildSceneIndexByPath()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var sceneCount = SceneManager.sceneCountInBuildSettings;
            for (var i = 0; i < sceneCount; i++)
            {
                var scenePath = NormalizePath(SceneUtility.GetScenePathByBuildIndex(i));
                if (!string.IsNullOrWhiteSpace(scenePath))
                    result[scenePath] = i;
            }

            return result;
        }

        private static IEnumerable<string> EnumerateScenesInFolder(string folderAssetPath)
        {
            if (string.IsNullOrWhiteSpace(folderAssetPath))
                yield break;

            var absoluteFolderPath = ConvertAssetPathToAbsolutePath(folderAssetPath);
            if (string.IsNullOrWhiteSpace(absoluteFolderPath) || !Directory.Exists(absoluteFolderPath))
                yield break;

            foreach (var sceneFilePath in Directory.GetFiles(absoluteFolderPath, "*.unity", SearchOption.TopDirectoryOnly))
                yield return ConvertAbsolutePathToAssetPath(sceneFilePath);
        }

        private static string ConvertAssetPathToAbsolutePath(string assetPath)
        {
            var normalizedAssetPath = NormalizePath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedAssetPath))
                return string.Empty;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                return string.Empty;

            if (!normalizedAssetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return NormalizePath(Path.Combine(projectRoot, normalizedAssetPath));

            var relativeToAssets = normalizedAssetPath.Length > "Assets".Length
                ? normalizedAssetPath.Substring("Assets".Length).TrimStart('/')
                : string.Empty;

            return NormalizePath(Path.Combine(projectRoot, "Assets", relativeToAssets));
        }

        private static string ConvertAbsolutePathToAssetPath(string absolutePath)
        {
            var normalizedAbsolute = NormalizePath(absolutePath);
            var normalizedAssetsRoot = NormalizePath(Application.dataPath);
            if (string.IsNullOrWhiteSpace(normalizedAbsolute) || string.IsNullOrWhiteSpace(normalizedAssetsRoot))
                return string.Empty;

            if (!normalizedAbsolute.StartsWith(normalizedAssetsRoot, StringComparison.OrdinalIgnoreCase))
                return normalizedAbsolute;

            var relativeToAssets = normalizedAbsolute.Substring(normalizedAssetsRoot.Length).TrimStart('/');
            return string.IsNullOrWhiteSpace(relativeToAssets)
                ? "Assets"
                : $"Assets/{relativeToAssets}";
        }

        private static string NormalizePath(string path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _logs.Add(line);
            if (_logs.Count > 64)
                _logs.RemoveAt(0);

            _logsScroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            SampleGuiFontScale.Apply();

            var margin = 10f;
            var areaRect = new Rect(
                margin,
                margin,
                Mathf.Max(320f, Screen.width - margin * 2f),
                Mathf.Max(220f, Screen.height - margin * 2f));
            var sceneListHeight = Mathf.Max(140f, areaRect.height * 0.38f);
            var logsHeight = Mathf.Max(140f, areaRect.height * 0.30f);

            GUILayout.BeginArea(areaRect, GUI.skin.box);
            GUILayout.Label("PlayServ Samples Hub (0_Samples)");
            GUILayout.Label("How to use: configure 'PlayServBootstrap' in this scene inspector, run Play mode, then open a sample scene below.");
            GUILayout.Label("Bootstrap is persistent (DontDestroyOnLoad), so one connection is reused across scenes.");
            GUILayout.Label($"Current scene: {Path.GetFileNameWithoutExtension(_currentScenePath)}");
            GUILayout.Label($"SDK state: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh"))
                RefreshSceneList();
            if (GUILayout.Button("Connect SDK"))
                _ = ConnectSdkAsync();
            if (GUILayout.Button("Disconnect SDK"))
                DisconnectSdk();
            if (GUILayout.Button(_showInfo ? "Hide Info" : "Info"))
                _showInfo = !_showInfo;
            GUILayout.EndHorizontal();

            if (_showInfo)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Info");
                GUILayout.Label("Purpose: This is the sample launcher and central SDK control point.");
                GUILayout.Label("How to use: Configure PlayServBootstrap, connect once, then open any sample scene.");
                GUILayout.Label("Use in your game: Keep one persistent bootstrap and reuse one SDK connection across scenes.");
                GUILayout.EndVertical();
            }

            GUILayout.Space(6f);
            GUILayout.Label("Scenes:");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(sceneListHeight));
            if (_sceneEntries.Count == 0)
            {
                GUILayout.Label("- No scenes");
            }
            else
            {
                for (var i = 0; i < _sceneEntries.Count; i++)
                {
                    var scene = _sceneEntries[i];
                    var label = scene.BuildIndex >= 0
                        ? $"{scene.Name}"
                        : $"{scene.Name} (not in Build Settings)";

                    if (GUILayout.Button(label))
                        LoadScene(scene);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            GUILayout.Label($"Logs ({_logs.Count}):");
            _logsScroll = GUILayout.BeginScrollView(_logsScroll, GUILayout.Height(logsHeight));
            if (_logs.Count == 0)
            {
                GUILayout.Label("- No logs yet");
            }
            else
            {
                foreach (var line in _logs)
                    GUILayout.Label($"- {line}");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
