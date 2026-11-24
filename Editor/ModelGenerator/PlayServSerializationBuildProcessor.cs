using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal sealed class PlayServSerializationBuildProcessor : BuildPlayerProcessor, IPreprocessBuildWithReport
    {
        private static BuildPlayerOptions? _options;
        public override int callbackOrder => -11000;

        static PlayServSerializationBuildProcessor()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                try { Validate(PlayServSerializationGenerator.ActiveOptions()); }
                catch (BuildFailedException error)
                {
                    EditorApplication.isPlaying = false;
                    UnityEngine.Debug.LogError(error.Message);
                }
            };
        }

        public override void PrepareForBuild(BuildPlayerContext context) => _options = context.BuildPlayerOptions;

        public void OnPreprocessBuild(BuildReport report)
        {
            var options = _options ?? (report == null ? PlayServSerializationGenerator.ActiveOptions() : new BuildPlayerOptions
            {
                target = report.summary.platform, options = report.summary.options,
                subtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget
            });
            _options = null;
            Validate(options);
        }

        private static void Validate(BuildPlayerOptions options)
        {
            var inputs = PlayServContractBuildInputs.Collect(options);
            var output = PlayServSerializationGenerator.BuildVariants(inputs, PlayServSerializationGenerator.EditorInputs(inputs));
            PlayServSerializationGenerator.ValidateOutput(output, Directory.GetCurrentDirectory());
        }
    }
}
