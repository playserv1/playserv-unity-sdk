using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Playserv.Editor
{
    internal sealed class PlayServLocalTokenStore
    {
        internal static readonly PlayServLocalTokenStore Instance = new PlayServLocalTokenStore(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PlayServ", "Tokens")));

        private readonly string _directory;
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        private readonly HashSet<string> _failedEdits = new HashSet<string>();

        internal PlayServLocalTokenStore(string directory) => _directory = Path.GetFullPath(directory);

        internal string BackupPath(string key) => Path.Combine(_directory, Hash128.Compute(key) + ".json");
        internal string Error(string key) => _errors.TryGetValue(key, out var message) ? message : null;

        internal bool TryGet(string key, out string value)
        {
            value = string.Empty;
            if (EditorPrefs.HasKey(key))
            {
                value = EditorPrefs.GetString(key, string.Empty);
                // Existing preferences remain authoritative; a backup never overwrites them.
                try
                {
                    var matches = false;
                    try { matches = TryReadBackup(key, out var saved) && saved == value; }
                    catch (Exception exception) when (IsStorageError(exception)) { }
                    if (!matches) WriteBackup(key, value);
                    ClearReadError(key);
                }
                catch (Exception exception) when (IsStorageError(exception))
                {
                    SetReadError(key, "The local token is available, but its Library backup could not be saved. Check Library/PlayServ permissions.");
                }
                return true;
            }

            try
            {
                if (!TryReadBackup(key, out value))
                {
                    ClearReadError(key);
                    return false;
                }
                EditorPrefs.SetString(key, value);
                ClearReadError(key);
                return true;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                value = string.Empty;
                SetReadError(key, "The Library token backup could not be restored. Re-enter the token or restore a valid backup.");
                return false;
            }
        }

        internal bool TrySet(string key, string value)
        {
            try
            {
                // Commit the backup before acknowledging an edit, including an explicit clear.
                WriteBackup(key, value ?? string.Empty);
                EditorPrefs.SetString(key, value ?? string.Empty);
                _failedEdits.Remove(key);
                _errors.Remove(key);
                return true;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                _failedEdits.Add(key);
                _errors[key] = "Token change was not saved. Check Library/PlayServ permissions and try again. The previous token was kept.";
                return false;
            }
        }

        private void ClearReadError(string key)
        {
            if (!_failedEdits.Contains(key)) _errors.Remove(key);
        }

        private void SetReadError(string key, string message)
        {
            if (!_failedEdits.Contains(key)) _errors[key] = message;
        }

        private bool TryReadBackup(string key, out string value)
        {
            value = string.Empty;
            var path = BackupPath(key);
            if (!File.Exists(path)) return false;
            var record = JsonUtility.FromJson<TokenRecord>(File.ReadAllText(path));
            if (record == null || record.schemaVersion != 1 || record.key != key || record.value == null)
                throw new InvalidDataException("Invalid local token backup.");
            value = record.value;
            return true;
        }

        private void WriteBackup(string key, string value)
        {
            Directory.CreateDirectory(_directory);
            var path = BackupPath(key);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var json = JsonUtility.ToJson(new TokenRecord { schemaVersion = 1, key = key, value = value });
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static bool IsStorageError(Exception exception) => exception is IOException || exception is InvalidDataException ||
            exception is UnauthorizedAccessException || exception is ArgumentException ||
            exception is NotSupportedException || exception is System.Security.SecurityException;

        [Serializable]
        private sealed class TokenRecord
        {
            public int schemaVersion;
            public string key;
            public string value;
        }
    }
}
