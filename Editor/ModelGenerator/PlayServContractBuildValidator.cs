using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Playserv.Editor
{
    /// <summary>Runs the same offline contract validation used by player builds.</summary>
    public static class PlayServContractBuildValidator
    {
        /// <summary>Headless entry point: -executeMethod Playserv.Editor.PlayServContractBuildValidator.ValidateActiveBuild.</summary>
        [MenuItem("Tools/PlayServ/Validate Build Contracts")]
        public static void ValidateActiveBuild()
        {
            Validate(new BuildPlayerOptions
            {
                target = EditorUserBuildSettings.activeBuildTarget,
                subtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget,
                options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None
            });
        }

        internal static void Validate(BuildPlayerOptions options)
        {
            Validate(PlayServContractBuildInputs.Collect(options), PlayServSchemaToolRunner.ProjectRoot);
        }

        internal static void Validate(PlayServContractBuildInputs inputs, string projectRoot)
        {
            if (!inputs.ContainsContracts()) return;
            if (!PlayServSchemaToolRunner.TryResolveTool(out var dotnet, out var tool, out var error))
                throw new BuildFailedException("PlayServ contract validation: " + error +
                    " Install com.playserv.schema-tool at the same SDK revision using Tools > PlayServ > Settings > Schema Workflows.");
            var directory = Path.Combine(projectRoot, "Library", "PlayServ", "ContractValidation");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(inputs));
                var result = PlayServSchemaToolRunner.RunContractValidation(dotnet, tool, projectRoot, path);
                if (result.Response == null)
                    throw new BuildFailedException("PlayServ contract validation: " + result.Message);
                foreach (var diagnostic in result.Response.diagnostics)
                {
                    var message = FormatDiagnostic(diagnostic, projectRoot);
                    if (diagnostic.severity == "error") Debug.LogError(message);
                    else if (diagnostic.severity == "warning") Debug.LogWarning(message);
                }
                if (!result.Success)
                    throw new BuildFailedException("PlayServ contract validation failed. See the source diagnostics above.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        internal static string FormatDiagnostic(PlayServSchemaDiagnostic diagnostic, string projectRoot)
        {
            var path = string.IsNullOrEmpty(diagnostic.path) ? "PlayServ" : Path.GetFullPath(Path.Combine(projectRoot, diagnostic.path));
            return $"{path}({diagnostic.line},{diagnostic.column}): {diagnostic.severity} {diagnostic.code}: {diagnostic.message}";
        }
    }

    internal sealed class PlayServContractBuildContext : BuildPlayerProcessor
    {
        internal static BuildPlayerOptions? PendingOptions;
        public override int callbackOrder => -10001;
        public override void PrepareForBuild(BuildPlayerContext context)
        {
            PendingOptions = context.BuildPlayerOptions;
        }
    }

    internal sealed class PlayServContractBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10001;
        public void OnPreprocessBuild(BuildReport report)
        {
            var options = PlayServContractBuildContext.PendingOptions ?? new BuildPlayerOptions
            {
                target = report == null ? EditorUserBuildSettings.activeBuildTarget : report.summary.platform,
                subtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget,
                options = report == null ? (EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None) : report.summary.options
            };
            PlayServContractBuildContext.PendingOptions = null;
            PlayServContractBuildValidator.Validate(options);
        }
    }
}
