using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal static class ServerImageEditorStore
    {
        private static string Prefix => "PlayServ.ServerImages." + Hash128.Compute(Path.GetFullPath(Application.dataPath)) + ".";
        internal static string Folder { get => EditorPrefs.GetString(Prefix + "Folder", Path.Combine(Path.GetDirectoryName(Application.dataPath), "server")); set => EditorPrefs.SetString(Prefix + "Folder", value); }
        internal static string Dockerfile { get => EditorPrefs.GetString(Prefix + "Dockerfile", "Dockerfile"); set => EditorPrefs.SetString(Prefix + "Dockerfile", value); }
        internal static string Server { get => EditorPrefs.GetString(Prefix + "Server", ""); set => EditorPrefs.SetString(Prefix + "Server", value); }
        internal static string Tag { get => EditorPrefs.GetString(Prefix + "Tag", ""); set => EditorPrefs.SetString(Prefix + "Tag", value); }
        internal static ServerImagePublication LastPublication
        {
            get { try { return JsonConvert.DeserializeObject<ServerImagePublication>(EditorPrefs.GetString(Prefix + "LastPublication", "")); } catch (JsonException) { return null; } }
            set => EditorPrefs.SetString(Prefix + "LastPublication", JsonConvert.SerializeObject(value));
        }
    }
}
