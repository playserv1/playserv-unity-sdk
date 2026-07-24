using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor.Migration
{
    internal sealed class PlayServApiMigrationWindow : EditorWindow
    {
        private const string MenuPath = "Tools/PlayServ/Migrate Project";
        private const float WindowMinWidth = 760f;
        private const float WindowMinHeight = 520f;

        private readonly Dictionary<string, bool> _expandedFiles =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private PlayServApiMigrationScanResult _scan;
        private PlayServApiMigrationApplyResult _lastApply;
        private Vector2 _scrollPosition;
        private string _status;
        private GUIStyle _expressionStyle;

        [MenuItem(MenuPath, false, 120)]
        private static void ShowWindow()
        {
            var window = GetWindow<PlayServApiMigrationWindow>(
                utility: false,
                title: "PlayServ API Migration",
                focus: true);
            window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            minSize = new Vector2(WindowMinWidth, WindowMinHeight);
            ScanProject();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            DrawToolbar();
            DrawStatus();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                DrawScanIssues();
                DrawPreview();
                DrawLastReport();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawHeader()
        {
            GUILayout.Space(10f);
            EditorGUILayout.LabelField("PlayServ API Migration", EditorStyles.boldLabel);
            if (_scan != null)
            {
                EditorGUILayout.LabelField(
                    $"{_scan.ScannedFileCount} scripts scanned, " +
                    $"{_scan.ChangeCount} legacy references found",
                    EditorStyles.miniLabel);
            }

            GUILayout.Space(6f);
        }

        private void DrawToolbar()
        {
            var selectedCount = _scan?.SelectedChangeCount ?? 0;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Scan Project", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                    ScanProject();

                using (new EditorGUI.DisabledScope(_scan == null || _scan.ChangeCount == 0))
                {
                    if (GUILayout.Button("Select All", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                        SetAllSelected(true);
                    if (GUILayout.Button("Select None", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                        SetAllSelected(false);
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(selectedCount == 0))
                {
                    if (GUILayout.Button(
                            $"Apply Selected ({selectedCount})",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(150f)))
                    {
                        ApplySelected();
                    }
                }
            }
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status))
                return;

            EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void DrawScanIssues()
        {
            if (_scan == null || _scan.Issues.Count == 0)
                return;

            EditorGUILayout.LabelField("Scan Issues", EditorStyles.boldLabel);
            for (var i = 0; i < _scan.Issues.Count; i++)
            {
                var issue = _scan.Issues[i];
                EditorGUILayout.HelpBox(
                    issue.AssetPath + ": " + issue.Message,
                    MessageType.Warning);
            }

            GUILayout.Space(6f);
        }

        private void DrawPreview()
        {
            if (_scan == null)
                return;

            if (_scan.Files.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No legacy PlayServ module API references were found.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            for (var fileIndex = 0; fileIndex < _scan.Files.Count; fileIndex++)
                DrawFile(_scan.Files[fileIndex]);
        }

        private void DrawFile(PlayServApiMigrationFilePlan file)
        {
            if (!_expandedFiles.TryGetValue(file.AssetPath, out var expanded))
                expanded = true;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    expanded = EditorGUILayout.Foldout(
                        expanded,
                        $"{file.AssetPath} ({file.Changes.Count})",
                        toggleOnLabelClick: true);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open", GUILayout.Width(54f)))
                        OpenScript(file.AssetPath, file.Changes[0].Line);
                }

                _expandedFiles[file.AssetPath] = expanded;
                if (!expanded)
                    return;

                for (var i = 0; i < file.Changes.Count; i++)
                {
                    if (i > 0)
                        DrawSeparator();
                    DrawChange(file.Changes[i]);
                }
            }
        }

        private void DrawChange(PlayServApiMigrationChange change)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                change.IsSelected = EditorGUILayout.Toggle(
                    change.IsSelected,
                    GUILayout.Width(18f));
                EditorGUILayout.LabelField(
                    $"Line {change.Line}:{change.Column}",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(86f));
                EditorGUILayout.LabelField(
                    change.ModuleApi,
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(46f)))
                    OpenScript(change.AssetPath, change.Line);
            }

            EditorGUILayout.SelectableLabel(
                change.OriginalExpression,
                _expressionStyle,
                GUILayout.Height(EditorGUIUtility.singleLineHeight + 4f));
            EditorGUILayout.LabelField("to", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.SelectableLabel(
                change.ReplacementExpression,
                _expressionStyle,
                GUILayout.Height(EditorGUIUtility.singleLineHeight + 4f));
        }

        private void DrawLastReport()
        {
            if (_lastApply == null)
                return;

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("Last Migration", EditorStyles.boldLabel);
            var message =
                $"{_lastApply.AppliedChangeCount} changes applied to " +
                $"{_lastApply.UpdatedFileCount} files.";
            if (_lastApply.HasErrors)
                message += " Some files were skipped; see the report.";

            EditorGUILayout.HelpBox(
                message,
                _lastApply.HasErrors ? MessageType.Warning : MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           string.IsNullOrEmpty(_lastApply.ReportPath) ||
                           !File.Exists(_lastApply.ReportPath)))
                {
                    if (GUILayout.Button("Reveal Report", GUILayout.Width(112f)))
                        EditorUtility.RevealInFinder(_lastApply.ReportPath);
                }

                using (new EditorGUI.DisabledScope(
                           string.IsNullOrEmpty(_lastApply.BackupRoot) ||
                           !Directory.Exists(_lastApply.BackupRoot)))
                {
                    if (GUILayout.Button("Reveal Backups", GUILayout.Width(120f)))
                        EditorUtility.RevealInFinder(_lastApply.BackupRoot);
                }
            }
        }

        private void ScanProject()
        {
            try
            {
                _scan = PlayServApiMigrationEngine.ScanProject(ProjectRoot);
                _expandedFiles.Clear();
                _status = _scan.ChangeCount == 0
                    ? "Project scan completed. No migration is required."
                    : $"Review {_scan.ChangeCount} proposed changes before applying them.";
            }
            catch (Exception exception)
            {
                _scan = null;
                _status = "Project scan failed: " + exception.Message;
            }

            Repaint();
        }

        private void ApplySelected()
        {
            var selectedCount = _scan?.SelectedChangeCount ?? 0;
            if (selectedCount == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Apply PlayServ API Migration",
                    $"Apply {selectedCount} selected API changes? " +
                    "Original scripts will be backed up under Library/PlayServ.",
                    "Apply",
                    "Cancel"))
            {
                return;
            }

            _lastApply = PlayServApiMigrationEngine.Apply(_scan, DateTime.UtcNow);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            var applied = _lastApply.AppliedChangeCount;
            var hasErrors = _lastApply.HasErrors;
            ScanProject();
            _status = hasErrors
                ? $"Applied {applied} changes. Some files were skipped; review the migration report."
                : $"Applied {applied} changes successfully.";
        }

        private void SetAllSelected(bool selected)
        {
            if (_scan == null)
                return;

            for (var fileIndex = 0; fileIndex < _scan.Files.Count; fileIndex++)
            {
                var file = _scan.Files[fileIndex];
                for (var changeIndex = 0; changeIndex < file.Changes.Count; changeIndex++)
                    file.Changes[changeIndex].IsSelected = selected;
            }

            Repaint();
        }

        private static void OpenScript(string assetPath, int line)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            if (script != null)
                AssetDatabase.OpenAsset(script, line);
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.32f, 0.32f, 0.32f, 0.6f));
            GUILayout.Space(3f);
        }

        private void EnsureStyles()
        {
            if (_expressionStyle != null)
                return;

            _expressionStyle = new GUIStyle(EditorStyles.textField)
            {
                font = EditorStyles.miniFont,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                padding = new RectOffset(6, 6, 2, 2)
            };
        }

        private static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}
