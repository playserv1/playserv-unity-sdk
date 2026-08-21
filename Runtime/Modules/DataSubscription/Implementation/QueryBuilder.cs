using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Playserv.Data;
using Playserv.Serialization;

namespace Playserv.DataSubscription
{
    internal sealed class PlayServRealtimeQueryPayload
    {
        public PlayServRealtimeQueryPayload(string query, Dictionary<string, object> variables)
        {
            Query = query ?? throw new ArgumentNullException(nameof(query));
            Variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }

        public string Query { get; }

        public Dictionary<string, object> Variables { get; }
    }

    internal static class QueryBuilder
    {
        private const int MaxSelectionDepth = 6;
        private static readonly object SchemaGate = new object();
        private static readonly IJsonCodec DefaultJsonCodec = new NewtonsoftJsonCodec();
        private static int _schemaVersionStamp = int.MinValue;
        private static readonly Dictionary<string, IDictionary<string, object>> SchemaDefinitions =
            new Dictionary<string, IDictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        public static string BuildQuery<T>(
            string entityType,
            object key,
            Expression<Func<T, object>> selector = null,
            IJsonCodec jsonCodec = null)
        {
            return BuildKeyedQuery(
                entityType,
                key,
                typeof(T),
                selector,
                null,
                jsonCodec);
        }

        /// <summary>
        /// Builds a keyed query without a selection set. The current dataflow backend interprets
        /// that shape as <c>SelectAll</c>, which is required when a managed record handle must
        /// refresh its complete canonical snapshot rather than a DTO projection.
        /// </summary>
        public static PlayServRealtimeQueryPayload BuildKeyedAllFieldsQuery(
            string entityType,
            object key)
        {
            ValidateIdentifier(entityType, "entity name");
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            return new PlayServRealtimeQueryPayload(
                entityType + "(id: $id)",
                BuildVariables(key));
        }

