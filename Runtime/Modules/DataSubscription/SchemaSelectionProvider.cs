#if UNITY_EDITOR || (!PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS)
using System;
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
using System.IO;
#endif
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.DataSubscription
{
    /// <summary>
    /// Provides the schema text used for automatic data-subscription selections.
    /// </summary>
    public static class SchemaSelectionProvider
    {
        private static readonly object Gate = new object();
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
        private const string CurrentSchemaAssetPath = "Resources/current-schema.json";
        private const string LatestSchemaAssetPath = "Resources/latest-schema.json";
#endif
        private static int _manualVersionStamp;

        /// <summary>
        /// Explicitly resets cached schema selections.
        /// </summary>
        public static void Reset()
        {
            NotifySchemaChanged();
        }

        /// <summary>
        /// Invalidates cached schema selections after editor model/schema sync.
        /// </summary>
        public static void NotifySchemaChanged()
        {
            lock (Gate)
            {
                unchecked
                {
                    _manualVersionStamp++;
                }
            }
        }

        internal static SchemaSelectionSnapshot GetSnapshot()
        {
            var schemaText = LoadSchemaText();
            var contentStamp = ComputeStableHash(schemaText);

            lock (Gate)
            {
                unchecked
                {
                    return new SchemaSelectionSnapshot(schemaText, (contentStamp * 397) ^ _manualVersionStamp);
                }
            }
        }

        private static string LoadSchemaText()
        {
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
            var editorSchemaText = LoadSchemaTextFromAssetFile();
            if (!string.IsNullOrWhiteSpace(editorSchemaText))
                return editorSchemaText;
#endif
#if UNITY_5_3_OR_NEWER
            var current = Resources.Load<TextAsset>("current-schema");
            if (current != null && !string.IsNullOrWhiteSpace(current.text))
                return current.text;

            var latest = Resources.Load<TextAsset>("latest-schema");
            if (latest != null && !string.IsNullOrWhiteSpace(latest.text))
                return latest.text;
#endif
            return string.Empty;
        }

#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
        private static string LoadSchemaTextFromAssetFile()
        {
            var current = TryReadAssetFile(CurrentSchemaAssetPath);
            if (!string.IsNullOrWhiteSpace(current))
                return current;

            return TryReadAssetFile(LatestSchemaAssetPath);
        }

        private static string TryReadAssetFile(string relativeAssetPath)
        {
            var path = Path.Combine(Application.dataPath, relativeAssetPath);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
#endif

        private static int ComputeStableHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            unchecked
            {
                var hash = (int)2166136261;
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }

                return hash;
            }
        }
    }

    internal readonly struct SchemaSelectionSnapshot
    {
        public SchemaSelectionSnapshot(string schemaJson, int versionStamp)
        {
            SchemaJson = schemaJson ?? string.Empty;
            VersionStamp = versionStamp;
        }

        public string SchemaJson { get; }

        public int VersionStamp { get; }

        public bool HasSchema => !string.IsNullOrWhiteSpace(SchemaJson);
    }
}

#endif
