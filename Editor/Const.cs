using Playserv.Modules;

namespace Playserv.Editor
{
    internal class Const
    {
        internal const string PrefKeyShowOnStartup = "PlayServ.Window.ShowOnStartup";
        internal const string PrefKeyAutoCodegen   = "PlayServ.Codegen.AutoGenerate";

        internal const string PrefFoldCodegen = "PlayServ.Window.Fold.Codegen";
        internal const string PrefFoldEvents = "PlayServ.Window.Fold.Events";
        internal const string PrefFoldModel = "PlayServ.Window.Fold.Model";
        internal const string PrefFoldConfig  = "PlayServ.Window.Fold.Config";
        internal const string PrefFoldDeployment = "PlayServ.Fold.Deployment";
        internal const string PrefFoldConnection = "PlayServ.Window.Fold.Connection";
        internal const string PrefFoldAppleSignIn = "PlayServ.Window.Fold.AppleSignIn";
        internal const string PrefFoldGoogleSignIn = "PlayServ.Window.Fold.GoogleSignIn";
        internal const string PrefKeyDeploymentFolderAssetPath = "PlayServ.Deployment.FolderAssetPath";

        internal const string PrefModuleDeployment = "PlayServ.Window.Module.Deployment";
        internal const string PrefModuleModelSync = "PlayServ.Window.Module.ModelSync";
        internal const string PrefModuleCodegen = "PlayServ.Window.Module.Codegen";
        internal const string PrefModuleStressTests = "PlayServ.Window.Module.StressTests";
        internal const string PrefRuntimeModuleUserDisabledPrefix = "PlayServ.Runtime.Module.UserDisabled.";

        internal const string DefineDisableEvents = PlayServModuleManifest.DefineDisableEvents;
        internal const string DefineDisableData = PlayServModuleManifest.DefineDisableData;
        internal const string DefineDisableRpcCore = PlayServModuleManifest.DefineDisableRpcCore;
        internal const string DefineDisableClientRpc = PlayServModuleManifest.DefineDisableClientRpc;
        internal const string DefineDisableServer = PlayServModuleManifest.DefineDisableServer;
        internal const string DefineDisableClientExecution = PlayServModuleManifest.DefineDisableClientExecution;
        internal const string DefineDisableSpawn = PlayServModuleManifest.DefineDisableSpawn;
        internal const string DefineDisablePulse = PlayServModuleManifest.DefineDisablePulse;
        internal const string DefineDisableEditorDeployment = "PLAYSERV_DISABLE_EDITOR_DEPLOYMENT";
        internal const string DefineDisableEditorModelSync = "PLAYSERV_DISABLE_EDITOR_MODEL_SYNC";
        internal const string DefineDisableEditorCodegen = "PLAYSERV_DISABLE_EDITOR_DTO_CODEGEN";
        
        internal const string PrefKeyJsonSchemaTimestamp = "PlayServ.JsonSchema.Timestamp";
        internal const string PrefKeyJsonSchemaVersion   = "PlayServ.JsonSchema.Version.";
        internal const string PrefKeyJsonSchemaLatestTimestamp = "PlayServ.JsonSchema.Latest.Timestamp";
        internal const string PrefKeyJsonSchemaLatestVersion   = "PlayServ.JsonSchema.Latest.Version";
        
        internal const string PrefKeyWebSocketEndpoint = "PlayServ.WebSocket.Endpoint";
    }
}