        public static string BuildKeyedQuery(
            string entityType,
            object key,
            Type modelType,
            LambdaExpression selector,
            IReadOnlyList<LambdaExpression> includes,
            IJsonCodec jsonCodec = null)
        {
            var sb = new StringBuilder();
            sb.Append(entityType);
            sb.Append("(id: $id)");

            var selection = selector == null
                ? BuildAutoSelection(modelType, entityType, jsonCodec)
                : BuildSelectionFromExpression(selector.Body);

            if (includes != null && includes.Count > 0)
            {
                var includeSelections = includes
                    .Select(BuildIncludeSelection)
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                selection = string.Join(" ", new[] { selection }
                    .Concat(includeSelections)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            if (string.IsNullOrWhiteSpace(selection))
                return sb.ToString();

            sb.Append(" { ");
            sb.Append(selection);
            sb.Append(" }");

            return sb.ToString();
        }

        public static PlayServRealtimeQueryPayload BuildCollectionQuery<T>(
            string entityType,
            PlayServQuerySnapshot snapshot,
            IJsonCodec jsonCodec = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            ValidateIdentifier(entityType, "entity name");

            ValidateRealtimeCapabilities(snapshot);

            var variables = new Dictionary<string, object>(StringComparer.Ordinal);
            var arguments = new List<string>();
            if (snapshot.Filters.Count > 0 || snapshot.OrGroups.Count > 0)
            {
                var where = new List<string>();
                for (var index = 0; index < snapshot.Filters.Count; index++)
                {
                    var variableName = "filter" + index;
                    where.Add(EncodeFilter(snapshot.Filters[index], variableName, variables));
                }

                if (snapshot.OrGroups.Count > 0)
                {
                    var groups = new List<string>();
                    for (var groupIndex = 0; groupIndex < snapshot.OrGroups.Count; groupIndex++)
                    {
                        var predicates = new List<string>();
                        var group = snapshot.OrGroups[groupIndex];
                        for (var filterIndex = 0; filterIndex < group.Count; filterIndex++)
                        {
                            var variableName = "or" + groupIndex + "Filter" + filterIndex;
                            predicates.Add(EncodeFilter(group[filterIndex], variableName, variables));
                        }
                        groups.Add("{ " + string.Join(", ", predicates) + " }");
                    }
                    where.Add("or: [ " + string.Join(", ", groups) + " ]");
                }

                arguments.Add("where: { " + string.Join(", ", where) + " }");
            }

            variables["limit"] = snapshot.Limit;
            arguments.Add("limit: $limit");

            var selection = BuildCollectionSelection<T>(entityType, snapshot, jsonCodec);
            var query = entityType + "(" + string.Join(", ", arguments) + ") { " + selection + " }";
            return new PlayServRealtimeQueryPayload(query, variables);
        }

        public static void ValidateRealtimeCapabilities(PlayServQuerySnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var unsupported = new List<string>();
            if (snapshot.Filters.Count > 16)
                unsupported.Add("more than 16 common predicates");
            if (snapshot.OrGroups.Count > 8)
                unsupported.Add("more than 8 OR groups");
            if (!string.IsNullOrWhiteSpace(snapshot.SearchText))
                unsupported.Add("search");
            if (snapshot.Sort.Count > 0)
                unsupported.Add("sort");
            if (!string.IsNullOrWhiteSpace(snapshot.Cursor))
                unsupported.Add("cursor");
            if (snapshot.HiddenColumns.Count > 0)
                unsupported.Add("hidden field projection");

            foreach (var filter in snapshot.Filters)
            {
                if (!IsRealtimeOperator(filter.Operator))
                    unsupported.Add(PlayServRecordWireNames.OperatorToWire(filter.Operator));
            }
            foreach (var group in snapshot.OrGroups)
            {
                if (group.Count > 16)
                    unsupported.Add("more than 16 predicates in an OR group");
                foreach (var filter in group)
                {
                    if (!IsRealtimeOperator(filter.Operator))
                        unsupported.Add(PlayServRecordWireNames.OperatorToWire(filter.Operator));
                }
            }

            if (unsupported.Count > 0)
                throw new PlayServQueryCapabilityException(PlayServQueryTarget.Realtime, unsupported);
        }

        public static void ValidateRestCapabilities(PlayServQuerySnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var unsupported = new List<string>();
            if (snapshot.OrGroups.Count > 0)
                unsupported.Add("or");
            if (snapshot.Includes.Any(include => include.IsNested))
                unsupported.Add("nested include");

            if (unsupported.Count > 0)
                throw new PlayServQueryCapabilityException(PlayServQueryTarget.Rest, unsupported);
        }

        private static string EncodeFilter(
            PlayServQueryFilter filter,
            string variableName,
            IDictionary<string, object> variables)
        {
            ValidateIdentifier(filter.Field, "filter field");
            variables[variableName] = filter.Value;
            return filter.Field + ": { " +
                   PlayServRecordWireNames.OperatorToWire(filter.Operator) +
                   ": $" + variableName + " }";
        }

        private static string BuildAutoSelection(Type modelType, string entityType, IJsonCodec jsonCodec)
        {
            var schemaSelection = TryBuildSelectionFromSchema(entityType, jsonCodec);
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
                    sb.Append(PlayServRecordWireNames.FromMember(member.Member));
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
                            BuildSelection(assignment.Expression, sb);
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

                string wireName;
                try
                {
                    wireName = PlayServRecordWireNames.FromMember(property);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!usedNames.Add(wireName))
                    continue;

                var fieldSelection = BuildFieldSelection(wireName, property.PropertyType, depth, path);
                if (!string.IsNullOrWhiteSpace(fieldSelection))
                    selections.Add(fieldSelection);
            }

            foreach (var field in normalizedType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsStatic)
                    continue;

                string wireName;
                try
                {
                    wireName = PlayServRecordWireNames.FromMember(field);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!usedNames.Add(wireName))
                    continue;

                var fieldSelection = BuildFieldSelection(wireName, field.FieldType, depth, path);
                if (!string.IsNullOrWhiteSpace(fieldSelection))
                    selections.Add(fieldSelection);
            }

            path.Remove(normalizedType);
            return string.Join(" ", selections);
        }

        private static string BuildCollectionSelection<T>(
            string entityType,
            PlayServQuerySnapshot snapshot,
            IJsonCodec jsonCodec)
        {
            var fields = snapshot.SelectedFields.Count > 0
                ? snapshot.SelectedFields.ToList()
                : BuildFlatSelection(typeof(T), entityType, jsonCodec);

            foreach (var field in fields)
                ValidateIdentifier(field, "selected field");

            var includeTree = BuildIncludeTree(snapshot.Includes);
            var selections = fields
                .Where(field => !includeTree.Any(include =>
                    string.Equals(include.Segment, field, StringComparison.Ordinal)))
                .ToList();
            foreach (var include in includeTree)
            {
                selections.Add(BuildIncludeTreeSelection(
                    typeof(T),
                    entityType,
                    include,
                    Array.Empty<string>(),
                    jsonCodec));
            }

            return selections.Count == 0 ? "*" : string.Join(" ", selections);
        }

