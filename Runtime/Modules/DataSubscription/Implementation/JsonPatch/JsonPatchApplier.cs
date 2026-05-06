using System;
using System.Collections.Generic;
using Playserv.DataSubscription.Exceptions;
using Playserv.Serialization;

namespace Playserv.DataSubscription.JsonPatch
{
    /// <summary>
    /// Applies JSON patch operations to typed objects.
    /// </summary>
    public static class JsonPatchApplier
    {
        private static readonly IJsonCodec DefaultJsonCodec = new NewtonsoftJsonCodec();

        /// <summary>
        /// Applies parsed patch operations to target object.
        /// </summary>
        /// <typeparam name="T">Target object type.</typeparam>
        /// <param name="target">Current object instance.</param>
        /// <param name="operations">Patch operations sequence.</param>
        /// <returns>Patched object instance.</returns>
        public static T ApplyPatch<T>(T target, IEnumerable<PatchOperation> operations) where T : class
        {
            return ApplyPatch(target, operations, null);
        }

        internal static T ApplyPatch<T>(T target, IEnumerable<PatchOperation> operations, IJsonCodec jsonCodec) where T : class
        {
            var codec = ResolveJsonCodec(jsonCodec);
            var root = RequireObjectDocument(codec.ToPlainValue(target));

            foreach (var operation in NormalizeOperations(operations, codec))
            {
                ApplyOperation(root, operation, codec);
            }

            return codec.Convert<T>(root);
        }

        /// <summary>
        /// Applies patch payload (JSON string/object/list) to target object.
        /// </summary>
        /// <typeparam name="T">Target object type.</typeparam>
        /// <param name="target">Current object instance.</param>
        /// <param name="patchData">Patch payload.</param>
        /// <returns>Patched object instance.</returns>
        public static T ApplyPatch<T>(T target, object patchData) where T : class
        {
            return ApplyPatch(target, patchData, null);
        }

        internal static T ApplyPatch<T>(T target, object patchData, IJsonCodec jsonCodec) where T : class
        {
            var codec = ResolveJsonCodec(jsonCodec);
            var operations = ParseOperations(patchData, codec);
            return ApplyPatch(target, operations, codec);
        }

        private static IJsonCodec ResolveJsonCodec(IJsonCodec jsonCodec)
        {
            return jsonCodec ?? DefaultJsonCodec;
        }

        private static IEnumerable<PatchOperation> ParseOperations(object patchData, IJsonCodec jsonCodec)
        {
            if (patchData is IEnumerable<PatchOperation> ops)
                return NormalizeOperations(ops, jsonCodec);

            if (patchData is string json)
                return NormalizeOperations(jsonCodec.Deserialize<List<PatchOperation>>(json), jsonCodec);

            return NormalizeOperations(jsonCodec.Convert<List<PatchOperation>>(patchData), jsonCodec);
        }

        private static IEnumerable<PatchOperation> NormalizeOperations(IEnumerable<PatchOperation> operations, IJsonCodec jsonCodec)
        {
            if (operations == null)
                return Array.Empty<PatchOperation>();

            var normalized = new List<PatchOperation>();
            foreach (var operation in operations)
            {
                if (operation == null)
                    continue;

                normalized.Add(new PatchOperation
                {
                    Op = operation.Op,
                    Path = operation.Path,
                    From = operation.From,
                    Value = jsonCodec.ToPlainValue(operation.Value)
                });
            }

            return normalized;
        }

        private static IDictionary<string, object> RequireObjectDocument(object value)
        {
            if (value is IDictionary<string, object> document)
                return document;

            throw new UpdateDataCorruptionException("Patch target must be a JSON object.");
        }

        private static void ApplyOperation(IDictionary<string, object> target, PatchOperation operation, IJsonCodec jsonCodec)
        {
            var path = NormalizePath(operation.Path);
            var segments = ParsePath(path);

            switch (operation.Op == null ? string.Empty : operation.Op.ToLowerInvariant())
            {
                case "add":
                    ApplyAdd(target, segments, operation.Value, jsonCodec);
                    break;
                case "remove":
                    ApplyRemove(target, segments);
                    break;
                case "replace":
                    ApplyReplace(target, segments, operation.Value, jsonCodec);
                    break;
                case "move":
                    ApplyMove(target, segments, operation.From, jsonCodec);
                    break;
                case "copy":
                    ApplyCopy(target, segments, operation.From, jsonCodec);
                    break;
                case "test":
                    ApplyTest(target, segments, operation.Value, jsonCodec);
                    break;
                default:
                    throw new UpdateDataCorruptionException($"Unknown patch operation: {operation.Op}");
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return path.StartsWith("/") ? path.Substring(1) : path;
        }

        private static string[] ParsePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Array.Empty<string>();

            return path.Split('/');
        }

        private static object NavigateToParent(IDictionary<string, object> root, string[] segments, out string lastSegment)
        {
            lastSegment = segments.Length == 0 ? string.Empty : segments[segments.Length - 1];
            object current = root;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                current = NavigateSegment(current, segments[i]);
            }

            return current;
        }

        private static object NavigateSegment(object current, string segment)
        {
            if (current is IDictionary<string, object> obj)
            {
                if (!obj.TryGetValue(segment, out var value))
                    throw new UpdateDataCorruptionException($"Path not found: {segment}");

                return value;
            }

