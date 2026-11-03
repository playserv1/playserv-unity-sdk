#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal static class SharedTextFinder
    {
        internal sealed class Binding
        {
            public string OwnerTypeName = "";
            public string MemberName = "";             // property/field name in owner
            public string DeclaredDtoTypeName = "";    // type of property/field (if present)

            public string Key = "";
            public string Selection = "";
            public string RootTypeExpr = "";
            public string GeneratedName = "";
        }

        public static List<Binding> FindBindings(string src)
        {
            var result = new List<Binding>();

            var lines = SplitLines(src);
            string currentType = "";

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();

                // Track current type (class/struct/record)
                if (TryParseTypeName(line, out var typeName))
                {
                    currentType = typeName;
                    continue;
                }

                if (!IsSharedAttributeStart(line))
                    continue;

                var attrText = ConsumeAttributeBlock(lines, ref i);
                if (string.IsNullOrWhiteSpace(attrText))
                    continue;

                // Next non-empty line should be member declaration (property OR field)
                int j = i + 1;
                while (j < lines.Count && string.IsNullOrWhiteSpace(lines[j])) j++;
                if (j >= lines.Count) break;

                var memberLine = lines[j].Trim();

                if (!TryParseMemberDeclaration(memberLine, out var memberName, out var declaredType))
                    continue;

                var binding = ParseSharedArgs(attrText);

                binding.OwnerTypeName = string.IsNullOrWhiteSpace(currentType) ? "GlobalType" : currentType;
                binding.MemberName = memberName;
                binding.DeclaredDtoTypeName = declaredType;

                if (string.IsNullOrWhiteSpace(binding.RootTypeExpr)) continue;
                if (string.IsNullOrWhiteSpace(binding.Selection)) continue;

                result.Add(binding);
            }

            return result;
        }

        private static bool IsSharedAttributeStart(string trimmedLine)
        {
            if (!trimmedLine.StartsWith("[", StringComparison.Ordinal))
                return false;

            // Fast check
            return trimmedLine.Contains("Shared", StringComparison.Ordinal);
        }

        private static Binding ParseSharedArgs(string attr)
        {
            var b = new Binding();

            var open = attr.IndexOf('(');
            var close = attr.LastIndexOf(')');
            var inside = (open >= 0 && close > open) ? attr.Substring(open + 1, close - open - 1) : "";

            var parts = SplitTopLevel(inside);

            // Positional patterns:
            // (typeof(Player), "player", Selection="...")
            // ("player", RootType=typeof(Player), Selection="...")
            if (parts.Count >= 2 && parts[0].TrimStart().StartsWith("typeof(", StringComparison.Ordinal))
            {
                b.RootTypeExpr = parts[0].Trim();
                b.Key = Unquote(parts[1].Trim());
            }
            else if (parts.Count >= 1 && parts[0].TrimStart().StartsWith("\"", StringComparison.Ordinal))
            {
                b.Key = Unquote(parts[0].Trim());
            }

            foreach (var p in parts)
            {
                var t = p.Trim();
                var eq = t.IndexOf('=');
                if (eq <= 0) continue;

                var name = t.Substring(0, eq).Trim();
                var val = t.Substring(eq + 1).Trim();

                if (name == "RootType") b.RootTypeExpr = val;
                if (name == "Selection") b.Selection = Unquote(val);
                if (name == "GeneratedName") b.GeneratedName = Unquote(val);
                if (name == "Key") b.Key = Unquote(val);
            }

            b.Key ??= "";
            b.Selection ??= "";
            b.RootTypeExpr ??= "";
            b.GeneratedName ??= "";

            return b;
        }

        private static bool TryParseTypeName(string line, out string typeName)
        {
            typeName = "";
            string[] keywords = { "class", "struct", "record" };

            foreach (var kw in keywords)
            {
                var idx = line.IndexOf(kw + " ", StringComparison.Ordinal);
                if (idx < 0) continue;

                var rest = line.Substring(idx + kw.Length + 1).Trim();
                var name = TakeIdentifier(rest);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    typeName = name;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Supports:
        ///  - Property: public Foo Bar { get; set; }
        ///  - Field:    private Foo _bar;
        ///  - Field with init: private Foo _bar = default!;
        /// </summary>
        private static bool TryParseMemberDeclaration(string line, out string memberName, out string declaredType)
        {
            memberName = "";
            declaredType = "";

            if (string.IsNullOrWhiteSpace(line))
                return false;

            // Skip methods / local functions
            if (line.Contains("(") && line.Contains(")"))
                return false;

            // Remove trailing comment if any
            var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIdx >= 0)
                line = line.Substring(0, commentIdx).Trim();

            // Property: "... {"
            var braceIdx = line.IndexOf('{');
            if (braceIdx > 0)
            {
                var before = line.Substring(0, braceIdx).Trim();
                return TryParseTypeAndNameFromLeft(before, out memberName, out declaredType);
            }

            // Field: "... ;"
            var semiIdx = line.IndexOf(';');
            if (semiIdx > 0)
            {
                var beforeSemi = line.Substring(0, semiIdx).Trim();

                // Remove initializer: "Foo x = ..." -> "Foo x"
                var eqIdx = beforeSemi.IndexOf('=');
                if (eqIdx > 0)
                    beforeSemi = beforeSemi.Substring(0, eqIdx).Trim();

                return TryParseTypeAndNameFromLeft(beforeSemi, out memberName, out declaredType);
            }

            return false;
        }

        /// <summary>
        /// Parses "modifiers type name" where type may contain dots/generics/arrays.
        /// Example: "private List<InventoryItem> Inventory"
        /// </summary>
        private static bool TryParseTypeAndNameFromLeft(string left, out string name, out string type)
        {
            name = "";
            type = "";

            var tokens = left.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return false;

            // Name is the last token
            name = tokens[^1].Trim();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Find where the type starts (skip modifiers)
            int typeStart = 0;
            while (typeStart < tokens.Length - 1 && IsModifierToken(tokens[typeStart]))
                typeStart++;

            if (typeStart >= tokens.Length - 1)
                return false;

            // Type is everything from typeStart to token before name
            var typeParts = tokens.Skip(typeStart).Take(tokens.Length - 1 - typeStart);
            type = string.Join(" ", typeParts).Trim();

            if (string.IsNullOrWhiteSpace(type))
                return false;

            return true;
        }

        private static bool IsModifierToken(string t)
        {
            switch (t)
            {
                case "public":
                case "private":
                case "protected":
                case "internal":
                case "static":
                case "readonly":
                case "const":
                case "volatile":
                case "new":
                case "virtual":
                case "override":
                case "sealed":
                case "partial":
                case "extern":
                case "unsafe":
                case "abstract":
                    return true;
                default:
                    return false;
            }
        }

        private static string ConsumeAttributeBlock(List<string> lines, ref int i)
        {
            var sb = new StringBuilder();
            var started = false;

            for (; i < lines.Count; i++)
            {
                var s = lines[i].Trim();
                if (!started)
                {
                    if (!s.StartsWith("[", StringComparison.Ordinal)) return "";
                    started = true;
                }

                sb.Append(s);

                if (s.Contains("]"))
                    break;
            }

            return sb.ToString();
        }

        private static List<string> SplitLines(string s)
        {
            return s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        }

        private static List<string> SplitTopLevel(string s)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            bool inString = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                    inString = !inString;

                if (!inString)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        res.Add(sb.ToString());
                        sb.Clear();
                        continue;
                    }
                }

                sb.Append(c);
            }

            if (sb.Length > 0) res.Add(sb.ToString());
            return res;
        }

        private static string Unquote(string s)
        {
            s = s.Trim();

            if (s.StartsWith("@\"", StringComparison.Ordinal) && s.EndsWith("\"", StringComparison.Ordinal))
                return s.Substring(2, s.Length - 3).Replace("\"\"", "\"");

            if (s.StartsWith("\"", StringComparison.Ordinal) && s.EndsWith("\"", StringComparison.Ordinal))
                return s.Substring(1, s.Length - 2);

            return s;
        }

        private static string TakeIdentifier(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    break;
            }
            return sb.ToString();
        }
    }
}
#nullable restore