#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Playserv.Serialization;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.DataSubscription
{
    internal static class QueryBuilder
    {
        private const int MaxSelectionDepth = 6;
        private static readonly object SchemaGate = new object();
        private static readonly IJsonCodec JsonCodec = new NewtonsoftJsonCodec();
        private static bool _schemaInitialized;
        private static readonly Dictionary<string, IDictionary<string, object>> SchemaDefinitions =
            new Dictionary<string, IDictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        public static string BuildQuery<T>(string entityType, object key, Expression<Func<T, object>> selector = null)
        {
            var sb = new StringBuilder();
            sb.Append(entityType);
            sb.Append("(id: $id)");

            var selection = selector == null
                ? BuildAutoSelection(typeof(T), entityType)
                : BuildSelectionFromExpression(selector.Body);

            if (string.IsNullOrWhiteSpace(selection))
                return sb.ToString();

            sb.Append(" { ");
            sb.Append(selection);
            sb.Append(" }");

            return sb.ToString();
        }

        private static string BuildAutoSelection(Type modelType, string entityType)
        {
            var schemaSelection = TryBuildSelectionFromSchema(entityType);
            if (!string.IsNullOrWhiteSpace(schemaSelection))
                return schemaSelection;

            return BuildSelectionFromType(modelType, 0, new HashSet<Type>());
        }

        private static string BuildSelectionFromExpression(Expression expression)
        {
            var selectionBuilder = new StringBuilder();
            BuildSelection(expression, selectionBuilder);
            return selectionBuilder.ToString().Trim();
        }

        private static void BuildSelection(Expression expression, StringBuilder sb)
        {
            switch (expression)
            {
                case MemberExpression member:
                    sb.Append(member.Member.Name);
                    break;

                case NewExpression newExpr:
                    var first = true;
                    foreach (var arg in newExpr.Arguments)
                    {
                        if (!first) sb.Append(" ");
                        BuildSelection(arg, sb);
                        first = false;
                    }
                    break;

                case MemberInitExpression init:
                    first = true;
                    foreach (var binding in init.Bindings)
                    {
                        if (binding is MemberAssignment assignment)
                        {
                            if (!first) sb.Append(" ");
                            sb.Append(assignment.Member.Name);
                            if (assignment.Expression is MemberExpression me)
                            {
                                sb.Append(" { ");
                                BuildSelection(me, sb);
                                sb.Append(" }");
                            }
                            first = false;
                        }
                    }
                    break;

                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert:
                    BuildSelection(unary.Operand, sb);
                    break;

                default:
                    break;
            }
        }

        private static string BuildSelectionFromType(Type type, int depth, HashSet<Type> path)
        {
            var normalizedType = NormalizeType(type);
            if (IsScalar(normalizedType))
                return string.Empty;

            if (depth >= MaxSelectionDepth || path.Contains(normalizedType))
                return string.Empty;

            path.Add(normalizedType);

            var selections = new List<string>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in normalizedType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                if (!usedNames.Add(property.Name))
                    continue;

                var fieldSelection = BuildFieldSelection(property.Name, property.PropertyType, depth, path);
                if (!string.IsNullOrWhiteSpace(fieldSelection))
                    selections.Add(fieldSelection);
            }

            foreach (var field in normalizedType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsStatic)
                    continue;

                if (!usedNames.Add(field.Name))
                    continue;

                var fieldSelection = BuildFieldSelection(field.Name, field.FieldType, depth, path);
                if (!string.IsNullOrWhiteSpace(fieldSelection))
                    selections.Add(fieldSelection);
            }

            path.Remove(normalizedType);
            return string.Join(" ", selections);
        }

        private static string BuildFieldSelection(string fieldName, Type memberType, int depth, HashSet<Type> path)
        {
            var normalizedType = NormalizeType(memberType);
            if (IsScalar(normalizedType))
                return fieldName;

            var nested = BuildSelectionFromType(normalizedType, depth + 1, new HashSet<Type>(path));
            if (string.IsNullOrWhiteSpace(nested))
                return fieldName;

            return $"{fieldName} {{ {nested} }}";
        }

        private static Type NormalizeType(Type type)
        {
            if (type == null)
                return typeof(object);

            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                type = nullable;

            if (type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type))
            {
                if (type.IsArray)
                    return NormalizeType(type.GetElementType() ?? typeof(object));

                if (type.IsGenericType)
                {
                    var args = type.GetGenericArguments();
                    if (args.Length > 0)
                        return NormalizeType(args[0]);
                }
            }

            return type;
        }

        private static bool IsScalar(Type type)
        {
            if (type.IsPrimitive || type.IsEnum)
                return true;

            return type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(Guid) ||
                   type == typeof(TimeSpan);
        }

        private static string TryBuildSelectionFromSchema(string entityType)
        {
            if (string.IsNullOrWhiteSpace(entityType))
                return string.Empty;

            EnsureSchemaDefinitionsLoaded();
            if (SchemaDefinitions.Count == 0)
                return string.Empty;

            if (!SchemaDefinitions.TryGetValue(entityType, out var entitySchema))
                return string.Empty;

            return BuildSelectionFromSchemaNode(entitySchema, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static string BuildSelectionFromSchemaNode(
            IDictionary<string, object> node,
            int depth,
            HashSet<string> visitedRefs)
        {
            if (node == null || depth >= MaxSelectionDepth)
                return string.Empty;

            if (!TryGetObject(node, "properties", out var properties) || properties.Count == 0)
                return string.Empty;

            var selections = new List<string>();

            foreach (var property in properties)
            {
                var fieldName = property.Key == null ? string.Empty : property.Key.Trim();
                if (string.IsNullOrWhiteSpace(fieldName))
                    continue;

                var nestedSchema = ResolveNestedSchema(property.Value, new HashSet<string>(visitedRefs, StringComparer.OrdinalIgnoreCase));
                if (nestedSchema == null)
                {
                    selections.Add(fieldName);
                    continue;
                }

                var nestedSelection = BuildSelectionFromSchemaNode(
                    nestedSchema,
                    depth + 1,
                    new HashSet<string>(visitedRefs, StringComparer.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(nestedSelection))
                    selections.Add(fieldName);
                else
                    selections.Add($"{fieldName} {{ {nestedSelection} }}");
            }

            return string.Join(" ", selections);
        }

        private static IDictionary<string, object> ResolveNestedSchema(object token, HashSet<string> visitedRefs)
        {
            if (!TryAsObject(token, out var node))
                return null;

            if (TryGetString(node, "$ref", out var reference))
                return ResolveReference(reference, visitedRefs);

            if (IsSchemaType(node, "array"))
            {
                if (TryGetValue(node, "items", out var items))
                    return ResolveNestedSchema(items, visitedRefs);

                return null;
            }

            if (IsSchemaType(node, "object") && TryGetObject(node, "properties", out _))
                return node;

            return null;
        }

        private static IDictionary<string, object> ResolveReference(string reference, HashSet<string> visitedRefs)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            var key = ExtractRefKey(reference);
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (!visitedRefs.Add(key))
                return null;

            return SchemaDefinitions.TryGetValue(key, out var schema) ? schema : null;
        }

        private static string ExtractRefKey(string reference)
        {
            var idx = reference.LastIndexOf('/');
            if (idx < 0 || idx >= reference.Length - 1)
                return reference.Trim();

            return reference.Substring(idx + 1).Trim();
        }

        private static void EnsureSchemaDefinitionsLoaded()
        {
            if (_schemaInitialized)
                return;

            lock (SchemaGate)
            {
                if (_schemaInitialized)
                    return;

                try
                {
#if UNITY_5_3_OR_NEWER
                    var schemaText = LoadSchemaTextFromResources();
                    if (!string.IsNullOrWhiteSpace(schemaText))
                        BuildSchemaIndex(schemaText);
#endif
                }
                catch
                {
                    SchemaDefinitions.Clear();
                }
                finally
                {
                    _schemaInitialized = true;
                }
            }
        }

#if UNITY_5_3_OR_NEWER
        private static string LoadSchemaTextFromResources()
        {
            var current = Resources.Load<TextAsset>("current-schema");
            if (current != null && !string.IsNullOrWhiteSpace(current.text))
                return current.text;

            var latest = Resources.Load<TextAsset>("latest-schema");
            if (latest != null && !string.IsNullOrWhiteSpace(latest.text))
                return latest.text;

            return string.Empty;
        }
#endif

        private static void BuildSchemaIndex(string rawSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(rawSchemaJson))
                return;

            var rootValue = JsonCodec.ParseToPlainValue(rawSchemaJson);
            if (!TryAsObject(rootValue, out var root))
                return;

            var jsonSchema = TryGetObject(root, "jsonSchema", out var schemaNode) ? schemaNode : root;
            if (!TryGetObject(jsonSchema, "$defs", out var defs) || defs.Count == 0)
                return;

            SchemaDefinitions.Clear();
            foreach (var section in defs)
            {
                if (TryAsObject(section.Value, out var sectionObject))
                    AddDefinitionsFromObject(sectionObject);
            }
        }

        private static void AddDefinitionsFromObject(IDictionary<string, object> source)
        {
            foreach (var item in source)
            {
                if (!TryAsObject(item.Value, out var definition))
                    continue;

                AddDefinition(item.Key, definition);

                if (TryGetString(definition, "title", out var title))
                    AddDefinition(title, definition);
            }
        }

        private static void AddDefinition(string key, IDictionary<string, object> definition)
        {
            if (string.IsNullOrWhiteSpace(key) || definition == null)
                return;

            if (SchemaDefinitions.ContainsKey(key))
                return;

            SchemaDefinitions[key] = definition;
        }

        private static bool TryAsObject(object value, out IDictionary<string, object> obj)
        {
            obj = value as IDictionary<string, object>;
            return obj != null;
        }

        private static bool TryGetObject(IDictionary<string, object> source, string propertyName, out IDictionary<string, object> result)
        {
            result = null;
            if (!TryGetValue(source, propertyName, out var value))
                return false;

            return TryAsObject(value, out result);
        }

        private static bool TryGetValue(IDictionary<string, object> source, string propertyName, out object value)
        {
            value = null;
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            if (source.TryGetValue(propertyName, out value))
                return true;

            foreach (var pair in source)
            {
                if (!string.Equals(pair.Key, propertyName, StringComparison.Ordinal))
                    continue;

                value = pair.Value;
                return true;
            }

            return false;
        }

        private static bool TryGetString(IDictionary<string, object> source, string propertyName, out string result)
        {
            result = null;
            if (!TryGetValue(source, propertyName, out var value) || value == null)
                return false;

            result = value.ToString();
            return !string.IsNullOrWhiteSpace(result);
        }

        private static bool IsSchemaType(IDictionary<string, object> source, string expectedType)
        {
            return TryGetString(source, "type", out var actualType) &&
                   string.Equals(actualType, expectedType, StringComparison.OrdinalIgnoreCase);
        }

        public static Dictionary<string, object> BuildVariables(object key)
        {
            return new Dictionary<string, object> { { "id", key } };
        }
    }
}

#endif
