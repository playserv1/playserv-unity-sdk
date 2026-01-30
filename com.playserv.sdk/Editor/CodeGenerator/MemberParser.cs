#nullable enable
using System;
using System.Collections.Generic;

namespace Playserv.CodeGenerator.Editor
{
    internal static class MemberParser
    {
        public static Dictionary<string, string> ParsePublicMembers(string typeBody)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var lines = typeBody.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (!line.StartsWith("public ", StringComparison.Ordinal))
                    continue;

                // Strip trailing comment
                var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx >= 0)
                    line = line.Substring(0, commentIdx).Trim();

                var braceIdx = line.IndexOf('{');
                var semiIdx = line.IndexOf(';');

                // If '(' occurs before '{' or ';' => method-like, skip.
                // But allow property initializers: "= new();" -> '(' after '=' and usually after '}'.
                var parenIdx = line.IndexOf('(');
                if (parenIdx >= 0)
                {
                    var firstStop = -1;
                    if (braceIdx >= 0 && semiIdx >= 0) firstStop = Math.Min(braceIdx, semiIdx);
                    else if (braceIdx >= 0) firstStop = braceIdx;
                    else if (semiIdx >= 0) firstStop = semiIdx;

                    if (firstStop >= 0 && parenIdx < firstStop)
                        continue;
                }

                // Property: public TYPE NAME { ...
                if (braceIdx > 0)
                {
                    var before = line.Substring(0, braceIdx).Trim();
                    var parts = before.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var type = parts[1];
                        var name = parts[2];
                        if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(name))
                            map[name] = type;
                    }
                    continue;
                }

                // Field: public TYPE NAME;
                if (semiIdx > 0)
                {
                    var before = line.Substring(0, semiIdx).Trim();
                    var parts = before.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var type = parts[1];
                        var name = parts[2];
                        if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(name))
                            map[name] = type;
                    }
                }
            }

            return map;
        }
    }
}
#nullable restore