            if (current is IList<object> arr)
            {
                var index = ParseExistingIndex(segment, arr.Count);
                return arr[index];
            }

            throw new UpdateDataCorruptionException($"Cannot navigate path segment: {segment}");
        }

        private static void ApplyAdd(IDictionary<string, object> root, string[] segments, object value, IJsonCodec jsonCodec)
        {
            if (segments.Length == 0)
                throw new UpdateDataCorruptionException("Cannot add to root");

            var parent = NavigateToParent(root, segments, out var lastSegment);
            var clonedValue = jsonCodec.Clone(value);

            if (parent is IDictionary<string, object> obj)
            {
                obj[lastSegment] = clonedValue;
                return;
            }

            if (parent is IList<object> arr)
            {
                if (lastSegment == "-")
                {
                    arr.Add(clonedValue);
                    return;
                }

                arr.Insert(ParseInsertIndex(lastSegment, arr.Count), clonedValue);
                return;
            }

            throw new UpdateDataCorruptionException($"Cannot add value at path segment: {lastSegment}");
        }

        private static void ApplyRemove(IDictionary<string, object> root, string[] segments)
        {
            if (segments.Length == 0)
                throw new UpdateDataCorruptionException("Cannot remove root");

            var parent = NavigateToParent(root, segments, out var lastSegment);

            if (parent is IDictionary<string, object> obj)
            {
                if (!obj.Remove(lastSegment))
                    throw new UpdateDataCorruptionException($"Property not found for remove: {lastSegment}");
                return;
            }

            if (parent is IList<object> arr)
            {
                arr.RemoveAt(ParseExistingIndex(lastSegment, arr.Count));
                return;
            }

            throw new UpdateDataCorruptionException($"Cannot remove value at path segment: {lastSegment}");
        }

        private static void ApplyReplace(IDictionary<string, object> root, string[] segments, object value, IJsonCodec jsonCodec)
        {
            if (segments.Length == 0)
                throw new UpdateDataCorruptionException("Cannot replace root");

            var parent = NavigateToParent(root, segments, out var lastSegment);
            var clonedValue = jsonCodec.Clone(value);

            if (parent is IDictionary<string, object> obj)
            {
                if (!obj.ContainsKey(lastSegment))
                    throw new UpdateDataCorruptionException($"Property not found for replace: {lastSegment}");

                obj[lastSegment] = clonedValue;
                return;
            }

            if (parent is IList<object> arr)
            {
                var index = ParseExistingIndex(lastSegment, arr.Count);
                arr[index] = clonedValue;
                return;
            }

            throw new UpdateDataCorruptionException($"Cannot replace value at path segment: {lastSegment}");
        }

        private static void ApplyMove(IDictionary<string, object> root, string[] toSegments, string from, IJsonCodec jsonCodec)
        {
            var fromSegments = ParsePath(NormalizePath(from));
            var parent = NavigateToParent(root, fromSegments, out var lastSegment);
            var value = ExtractAndRemoveValue(parent, lastSegment);
            ApplyAdd(root, toSegments, value, jsonCodec);
        }

        private static void ApplyCopy(IDictionary<string, object> root, string[] toSegments, string from, IJsonCodec jsonCodec)
        {
            var fromSegments = ParsePath(NormalizePath(from));
            object current = root;

            for (int i = 0; i < fromSegments.Length; i++)
            {
                current = NavigateSegment(current, fromSegments[i]);
            }

            ApplyAdd(root, toSegments, jsonCodec.Clone(current), jsonCodec);
        }

        private static void ApplyTest(IDictionary<string, object> root, string[] segments, object expectedValue, IJsonCodec jsonCodec)
        {
            object current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                current = NavigateSegment(current, segments[i]);
            }

            var actualJson = jsonCodec.ToCanonicalJson(current);
            var expectedJson = jsonCodec.ToCanonicalJson(jsonCodec.ToPlainValue(expectedValue));
            if (!string.Equals(actualJson, expectedJson, StringComparison.Ordinal))
                throw new UpdateDataCorruptionException("Test operation failed - value mismatch");
        }

        private static object ExtractAndRemoveValue(object parent, string lastSegment)
        {
            if (parent is IDictionary<string, object> obj)
            {
                if (!obj.TryGetValue(lastSegment, out var value))
                    throw new UpdateDataCorruptionException($"Property not found for move: {lastSegment}");

                obj.Remove(lastSegment);
                return value;
            }

            if (parent is IList<object> arr)
            {
                var index = ParseExistingIndex(lastSegment, arr.Count);
                var value = arr[index];
                arr.RemoveAt(index);
                return value;
            }

            throw new UpdateDataCorruptionException($"Cannot move from path segment: {lastSegment}");
        }

        private static int ParseExistingIndex(string segment, int count)
        {
            if (!int.TryParse(segment, out var index) || index < 0 || index >= count)
                throw new UpdateDataCorruptionException($"Invalid array index: {segment}");

            return index;
        }

        private static int ParseInsertIndex(string segment, int count)
        {
            if (!int.TryParse(segment, out var index) || index < 0 || index > count)
                throw new UpdateDataCorruptionException($"Invalid array index: {segment}");

            return index;
        }
    }
}
