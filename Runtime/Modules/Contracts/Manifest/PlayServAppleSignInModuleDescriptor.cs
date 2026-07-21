using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(80)]
    internal sealed class PlayServAppleSignInModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.AppleSignInId,
                "Apple Sign In",
                "Native iOS Sign in with Apple facade, credential settings, Xcode capability setup, quick login, credential state, and revocation callbacks.",
                PlayServModuleManifest.DefineDisableAppleSignIn,
                defaultEnabled: false,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/AppleSignIn/Implementation", "Runtime/Modules/AppleSignIn/Editor" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.AppleSignIn");
        }
    }
}
