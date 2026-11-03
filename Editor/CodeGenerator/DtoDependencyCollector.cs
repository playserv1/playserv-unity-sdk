#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.CodeGenerator.Editor
{
    internal sealed class DependencyCollectionResult
    {
        public HashSet<string> Dependencies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Diagnostics { get; } = new List<string>();
    }

    internal static class DtoDependencyCollector
    {
        public static DependencyCollectionResult CollectDependencies(IEnumerable<SharedTextFinder.Binding> bindings, TypeIndex typeIndex)
        {
            var result = new DependencyCollectionResult();
            if (bindings == null)
                return result;

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                var bindingSource = BuildBindingSource(binding);
                var rootTypeNameRaw = TypeNameUtil.ExtractTypeofName(binding.RootTypeExpr);
                var rootTypeLookup = NormalizeTypeName(rootTypeNameRaw) ?? StripGlobalPrefix(rootTypeNameRaw);
                var rootType = ResolveRootType(typeIndex, rootTypeLookup, result, bindingSource);

                RegisterTypeDependency(rootTypeNameRaw, typeIndex, result, bindingSource, "root type");

                if (string.IsNullOrWhiteSpace(binding.Selection))
                    continue;

                if (rootType == null)
                {
                    if (!string.IsNullOrWhiteSpace(rootTypeLookup))
                    {
                        result.Diagnostics.Add(
                            "Dependency graph may be incomplete for '" + bindingSource +
                            "' because root type '" + rootTypeLookup + "' could not be resolved precisely.");
                    }

                    continue;
                }

                var selection = SelectionParser.Parse(binding.Selection);
                CollectSelectionDependencies(selection, rootType, typeIndex, result, bindingSource, string.Empty);
            }

            return result;
        }

        private static void CollectSelectionDependencies(
            IEnumerable<SelectionParser.SelectionNode> selection,
            TypeInfoModel? rootType,
            TypeIndex typeIndex,
            DependencyCollectionResult result,
            string bindingSource,
            string parentPath)
        {
            if (selection == null || rootType == null)
                return;

            foreach (var node in selection)
            {
                if (node == null)
                    continue;

                var currentPath = string.IsNullOrWhiteSpace(parentPath)
                    ? node.Name
                    : parentPath + "." + node.Name;

                var memberTypeName = TryResolveMemberType(rootType, node.Name, out _);
                if (string.IsNullOrWhiteSpace(memberTypeName))
                {
                    result.Diagnostics.Add(
                        "Could not resolve member '" + currentPath + "' on '" + rootType.FullName +
                        "' while collecting dependencies for '" + bindingSource + "'.");
                    continue;
                }

                RegisterTypeDependency(memberTypeName, typeIndex, result, bindingSource, "member '" + currentPath + "'");

                if (node.Children.Count == 0)
                    continue;

                var nestedRoot = ResolveNestedRoot(typeIndex, memberTypeName, result, bindingSource, currentPath);
                if (nestedRoot != null)
                    result.Dependencies.Add(nestedRoot.FullName);

                CollectSelectionDependencies(node.Children, nestedRoot, typeIndex, result, bindingSource, currentPath);
            }
        }

        private static void RegisterTypeDependency(
            string? typeName,
            TypeIndex typeIndex,
            DependencyCollectionResult result,
            string bindingSource,
            string context)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return;

            foreach (var token in EnumerateTypeTokens(typeName))
            {
                if (string.IsNullOrWhiteSpace(token) || IsFrameworkType(token))
                    continue;

                var resolution = typeIndex.ResolveReference(token);
                if (resolution.IsResolved)
                {
                    if (!string.IsNullOrWhiteSpace(resolution.FullName))
                        result.Dependencies.Add(resolution.FullName);
                    continue;
                }

                if (resolution.IsAmbiguous)
                {
                    foreach (var candidate in resolution.Candidates)
                        result.Dependencies.Add(candidate);

                    result.Diagnostics.Add(
                        "Ambiguous type reference '" + token + "' in " + context + " for '" + bindingSource +
                        "'. Candidates: " + string.Join(", ", resolution.Candidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) + ".");
                    continue;
                }

                var stripped = StripGlobalPrefix(token);
                if (!string.IsNullOrWhiteSpace(stripped) && stripped.IndexOf('.', StringComparison.Ordinal) >= 0)
                {
                    result.Dependencies.Add(stripped);
                    result.Diagnostics.Add(
                        "Unverified qualified type reference '" + stripped + "' in " + context + " for '" + bindingSource +
                        "'. It was kept as a best-effort dependency.");
                    continue;
                }

                result.Diagnostics.Add(
                    "Unresolved type reference '" + token + "' in " + context + " for '" + bindingSource +
                    "'. Dependency graph may be incomplete.");
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
                case "List":
                case "IList":
                case "IReadOnlyList":
                case "IEnumerable":
                case "ICollection":
                case "Dictionary":
                case "IDictionary":
                case "IReadOnlyDictionary":
                case "HashSet":
                case "ISet":
                case "Queue":
                case "Stack":
                case "KeyValuePair":
                case "Nullable":
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
                case "System.Nullable":
                case "System.Collections.Generic.List":
                case "System.Collections.Generic.IList":
                case "System.Collections.Generic.IReadOnlyList":
                case "System.Collections.Generic.IEnumerable":
                case "System.Collections.Generic.ICollection":
                case "System.Collections.Generic.Dictionary":
                case "System.Collections.Generic.IDictionary":
                case "System.Collections.Generic.IReadOnlyDictionary":
                case "System.Collections.Generic.HashSet":
                case "System.Collections.Generic.ISet":
                case "System.Collections.Generic.Queue":
                case "System.Collections.Generic.Stack":
                case "System.Collections.Generic.KeyValuePair":
                    return true;
                default:
                    return false;
            }
        }

        private static TypeInfoModel? ResolveRootType(
            TypeIndex typeIndex,
            string? typeName,
            DependencyCollectionResult result,
            string bindingSource)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var resolution = typeIndex.ResolveReference(typeName);
            if (resolution.IsResolved)
                return string.IsNullOrWhiteSpace(resolution.FullName) ? null : typeIndex.Find(resolution.FullName);

            if (resolution.IsAmbiguous)
            {
                foreach (var candidate in resolution.Candidates)
                    result.Dependencies.Add(candidate);

                result.Diagnostics.Add(
                    "Ambiguous root type '" + typeName + "' for '" + bindingSource +
                    "'. Candidates: " + string.Join(", ", resolution.Candidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) + ".");
            }

            return null;
        }

        private static TypeInfoModel? ResolveNestedRoot(
            TypeIndex typeIndex,
            string? typeName,
            DependencyCollectionResult result,
            string bindingSource,
            string memberPath)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var normalized = NormalizeTypeName(typeName);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var resolution = typeIndex.ResolveReference(normalized);
            if (resolution.IsResolved)
                return string.IsNullOrWhiteSpace(resolution.FullName) ? null : typeIndex.Find(resolution.FullName);

            if (resolution.IsAmbiguous)
            {
                foreach (var candidate in resolution.Candidates)
                    result.Dependencies.Add(candidate);

                result.Diagnostics.Add(
                    "Ambiguous nested type '" + normalized + "' for member '" + memberPath +
                    "' in '" + bindingSource + "'. Candidates: " +
                    string.Join(", ", resolution.Candidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) + ".");
                return null;
            }

            if (!IsFrameworkType(normalized))
            {
                result.Diagnostics.Add(
                    "Could not resolve nested type '" + normalized + "' for member '" + memberPath +
                    "' in '" + bindingSource + "'.");
            }

            return null;
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

        private static string BuildBindingSource(SharedTextFinder.Binding binding)
        {
            var owner = string.IsNullOrWhiteSpace(binding.OwnerTypeName) ? "UnknownOwner" : binding.OwnerTypeName;
            var member = string.IsNullOrWhiteSpace(binding.MemberName) ? "UnknownMember" : binding.MemberName;
            return owner + "." + member;
        }
    }
}
#nullable restore