        private static IReadOnlyList<IncludeTreeNode> BuildIncludeTree(
            IReadOnlyList<PlayServQueryInclude> includes)
        {
            var roots = new List<IncludeTreeNode>();
            foreach (var include in includes)
            {
                var level = roots;
                for (var depth = 0; depth < include.Segments.Count; depth++)
                {
                    var segment = include.Segments[depth];
                    ValidateIdentifier(segment, "include field");
                    var node = level.FirstOrDefault(value =>
                        string.Equals(value.Segment, segment, StringComparison.Ordinal));
                    if (node == null)
                    {
                        if (level.Count >= 16)
                        {
                            throw new ArgumentException(
                                "Realtime selection supports at most 16 relation expansions per level.");
                        }
                        node = new IncludeTreeNode(segment);
                        level.Add(node);
                    }
                    level = node.Children;
                }
            }
            return roots;
        }

        private static string BuildIncludeTreeSelection(
            Type ownerType,
            string entityType,
            IncludeTreeNode node,
            IReadOnlyList<string> ownerPath,
            IJsonCodec jsonCodec)
        {
            ValidateIdentifier(node.Segment, "include field");
            var memberType = PlayServRecordWireNames.TryGetMemberType(ownerType, node.Segment);
            var normalizedMemberType = memberType == null ? null : NormalizeType(memberType);
            var currentPath = ownerPath.Concat(new[] { node.Segment }).ToArray();
            var fields = normalizedMemberType == null
                ? new List<string>()
                : GetSerializableMembers(normalizedMemberType)
                    .Select(member => PlayServRecordWireNames.FromMember(member))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            if (fields.Count == 0)
                fields = TryGetNestedSchemaPropertyNames(entityType, currentPath, jsonCodec);
            foreach (var field in fields)
                ValidateIdentifier(field, "included field");

            var selections = fields
                .Where(field => !node.Children.Any(child =>
                    string.Equals(child.Segment, field, StringComparison.Ordinal)))
                .ToList();
            foreach (var child in node.Children)
            {
                selections.Add(BuildIncludeTreeSelection(
                    normalizedMemberType,
                    entityType,
                    child,
                    currentPath,
                    jsonCodec));
            }
            if (selections.Count == 0)
                selections.Add("*");

            return node.Segment + " { " + string.Join(" ", selections) + " }";
        }

