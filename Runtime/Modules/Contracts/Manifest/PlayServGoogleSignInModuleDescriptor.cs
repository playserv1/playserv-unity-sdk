using UnityEngine.Scripting;

namespace Playserv.Modules
{
    [Preserve]
    [PlayServModuleManifestDescriptor(90)]
    internal sealed class PlayServGoogleSignInModuleDescriptor : IPlayServModuleManifestDescriptor
    {
        public PlayServModuleManifestEntry CreateEntry()
        {
            return new PlayServModuleManifestEntry(
                PlayServModuleManifest.GoogleSignInId,
                "Google Sign In",
                "Google Sign-In facade for OAuth ID tokens, server auth codes, profile identity, silent sign-in, sign-out, and disconnect.",
                PlayServModuleManifest.DefineDisableGoogleSignIn,
                defaultEnabled: false,
                isServerModule: false,
                visibleInSettings: true,
                visibleInExport: true,
                assetPaths: new[] { "Runtime/Modules/GoogleSignIn/Implementation", "Runtime/Modules/GoogleSignIn/Editor" },
                dependencyIds: new[] { PlayServModuleManifest.ClientExecutionId },
                hiddenDependencyAssetPaths: null,
                hiddenDependencyModuleIds: null,
                rootAssemblyReference: "Playserv.Runtime.Modules.GoogleSignIn");
        }
    }
}
