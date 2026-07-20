namespace Playserv.Serialization
{
#if !PLAYSERV_HAS_NEWTONSOFT_JSON
#warning PlayServ SDK requires com.unity.nuget.newtonsoft-json. Add it to Packages/manifest.json or use Tools/PlayServ/Dependencies/Install Newtonsoft Json.
#endif

    internal static class NewtonsoftJsonDependency
    {
        public const string PackageName = "com.unity.nuget.newtonsoft-json";
        public const string PackageVersion = "3.2.2";

        public static JsonCodecException CreateMissingException()
        {
            return new JsonCodecException(
                "PlayServ SDK requires the Unity Newtonsoft Json package. " +
                "Add \"com.unity.nuget.newtonsoft-json\": \"3.2.2\" to Packages/manifest.json, " +
                "or install PlayServ SDK through Unity Package Manager so dependencies are resolved automatically.");
        }
    }
}
