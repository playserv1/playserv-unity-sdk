using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class PlatformFunctionEditorStore
    {
        private static string Prefix => "PlayServ.PlatformFunctions." + Hash128.Compute(Path.GetFullPath(Application.dataPath)) + ".";
        public static string Api { get => EditorPrefs.GetString(Prefix + "Api", ""); set => EditorPrefs.SetString(Prefix + "Api", value); }
        public static string Folder { get => EditorPrefs.GetString(Prefix + "Folder", Path.Combine(Path.GetDirectoryName(Application.dataPath), "functions")); set => EditorPrefs.SetString(Prefix + "Folder", value); }
        public static string LocalKey { get => EditorPrefs.GetString(Prefix + "Key", ""); set { if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(Prefix + "Key"); else EditorPrefs.SetString(Prefix + "Key", value.Trim()); } }
        public static string EnvironmentKey => Environment.GetEnvironmentVariable("PLAYSERV_API_KEY");
        public static string ResolveKey(string draft) => PlatformFunctionConnection.ResolveKey(EnvironmentKey, draft);
        public static PlatformDeploymentReference LastDeployment
        {
            get
            {
                try { return JsonConvert.DeserializeObject<PlatformDeploymentReference>(EditorPrefs.GetString(Prefix + "LastDeployment", "")); }
                catch (JsonException) { return null; }
            }
            set => EditorPrefs.SetString(Prefix + "LastDeployment", JsonConvert.SerializeObject(value));
        }
    }
}
