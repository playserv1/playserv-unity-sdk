using Playserv.AppleSignIn;
using Playserv.Modules;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

#if UNITY_IOS
using System.IO;
using UnityEditor.iOS.Xcode;
#endif

namespace Playserv.Editor.AppleSignIn
{
    internal static class PlayServAppleSignInPostprocessor
    {
        [PostProcessBuild(20)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS)
                return;

            var moduleState = PlayServRuntimeModuleDefines.LoadUserPreferenceState();
            PlayServEditorModuleAvailability.NormalizeAvailableRuntimeState(moduleState);
            PlayServRuntimeModuleDefines.NormalizeDependencies(moduleState);
            if (!moduleState.IsEnabled(PlayServModuleManifest.AppleSignInId))
                return;

            var settings = PlayServAppleSignInSettingsAssetProvider.FindExisting();
            if (settings != null && !settings.AddSignInCapabilityOnBuild)
                return;

            AddAppleSignInCapability(pathToBuiltProject, settings);
#endif
        }

#if UNITY_IOS
        private static void AddAppleSignInCapability(string pathToBuiltProject, PlayServAppleSignInSettings settings)
        {
            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromString(File.ReadAllText(projectPath));

            var mainTargetGuid = project.GetUnityMainTargetGuid();
            var frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(frameworkTargetGuid, "AuthenticationServices.framework", true);
            File.WriteAllText(projectPath, project.WriteToString());

            var entitlementsFileName = settings == null
                ? "PlayServAppleSignIn.entitlements"
                : settings.EntitlementsFileName;
            var manager = new ProjectCapabilityManager(projectPath, entitlementsFileName, null, mainTargetGuid);
            manager.AddSignInWithApple();
            manager.WriteToFile();

            Debug.Log("[PlayServ] Added Sign In with Apple capability and AuthenticationServices.framework to the Xcode project.");
        }
#endif
    }
}
