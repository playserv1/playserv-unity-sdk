using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal sealed class SimpleCache
    {
        public Dictionary<string, string> FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> FileOutputs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OutputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FileTypeSnapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FileSharedMarkers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static SimpleCache Load(string path)
        {
            var c = new SimpleCache();
            if (!File.Exists(path)) return c;

            string section = "";
            foreach (var line in File.ReadAllLines(path))
            {
                if (line == "[FileHashes]" ||
                    line == "[FileOutputs]" ||
                    line == "[OutputHashes]" ||
                    line == "[FileTypeSnapshots]" ||
                    line == "[FileSharedMarkers]")
                {
                    section = line;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                if (section == "[FileHashes]" ||
                    section == "[OutputHashes]" ||
                    section == "[FileTypeSnapshots]" ||
                    section == "[FileSharedMarkers]")
                {
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var k = line.Substring(0, idx);
                    var v = line.Substring(idx + 1);
                    if (section == "[FileHashes]") c.FileHashes[k] = v;
                    else if (section == "[OutputHashes]") c.OutputHashes[k] = v;
                    else if (section == "[FileTypeSnapshots]") c.FileTypeSnapshots[k] = v;
                    else c.FileSharedMarkers[k] = v;
                }
                else if (section == "[FileOutputs]")
                {
                    var idx = line.IndexOf('|');
                    if (idx <= 0) continue;
                    var k = line.Substring(0, idx);
                    var v = line.Substring(idx + 1);
                    c.FileOutputs[k] = v.Length == 0 ? new List<string>() : v.Split(';').ToList();
                }
            }

            return c;
        }

        public static void Save(string path, SimpleCache c)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();

            sb.AppendLine("[FileHashes]");
            foreach (var kv in c.FileHashes)
                sb.AppendLine(kv.Key + "=" + kv.Value);

            sb.AppendLine("[FileOutputs]");
            foreach (var kv in c.FileOutputs)
                sb.AppendLine(kv.Key + "|" + string.Join(";", kv.Value));

            sb.AppendLine("[OutputHashes]");
            foreach (var kv in c.OutputHashes)
                sb.AppendLine(kv.Key + "=" + kv.Value);

            sb.AppendLine("[FileTypeSnapshots]");
            foreach (var kv in c.FileTypeSnapshots)
                sb.AppendLine(kv.Key + "=" + kv.Value);

            sb.AppendLine("[FileSharedMarkers]");
            foreach (var kv in c.FileSharedMarkers)
                sb.AppendLine(kv.Key + "=" + kv.Value);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
