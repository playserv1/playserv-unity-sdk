#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Playserv.CodeGenerator.Editor
{
    internal static class TypeIndexBuilder
    {
        public static TypeIndex BuildFromAllCsFiles(IEnumerable<string> absCsFiles)
        {
            var dict = new Dictionary<string, TypeInfoModel>(StringComparer.OrdinalIgnoreCase);

            // Keep both short & full enum names (for IsEnum)
            var enums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Map enum short name -> full name (to qualify types like PlayerStatus -> Playserv.Test.PlayerStatus)
            var enumFullNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var abs in absCsFiles)
            {
                var text = File.ReadAllText(abs);

                // Namespace for this file (simple parser)
                var ns = NamespaceParser.ExtractNamespace(text);

                // 1) Enums
                foreach (var en in EnumParser.FindEnums(text))
                {
                    if (string.IsNullOrWhiteSpace(en))
                        continue;

                    // short name
                    enums.Add(en);

                    // full name (if namespace exists)
                    if (!string.IsNullOrWhiteSpace(ns))
                    {
                        var full = ns + "." + en;
                        enums.Add(full);

                        // Only set if not set yet (avoid collisions; first wins)
                        if (!enumFullNames.ContainsKey(en))
                            enumFullNames[en] = full;
                    }
                }

                // 2) Types (class/struct/record)
                foreach (var typeBlock in TypeBlockParser.FindTypeBlocks(text))
                {
                    var typeName = typeBlock.TypeName;
                    if (string.IsNullOrWhiteSpace(typeName))
                        continue;

                    var fullName = string.IsNullOrWhiteSpace(ns) ? typeName : (ns + "." + typeName);

                    var members = MemberParser.ParsePublicMembers(typeBlock.Body);

                    // Lookup by short name
                    dict[typeName] = new TypeInfoModel(typeName, fullName, members);

                    // Lookup by full name too (optional but helpful)
                    dict[fullName] = new TypeInfoModel(typeName, fullName, members);
                }
            }

            return new TypeIndex(dict, enums, enumFullNames);
        }
    }
}
#nullable restore