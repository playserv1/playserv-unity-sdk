#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.CodeGenerator.Editor
{
    internal static class DtoDependencyCollector
    {
        public static HashSet<string> CollectDependencies(IEnumerable<SharedTextFinder.Binding> bindings, TypeIndex typeIndex)
        {
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (bindings == null)
                return dependencies;

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                var rootTypeNameRaw = TypeNameUtil.ExtractTypeofName(binding.RootTypeExpr);
                var rootTypeNameQualified = string.IsNullOrWhiteSpace(rootTypeNameRaw)
                    ? string.Empty
                    : TypeNameUtil.QualifyTypeIfNeeded(rootTypeNameRaw, typeIndex);
                var rootTypeLookup = StripGlobalPrefix(rootTypeNameQualified);
                var rootType = string.IsNullOrWhiteSpace(rootTypeLookup) ? null : typeIndex.Find(rootTypeLookup);

                RegisterTypeDependency(rootTypeLookup, typeIndex, dependencies);

                if (string.IsNullOrWhiteSpace(binding.Selection))
                    continue;

                var selection = SelectionParser.Parse(binding.Selection);
                CollectSelectionDependencies(selection, rootType, typeIndex, dependencies);
            }

            return dependencies;
        }

        private static void CollectSelectionDependencies(
            IEnumerable<SelectionParser.SelectionNode> selection,
            TypeInfoModel? rootType,
            TypeIndex typeIndex,
            ISet<string> dependencies)
        {
            if (selection == null)
                return;

            foreach (var node in selection)
            {
                if (node == null)
                    continue;

                var memberTypeName = TryResolveMemberType(rootType, node.Name, out _);
                RegisterTypeDependency(memberTypeName, typeIndex, dependencies);

                if (node.Children.Count == 0)
                    continue;

                var nestedRoot = ResolveNestedRoot(typeIndex, memberTypeName);
                if (nestedRoot != null)
                    dependencies.Add(nestedRoot.FullName);

                CollectSelectionDependencies(node.Children, nestedRoot, typeIndex, dependencies);
            }
        }

        private static void RegisterTypeDependency(string? typeName, TypeIndex typeIndex, ISet<string> dependencies)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return;

            foreach (var token in EnumerateTypeTokens(typeName))
            {
                if (string.IsNullOrWhiteSpace(token) || IsFrameworkType(token))
                    continue;

                var fullName = typeIndex.TryGetFullName(token);
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    dependencies.Add(fullName);
                    continue;
                }

                var stripped = StripGlobalPrefix(token);
                if (!string.IsNullOrWhiteSpace(stripped) && stripped.IndexOf('.', StringComparison.Ordinal) >= 0)
                    dependencies.Add(stripped);
            }
        }

        private static IEnumerable<string> EnumerateTypeTokens(string typeName)
        {
            var normalized = StripGlobalPrefix((typeName ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            if (normalized.EndsWith("?", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);

            if (normalized.EndsWith("[]", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 2);

            normalized = normalized.Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            var lt = normalized.IndexOf('<');
            var gt = normalized.LastIndexOf('>');
            if (lt > 0 && gt > lt)
            {
                var outer = normalized.Substring(0, lt);
                yield return outer;

                var inner = normalized.Substring(lt + 1, gt - lt - 1);
                foreach (var arg in SplitTopLevelGenericArgs(inner))
                {
                    foreach (var nested in EnumerateTypeTokens(arg))
                        yield return nested;
                }

                yield break;
            }

            yield return normalized;
        }

        private static IEnumerable<string> SplitTopLevelGenericArgs(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            var depth = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return text.Substring(start, i - start).Trim();
                    start = i + 1;
                }
            }

            if (start < text.Length)
                yield return text.Substring(start).Trim();
        }

        private static bool IsFrameworkType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return true;

            var t = StripGlobalPrefix(typeName);
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
                case "List":
                case "IList":
                case "IReadOnlyList":
                case "IEnumerable":
                case "ICollection":
                case "System.Collections.Generic.List":
                case "System.Collections.Generic.IList":
                case "System.Collections.Generic.IReadOnlyList":
                case "System.Collections.Generic.IEnumerable":
                case "System.Collections.Generic.ICollection":
                    return true;
                default:
                    return false;
            }
        }

        private static TypeInfoModel? ResolveNestedRoot(TypeIndex typeIndex, string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var t = NormalizeTypeName(typeName);
            if (string.IsNullOrWhiteSpace(t))
                return null;

            return typeIndex.Find(t);
        }

        private static string? TryResolveMemberType(TypeInfoModel? rootType, string memberName, out string resolvedMemberName)
        {
            resolvedMemberName = memberName;

            if (rootType == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            if (rootType.TryResolveMember(memberName, out var realName, out var realType))
            {
                resolvedMemberName = realName;
                return realType;
            }

            var pascal = ToPascalCase(memberName);
            if (!pascal.Equals(memberName, StringComparison.Ordinal) &&
                rootType.TryResolveMember(pascal, out realName, out realType))
            {
                resolvedMemberName = realName;
                return realType;
            }

            return null;
        }

        private static string? NormalizeTypeName(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var t = StripGlobalPrefix(typeName.Trim());

            if (t.EndsWith("?", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1);

            if (t.EndsWith("[]", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 2);

            if (TypeNameUtil.TryGetCollectionElementType(t, out var elem))
                t = elem;

            t = t.Replace(" ", string.Empty);

            var lt = t.IndexOf('<');
            if (lt > 0)
                t = t.Substring(0, lt);

            return t;
        }

        private static string ToPascalCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (text.Length == 1)
                return text.ToUpperInvariant();

            if (char.IsUpper(text[0]))
                return text;

            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static string StripGlobalPrefix(string typeName)
        {
            const string prefix = "global::";
            return !string.IsNullOrWhiteSpace(typeName) &&
                   typeName.StartsWith(prefix, StringComparison.Ordinal)
                ? typeName.Substring(prefix.Length)
                : typeName;
        }
    }
}
#nullable restore

