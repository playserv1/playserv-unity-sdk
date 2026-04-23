#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal static class TypeIndexBuilder
    {
        internal sealed class CachedSourceSnapshot
        {
            public string Namespace = "";
            public List<string> Enums = new List<string>();
            public List<CachedTypeSnapshot> Types = new List<CachedTypeSnapshot>();
        }

        internal sealed class CachedTypeSnapshot
        {
            public string Name = "";
            public string FullName = "";
            public Dictionary<string, string> Members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static TypeIndex BuildFromAllCsFiles(IEnumerable<string> absCsFiles)
        {
            var snapshots = absCsFiles
                .Select(abs => ParseSnapshot(File.ReadAllText(abs)))
                .ToArray();

            return BuildFromSnapshots(snapshots);
        }

        public static CachedSourceSnapshot ParseSnapshot(string text)
        {
            var snapshot = new CachedSourceSnapshot
            {
                Namespace = NamespaceParser.ExtractNamespace(text)
            };

            foreach (var en in EnumParser.FindEnums(text))
            {
                if (!string.IsNullOrWhiteSpace(en))
                    snapshot.Enums.Add(en);
            }

            foreach (var typeBlock in TypeBlockParser.FindTypeBlocks(text))
            {
                var typeName = typeBlock.TypeName;
                if (string.IsNullOrWhiteSpace(typeName))
                    continue;

                snapshot.Types.Add(new CachedTypeSnapshot
                {
                    Name = typeName,
                    FullName = string.IsNullOrWhiteSpace(snapshot.Namespace)
                        ? typeName
                        : snapshot.Namespace + "." + typeName,
                    Members = MemberParser.ParsePublicMembers(typeBlock.Body)
                });
            }

            return snapshot;
        }

        public static TypeIndex BuildFromSnapshots(IEnumerable<CachedSourceSnapshot> snapshots)
        {
            var dict = new Dictionary<string, TypeInfoModel>(StringComparer.OrdinalIgnoreCase);
            var enums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var enumFullNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var typeCandidatesByShortName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var enumCandidatesByShortName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var snapshot in snapshots)
            {
                if (snapshot == null)
                    continue;

                foreach (var en in snapshot.Enums)
                {
                    if (string.IsNullOrWhiteSpace(en))
                        continue;

                    enums.Add(en);

                    if (!string.IsNullOrWhiteSpace(snapshot.Namespace))
                    {
                        var full = snapshot.Namespace + "." + en;
                        enums.Add(full);
                        AddCandidate(enumCandidatesByShortName, en, full);

                        if (!enumFullNames.ContainsKey(en))
                            enumFullNames[en] = full;
                    }
                    else
                    {
                        AddCandidate(enumCandidatesByShortName, en, en);
                    }
                }

                foreach (var type in snapshot.Types)
                {
                    if (string.IsNullOrWhiteSpace(type?.Name))
                        continue;

                    var info = new TypeInfoModel(type.Name, type.FullName, type.Members);
                    AddCandidate(typeCandidatesByShortName, type.Name, type.FullName);
                    dict[type.Name] = info;
                    dict[type.FullName] = info;
                }
            }

            return new TypeIndex(dict, enums, enumFullNames, typeCandidatesByShortName, enumCandidatesByShortName);
        }

        public static string SerializeSnapshot(CachedSourceSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var lines = new List<string>
            {
                "N|" + Encode(snapshot.Namespace)
            };

            lines.Add("E|" + string.Join(";", snapshot.Enums.Select(Encode)));

            foreach (var type in snapshot.Types)
            {
                var members = string.Join(";",
                    type.Members.Select(kv => Encode(kv.Key) + "~" + Encode(kv.Value)));

                lines.Add("T|" + Encode(type.Name) + "|" + Encode(type.FullName) + "|" + members);
            }

            return string.Join("\n", lines);
        }

        public static CachedSourceSnapshot DeserializeSnapshot(string raw)
        {
            var snapshot = new CachedSourceSnapshot();
            if (string.IsNullOrWhiteSpace(raw))
                return snapshot;

            var lines = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("N|", StringComparison.Ordinal))
                {
                    snapshot.Namespace = Decode(line.Substring(2));
                    continue;
                }

                if (line.StartsWith("E|", StringComparison.Ordinal))
                {
                    var payload = line.Substring(2);
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        snapshot.Enums.AddRange(payload
                            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(Decode));
                    }

                    continue;
                }

                if (!line.StartsWith("T|", StringComparison.Ordinal))
                    continue;

                var parts = line.Split(new[] { '|' }, 4);
                if (parts.Length < 4)
                    continue;

                var type = new CachedTypeSnapshot
                {
                    Name = Decode(parts[1]),
                    FullName = Decode(parts[2])
                };

                if (!string.IsNullOrWhiteSpace(parts[3]))
                {
                    foreach (var member in parts[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var memberParts = member.Split(new[] { '~' }, 2);
                        if (memberParts.Length != 2)
                            continue;

                        type.Members[Decode(memberParts[0])] = Decode(memberParts[1]);
                    }
                }

                snapshot.Types.Add(type);
            }

            return snapshot;
        }

        private static string Encode(string value)
        {
            var text = value ?? string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        private static void AddCandidate(
            Dictionary<string, List<string>> map,
            string shortName,
            string fullName)
        {
            if (string.IsNullOrWhiteSpace(shortName) || string.IsNullOrWhiteSpace(fullName))
                return;

            if (!map.TryGetValue(shortName, out var candidates))
            {
                candidates = new List<string>();
                map[shortName] = candidates;
            }

            if (!candidates.Contains(fullName, StringComparer.OrdinalIgnoreCase))
                candidates.Add(fullName);
        }
    }
}
#nullable restore
