namespace Playserv.Editor
{
    internal static class PlayServSchemaToolPackage
    {
        public const string PackageId = "com.playserv.schema-tool";
        public const string ToolRelativePath =
            "Tools~/SchemaTool/runtime/PlayServ.Schema.Tool.dll";

        public static readonly PlayServCompanionPackageDefinition Definition =
            new PlayServCompanionPackageDefinition(
                packageId: PackageId,
                moduleId: "editor-schema-tool",
                displayName: "PlayServ Schema Tool",
                description: "External schema analyzer and code generator.",
                gitPath: "CompanionPackages~/com.playserv.schema-tool");
    }
}
