using System;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlayServModuleRepairer
    {
        public static PlayServModuleValidationReport RunInteractive()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(
                    "PlayServ module repair",
                    "Wait for the current Unity compile or asset update to finish, then run module repair again.",
                    "OK");
                return null;
            }

            try
            {
                var before = PlayServModuleValidator.Validate();
                var state = PlayServModuleGraphSynchronizer.LoadActiveState();
                PlayServRuntimeModuleDefines.Apply(state, syncModuleGraph: false);

                PlayServModuleGraphSynchronizer.SyncNow();

                var after = PlayServModuleValidator.Validate();
                LogResult(before, after);
                ShowResult(after);
                return after;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "PlayServ module repair failed",
                    $"Module repair stopped: {ex.GetBaseException().Message}\n\nDetails were written to the Console.",
                    "OK");
                return null;
            }
        }

        private static void LogResult(
            PlayServModuleValidationReport before,
            PlayServModuleValidationReport after)
        {
            Debug.Log(
                $"[PlayServ] Module repair completed. " +
                $"Before: {FormatCounts(before)} After: {FormatCounts(after)}");
            PlayServModuleValidator.LogReport(after);
        }

        private static void ShowResult(PlayServModuleValidationReport report)
        {
            if (report.IsClean)
            {
                EditorUtility.DisplayDialog(
                    "PlayServ module repair",
                    "Module defines, assembly references, and generated files are synchronized. Validation passed.",
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                report.HasErrors ? "PlayServ module repair incomplete" : "PlayServ module repair",
                $"Repairable module graph files were synchronized.\n\n{report.Summary}\n\nRemaining issues require a code or package-content change. Details were written to the Console.",
                "OK");
        }

        private static string FormatCounts(PlayServModuleValidationReport report)
        {
            return report == null
                ? "not available"
                : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.InfoCount} info item(s).";
        }
    }

    internal static class PlayServModuleRepairerMenu
    {
        [MenuItem("Tools/PlayServ/Repair SDK Modules")]
        private static void RepairSdkModules()
        {
            PlayServModuleRepairer.RunInteractive();
        }

        [MenuItem("Tools/PlayServ/Repair SDK Modules", true)]
        private static bool ValidateRepairSdkModules()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isUpdating;
        }
    }
}
