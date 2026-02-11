#nullable enable
using System;

namespace Playserv.CodeGenerator.Editor
{
    internal static class TypeNameUtil
    {
        public static string ExtractTypeofName(string typeofExpr)
        {
            if (string.IsNullOrWhiteSpace(typeofExpr))
                return "";

            var s = typeofExpr.Trim();
            var start = s.IndexOf("typeof(", StringComparison.Ordinal);
            if (start < 0) return "";

            start += "typeof(".Length;
            var end = s.LastIndexOf(')');
            if (end <= start) return "";

            return s.Substring(start, end - start).Trim();
        }

        public static string TakeIdentifier(string s)
        {
            var sb = new System.Text.StringBuilder();
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

        public static bool IsIdentifier(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;

            for (int i = 1; i < s.Length; i++)
            {
                if (!(char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Normalize type for dictionary lookup: strips '?', array brackets and trims spaces.
        /// Keeps namespace and generic syntax (we handle generics elsewhere).
        /// </summary>
        public static string NormalizeForLookup(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return "";

            var t = typeName.Trim();

            if (t.EndsWith("?", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1);

            if (t.EndsWith("[]", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 2);

            t = t.Replace(" ", "");

            return t;
        }

        public static bool TryGetCollectionElementType(string typeName, out string elementType)
        {
            elementType = "";

            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            var t = typeName.Replace(" ", "");

            // Array: T[]
            if (t.EndsWith("[]", StringComparison.Ordinal))
            {
                elementType = t.Substring(0, t.Length - 2);
                return true;
            }

            // Generic: Outer<Elem>
            var lt = t.IndexOf('<');
            var gt = t.LastIndexOf('>');
            if (lt > 0 && gt > lt)
            {
                var outer = t.Substring(0, lt);
                var inner = t.Substring(lt + 1, gt - lt - 1);

                if (IsKnownCollectionOuter(outer))
                {
                    elementType = inner;
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownCollectionOuter(string outer)
        {
            return outer == "List"
                   || outer == "IList"
                   || outer == "IReadOnlyList"
                   || outer == "IEnumerable"
                   || outer == "ICollection"
                   || outer == "System.Collections.Generic.List"
                   || outer == "System.Collections.Generic.IList"
                   || outer == "System.Collections.Generic.IReadOnlyList"
                   || outer == "System.Collections.Generic.IEnumerable"
                   || outer == "System.Collections.Generic.ICollection";
        }

        public static bool IsKnownCollectionType(string typeName)
        {
            return TryGetCollectionElementType(typeName, out _);
        }

        /// <summary>
        /// Qualify types using TypeIndex:
        /// - leaves primitives/keywords untouched
        /// - supports nullable Foo?, arrays Foo[]
        /// - supports generics (qualifies args)
        /// - NORMALIZES common collections to global::System.Collections.Generic.List&lt;T&gt;
        ///   so generated DTOs compile without requiring "using System.Collections.Generic".
        /// </summary>
        public static string QualifyTypeIfNeeded(string typeName, TypeIndex typeIndex)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return typeName;

            var original = typeName.Trim();

            // Preserve nullable marker for final output
            var isNullable = original.EndsWith("?", StringComparison.Ordinal);
            if (isNullable)
                original = original.Substring(0, original.Length - 1);

            // Array suffix
            var isArray = original.EndsWith("[]", StringComparison.Ordinal);
            if (isArray)
                original = original.Substring(0, original.Length - 2);

            original = original.Replace(" ", "");

            // Generic?
            var lt = original.IndexOf('<');
            var gt = original.LastIndexOf('>');
            if (lt > 0 && gt > lt)
            {
                var outer = original.Substring(0, lt);
                var inner = original.Substring(lt + 1, gt - lt - 1);

                // Normalize collections to global::System.Collections.Generic.List<T>
                if (IsKnownCollectionOuter(outer))
                {
                    var innerQ = QualifyGenericArgs(inner, typeIndex);
                    var rebuilt = "global::System.Collections.Generic.List<" + innerQ + ">";
                    if (isArray) rebuilt += "[]";
                    if (isNullable) rebuilt += "?";
                    return rebuilt;
                }

                // Otherwise, qualify outer and args
                var outerQ = QualifyNonGeneric(outer, typeIndex);
                var innerQualified = QualifyGenericArgs(inner, typeIndex);

                var rebuiltNonCollection = outerQ + "<" + innerQualified + ">";
                if (isArray) rebuiltNonCollection += "[]";
                if (isNullable) rebuiltNonCollection += "?";
                return rebuiltNonCollection;
            }

            // Non-generic
            var nonGen = QualifyNonGeneric(original, typeIndex);
            if (isArray) nonGen += "[]";
            if (isNullable) nonGen += "?";
            return nonGen;
        }

        private static string QualifyNonGeneric(string t, TypeIndex typeIndex)
        {
            if (string.IsNullOrWhiteSpace(t))
                return t;

            // Already qualified; ensure it's "global::" to avoid using issues if you want
            if (t.Contains(".", StringComparison.Ordinal))
            {
                // If it already starts with global::, keep it; else prefix.
                return t.StartsWith("global::", StringComparison.Ordinal) ? t : ("global::" + t);
            }

            if (IsPrimitiveOrKeyword(t))
                return t;

            if (!IsIdentifier(t))
                return t;

            // Ask TypeIndex for full name (types + enums)
            var full = typeIndex.TryGetFullName(t);

            // If known - prefix global::
            if (!string.IsNullOrWhiteSpace(full))
                return "global::" + full!;

            return t;
        }

        private static string QualifyGenericArgs(string args, TypeIndex typeIndex)
        {
            var parts = SplitTopLevelGenericArgs(args);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = QualifyTypeIfNeeded(parts[i], typeIndex);

            return string.Join(", ", parts);
        }

        private static string[] SplitTopLevelGenericArgs(string s)
        {
            // Split by commas at top-level, respecting nested <>
            var list = new System.Collections.Generic.List<string>();
            var sb = new System.Text.StringBuilder();
            int depth = 0;

            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    list.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }

            var last = sb.ToString().Trim();
            if (last.Length > 0)
                list.Add(last);

            return list.ToArray();
        }

        private static bool IsPrimitiveOrKeyword(string t)
        {
            switch (t)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "char":
                case "string":
                case "object":
                case "void":
                case "System.Boolean":
                case "System.Byte":
                case "System.SByte":
                case "System.Int16":
                case "System.UInt16":
                case "System.Int32":
                case "System.UInt32":
                case "System.Int64":
                case "System.UInt64":
                case "System.Single":
                case "System.Double":
                case "System.Decimal":
                case "System.Char":
                case "System.String":
                case "System.Object":
                case "System.Void":
                    return true;
                default:
                    return false;
            }
        }
    }
}
#nullable restore