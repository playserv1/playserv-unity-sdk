using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playserv.DataSubscription.Exceptions;

namespace Playserv.DataSubscription.JsonPatch
{
    /// <summary>
    /// Applies JSON patch operations to typed objects.
    /// </summary>
    public static class JsonPatchApplier
    {
        /// <summary>
        /// Applies parsed patch operations to target object.
        /// </summary>
        /// <typeparam name="T">Target object type.</typeparam>
        /// <param name="target">Current object instance.</param>
        /// <param name="operations">Patch operations sequence.</param>
        /// <returns>Patched object instance.</returns>
        public static T ApplyPatch<T>(T target, IEnumerable<PatchOperation> operations) where T : class
        {
            var json = JObject.FromObject(target);

            foreach (var operation in operations)
            {
                ApplyOperation(json, operation);
            }

            return json.ToObject<T>();
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
            var operations = ParseOperations(patchData);
            return ApplyPatch(target, operations);
        }

        private static IEnumerable<PatchOperation> ParseOperations(object patchData)
        {
            if (patchData is IEnumerable<PatchOperation> ops)
                return ops;

            var json = patchData is string str ? str : JsonConvert.SerializeObject(patchData);
            return JsonConvert.DeserializeObject<List<PatchOperation>>(json);
        }

        private static void ApplyOperation(JObject target, PatchOperation operation)
        {
            var path = NormalizePath(operation.Path);
            var segments = ParsePath(path);

            switch (operation.Op?.ToLowerInvariant())
            {
                case "add":
                    ApplyAdd(target, segments, operation.Value);
                    break;
                case "remove":
                    ApplyRemove(target, segments);
                    break;
                case "replace":
                    ApplyReplace(target, segments, operation.Value);
                    break;
                case "move":
                    ApplyMove(target, segments, operation.From);
                    break;
                case "copy":
                    ApplyCopy(target, segments, operation.From);
                    break;
                case "test":
                    ApplyTest(target, segments, operation.Value);
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

        private static JToken NavigateToParent(JObject root, string[] segments, out string lastSegment)
        {
            lastSegment = segments.LastOrDefault();
            JToken current = root;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                current = NavigateSegment(current, segments[i]);
            }

            return current;
        }

        private static JToken NavigateSegment(JToken current, string segment)
        {
            if (current is JObject obj)
            {
                if (!obj.TryGetValue(segment, out var value))
                    throw new UpdateDataCorruptionException($"Path not found: {segment}");
                return value;
            }

            if (current is JArray arr)
            {
                if (!int.TryParse(segment, out var index) || index < 0 || index >= arr.Count)
                    throw new UpdateDataCorruptionException($"Invalid array index: {segment}");
                return arr[index];
            }

            throw new UpdateDataCorruptionException($"Cannot navigate path segment: {segment}");
        }

        private static void ApplyAdd(JObject root, string[] segments, object value)
        {
            if (segments.Length == 0)
                throw new UpdateDataCorruptionException("Cannot add to root");

            var parent = NavigateToParent(root, segments, out var lastSegment);
            var jValue = value is JToken jt ? jt : JToken.FromObject(value ?? JValue.CreateNull());

            if (parent is JObject obj)
            {
                obj[lastSegment] = jValue;
            }
            else if (parent is JArray arr)
            {
                if (lastSegment == "-")
                {
                    arr.Add(jValue);
                }
                else if (int.TryParse(lastSegment, out var index))
                {
                    arr.Insert(index, jValue);
                }
                else
                {
                    throw new UpdateDataCorruptionException($"Invalid array index: {lastSegment}");
                }
            }
        }

        private static void ApplyRemove(JObject root, string[] segments)
        {
            if (segments.Length == 0)
                throw new UpdateDataCorruptionException("Cannot remove root");

            var parent = NavigateToParent(root, segments, out var lastSegment);

            if (parent is JObject obj)
            {
                obj.Remove(lastSegment);
            }
            else if (parent is JArray arr && int.TryParse(lastSegment, out var index))
            {
                arr.RemoveAt(index);
            }
        }

        private static void ApplyReplace(JObject root, string[] segments, object value)
        {
            if (segments.Length == 0)
                throw new UpdateDataCorruptionException("Cannot replace root");

            var parent = NavigateToParent(root, segments, out var lastSegment);
            var jValue = value is JToken jt ? jt : JToken.FromObject(value ?? JValue.CreateNull());

            if (parent is JObject obj)
            {
                if (!obj.ContainsKey(lastSegment))
                    throw new UpdateDataCorruptionException($"Property not found for replace: {lastSegment}");
                obj[lastSegment] = jValue;
            }
            else if (parent is JArray arr && int.TryParse(lastSegment, out var index))
            {
                if (index < 0 || index >= arr.Count)
                    throw new UpdateDataCorruptionException($"Array index out of bounds: {index}");
                arr[index] = jValue;
            }
        }

        private static void ApplyMove(JObject root, string[] toSegments, string from)
        {
            var fromSegments = ParsePath(NormalizePath(from));
            var parent = NavigateToParent(root, fromSegments, out var lastSegment);

            JToken value;
            if (parent is JObject obj)
            {
                value = obj[lastSegment];
                obj.Remove(lastSegment);
            }
            else if (parent is JArray arr && int.TryParse(lastSegment, out var index))
            {
                value = arr[index];
                arr.RemoveAt(index);
            }
            else
            {
                throw new UpdateDataCorruptionException($"Cannot move from path: {from}");
            }

            ApplyAdd(root, toSegments, value);
        }

        private static void ApplyCopy(JObject root, string[] toSegments, string from)
        {
            var fromSegments = ParsePath(NormalizePath(from));
            var current = (JToken)root;

            foreach (var segment in fromSegments)
            {
                current = NavigateSegment(current, segment);
            }

            ApplyAdd(root, toSegments, current.DeepClone());
        }

        private static void ApplyTest(JObject root, string[] segments, object expectedValue)
        {
            var current = (JToken)root;

            foreach (var segment in segments)
            {
                current = NavigateSegment(current, segment);
            }

            var expected = expectedValue is JToken jt ? jt : JToken.FromObject(expectedValue ?? JValue.CreateNull());
            if (!JToken.DeepEquals(current, expected))
            {
                throw new UpdateDataCorruptionException("Test operation failed - value mismatch");
            }
        }
    }
}