        private static List<string> BuildFlatSelection(
            Type modelType,
            string entityType,
            IJsonCodec jsonCodec)
        {
            var schemaFields = TryGetSchemaPropertyNames(entityType, jsonCodec);
            if (schemaFields.Count > 0)
                return schemaFields;

            return GetSerializableMembers(modelType)
                .Select(member => PlayServRecordWireNames.FromMember(member))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string BuildFlatNestedSelection(
            Type modelType,
            string entityType,
            string include,
            IJsonCodec jsonCodec)
        {
            var memberType = PlayServRecordWireNames.TryGetMemberType(modelType, include);
            if (memberType != null)
            {
                var fields = GetSerializableMembers(NormalizeType(memberType))
                    .Select(member => PlayServRecordWireNames.FromMember(member))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                foreach (var field in fields)
                    ValidateIdentifier(field, "included field");
                if (fields.Count > 0)
                    return string.Join(" ", fields);
            }

            var schemaFields = TryGetNestedSchemaPropertyNames(entityType, include, jsonCodec);
            foreach (var field in schemaFields)
                ValidateIdentifier(field, "included field");
            return schemaFields.Count == 0 ? "*" : string.Join(" ", schemaFields);
        }

        private static IEnumerable<MemberInfo> GetSerializableMembers(Type type)
        {
            if (type == null || IsScalar(type))
                return Array.Empty<MemberInfo>();

            var properties = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .Cast<MemberInfo>();
            var fields = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsStatic)
                .Cast<MemberInfo>();
            return properties.Concat(fields).Where(member =>
            {
                try
                {
                    PlayServRecordWireNames.FromMember(member);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            });
        }

        private static string BuildIncludeSelection(LambdaExpression include)
        {
            if (include == null)
                throw new ArgumentNullException(nameof(include));

            Expression body = include.Body;
            if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
                body = unary.Operand;
            if (!(body is MemberExpression member) ||
                !(member.Expression is ParameterExpression))
            {
                throw new ArgumentException(
                    "Include must directly reference one relation, for example x => x.Team.",
                    nameof(include));
            }

            var field = PlayServRecordWireNames.FromMember(member.Member, nameof(include));
            var nestedFields = GetSerializableMembers(NormalizeType(member.Type))
                .Select(value => PlayServRecordWireNames.FromMember(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var nested = nestedFields.Count == 0 ? "*" : string.Join(" ", nestedFields);
            return field + " { " + nested + " }";
        }

        private static bool IsRealtimeOperator(PlayServQueryOperator op)
        {
            return op == PlayServQueryOperator.Eq ||
                   op == PlayServQueryOperator.Neq ||
                   op == PlayServQueryOperator.Gt ||
                   op == PlayServQueryOperator.Gte ||
                   op == PlayServQueryOperator.Lt ||
                   op == PlayServQueryOperator.Lte ||
                   op == PlayServQueryOperator.Contains ||
                   op == PlayServQueryOperator.StartsWith ||
                   op == PlayServQueryOperator.EndsWith;
        }

        private static void ValidateIdentifier(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !(char.IsLetter(value[0]) || value[0] == '_') ||
                value.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            {
                throw new ArgumentException(
                    $"Realtime {label} '{value}' is not a valid dataflow identifier.");
            }
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

        private static string TryBuildSelectionFromSchema(string entityType, IJsonCodec jsonCodec)
        {
            if (string.IsNullOrWhiteSpace(entityType))
                return string.Empty;

            EnsureSchemaDefinitionsLoaded(jsonCodec);
            if (SchemaDefinitions.Count == 0)
                return string.Empty;

            if (!SchemaDefinitions.TryGetValue(entityType, out var entitySchema))
                return string.Empty;

            return BuildSelectionFromSchemaNode(entitySchema, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static List<string> TryGetSchemaPropertyNames(
            string entityType,
            IJsonCodec jsonCodec)
        {
            EnsureSchemaDefinitionsLoaded(jsonCodec);
            if (string.IsNullOrWhiteSpace(entityType) ||
                !SchemaDefinitions.TryGetValue(entityType, out var entitySchema) ||
                !TryGetObject(entitySchema, "properties", out var properties))
            {
                return new List<string>();
            }

            return properties.Keys
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> TryGetNestedSchemaPropertyNames(
            string entityType,
            string field,
            IJsonCodec jsonCodec)
        {
            return TryGetNestedSchemaPropertyNames(
                entityType,
                new[] { field },
                jsonCodec);
        }

        private static List<string> TryGetNestedSchemaPropertyNames(
            string entityType,
            IReadOnlyList<string> path,
            IJsonCodec jsonCodec)
        {
            EnsureSchemaDefinitionsLoaded(jsonCodec);
            if (string.IsNullOrWhiteSpace(entityType) ||
                path == null ||
                path.Count == 0 ||
                !SchemaDefinitions.TryGetValue(entityType, out var current))
            {
                return new List<string>();
            }

            foreach (var segment in path)
            {
                if (!TryGetObject(current, "properties", out var properties) ||
                    !TryGetValue(properties, segment, out var fieldSchema))
                {
                    return new List<string>();
                }

                current = ResolveNestedSchema(
                    fieldSchema,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (current == null)
                    return new List<string>();
            }

            if (!TryGetObject(current, "properties", out var nestedProperties))
                return new List<string>();

            return nestedProperties.Keys
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
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

        private static void EnsureSchemaDefinitionsLoaded(IJsonCodec jsonCodec)
        {
            var snapshot = SchemaSelectionProvider.GetSnapshot();
            if (_schemaVersionStamp == snapshot.VersionStamp)
                return;

            lock (SchemaGate)
            {
                snapshot = SchemaSelectionProvider.GetSnapshot();
                if (_schemaVersionStamp == snapshot.VersionStamp)
                    return;

                SchemaDefinitions.Clear();
                try
                {
                    if (snapshot.HasSchema)
                        BuildSchemaIndex(snapshot.SchemaJson, ResolveJsonCodec(jsonCodec));
                }
                catch
                {
                    SchemaDefinitions.Clear();
                }
                finally
                {
                    _schemaVersionStamp = snapshot.VersionStamp;
                }
            }
        }

        private static IJsonCodec ResolveJsonCodec(IJsonCodec jsonCodec)
        {
            return jsonCodec ?? DefaultJsonCodec;
        }

        private static void BuildSchemaIndex(string rawSchemaJson, IJsonCodec jsonCodec)
        {
            if (string.IsNullOrWhiteSpace(rawSchemaJson))
                return;

            var rootValue = jsonCodec.ParseToPlainValue(rawSchemaJson);
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

        private sealed class IncludeTreeNode
        {
            public IncludeTreeNode(string segment)
            {
                Segment = segment;
            }

            public string Segment { get; }

            public List<IncludeTreeNode> Children { get; } = new List<IncludeTreeNode>();
        }
    }
}
