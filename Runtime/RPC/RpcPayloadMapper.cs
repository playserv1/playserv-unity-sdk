using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Playserv.RPC
{
    /// <summary>
    /// Maps RPC method expressions and explicit payload objects to transport-ready payload structures.
    /// </summary>
    internal static class RpcPayloadMapper
    {
        private static readonly ConcurrentDictionary<MethodInfo, ParameterInfo[]> MethodParametersCache = new();
        private static readonly ConcurrentDictionary<Type, PayloadPropertyAccessor[]> PayloadAccessorCache = new();
        private static readonly ConditionalWeakTable<Expression, Func<object>> ExpressionValueGetterCache = new();

        public static object BuildPayloadFromMethodCall(MethodCallExpression methodCall)
        {
            if (methodCall == null)
                throw new ArgumentNullException(nameof(methodCall));

            var parameters = GetCachedMethodParameters(methodCall.Method);
            if (parameters.Length == 0)
                return new Dictionary<string, object>(0);

            var payload = new Dictionary<string, object>(parameters.Length, StringComparer.Ordinal);
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterName = parameters[i].Name;
                if (string.IsNullOrWhiteSpace(parameterName))
                    parameterName = $"arg{i}";

                payload[parameterName] = EvaluateExpressionValue(methodCall.Arguments[i]);
            }

            return payload;
        }

        public static object BuildPayloadFromExplicitPayload(MethodCallExpression methodCall, object payload)
        {
            if (methodCall == null)
                throw new ArgumentNullException(nameof(methodCall));

            var parameters = GetCachedMethodParameters(methodCall.Method);
            if (parameters.Length == 0)
                return Array.Empty<object>();

            if (payload == null)
                return BuildArgumentsFromEmptyPayload(parameters, methodCall.Method);

            if (TryConvertToPositionalArray(payload, out var positionalArray))
                return MapPositionalPayload(parameters, positionalArray, methodCall.Method);

            if (parameters.Length == 1)
                return new object[] { payload };

            if (TryBuildArgumentsFromNamedPayload(payload, parameters, methodCall.Method, out var namedArguments))
                return namedArguments;

            throw new InvalidOperationException(
                $"Cannot map payload of type '{payload.GetType().FullName}' to method " +
                $"'{methodCall.Method.DeclaringType?.Name}.{methodCall.Method.Name}' with {parameters.Length} parameters. " +
                "Provide an array payload in parameter order or an object/dictionary with matching parameter names.");
        }

        private static object EvaluateExpressionValue(Expression expression)
        {
            if (expression is ConstantExpression constantExpression)
                return constantExpression.Value;

            return GetCachedExpressionValueGetter(expression).Invoke();
        }

        private static object[] BuildArgumentsFromEmptyPayload(ParameterInfo[] parameters, MethodInfo method)
        {
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                    continue;
                }

                if (!parameters[i].ParameterType.IsValueType || Nullable.GetUnderlyingType(parameters[i].ParameterType) != null)
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"RPC payload is empty but parameter '{parameters[i].Name}' is required for method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}'.");
            }

            return args;
        }

        private static object[] MapPositionalPayload(ParameterInfo[] parameters, object[] payloadArray, MethodInfo method)
        {
            if (payloadArray.Length > parameters.Length)
            {
                throw new InvalidOperationException(
                    $"RPC positional payload contains {payloadArray.Length} value(s), but method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}' expects {parameters.Length} parameter(s).");
            }

            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i < payloadArray.Length)
                {
                    args[i] = payloadArray[i];
                    continue;
                }

                if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                    continue;
                }

                if (!parameters[i].ParameterType.IsValueType || Nullable.GetUnderlyingType(parameters[i].ParameterType) != null)
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"RPC positional payload is missing required parameter '{parameters[i].Name}' for method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}'.");
            }

            return args;
        }

        private static bool TryConvertToPositionalArray(object payload, out object[] values)
        {
            values = Array.Empty<object>();

            if (payload is string)
                return false;

            if (payload is object[] objectArray)
            {
                values = objectArray;
                return true;
            }

            if (payload is IList list)
            {
                values = new object[list.Count];
                for (var i = 0; i < list.Count; i++)
                    values[i] = list[i];
                return true;
            }

            return false;
        }

        private static bool TryBuildArgumentsFromNamedPayload(
            object payload,
            ParameterInfo[] parameters,
            MethodInfo method,
            out object[] args)
        {
            args = Array.Empty<object>();

            if (payload is IDictionary<string, object> genericDictionary)
                return TryBuildArgumentsFromGenericDictionary(genericDictionary, parameters, method, out args);

            if (payload is IReadOnlyDictionary<string, object> readOnlyDictionary)
                return TryBuildArgumentsFromReadOnlyDictionary(readOnlyDictionary, parameters, method, out args);

            var valuesByName = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (payload is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key == null)
                        continue;

                    var key = entry.Key.ToString();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    valuesByName[key] = entry.Value;
                }
            }
            else
            {
                var accessors = GetCachedPayloadAccessors(payload.GetType());
                for (var i = 0; i < accessors.Length; i++)
                {
                    var accessor = accessors[i];
                    valuesByName[accessor.Name] = accessor.Getter(payload);
                }
            }

            if (valuesByName.Count == 0)
                return false;

            args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterName = parameters[i].Name ?? $"arg{i}";
                if (valuesByName.TryGetValue(parameterName, out var value))
                {
                    args[i] = value;
                    continue;
                }

                if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                    continue;
                }

                if (!parameters[i].ParameterType.IsValueType || Nullable.GetUnderlyingType(parameters[i].ParameterType) != null)
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Named RPC payload does not contain required parameter '{parameterName}' for method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}'.");
            }

            return true;
        }

        private static bool TryBuildArgumentsFromGenericDictionary(
            IDictionary<string, object> valuesByName,
            ParameterInfo[] parameters,
            MethodInfo method,
            out object[] args)
        {
            if (valuesByName == null || valuesByName.Count == 0)
            {
                args = Array.Empty<object>();
                return false;
            }

            args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterName = parameters[i].Name ?? $"arg{i}";
                if (TryGetValueIgnoreCase(valuesByName, parameterName, out var value))
                {
                    args[i] = value;
                    continue;
                }

                if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                    continue;
                }

                if (!parameters[i].ParameterType.IsValueType || Nullable.GetUnderlyingType(parameters[i].ParameterType) != null)
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Named RPC payload does not contain required parameter '{parameterName}' for method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}'.");
            }

            return true;
        }

        private static bool TryBuildArgumentsFromReadOnlyDictionary(
            IReadOnlyDictionary<string, object> valuesByName,
            ParameterInfo[] parameters,
            MethodInfo method,
            out object[] args)
        {
            if (valuesByName == null || valuesByName.Count == 0)
            {
                args = Array.Empty<object>();
                return false;
            }

            args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterName = parameters[i].Name ?? $"arg{i}";
                if (TryGetValueIgnoreCase(valuesByName, parameterName, out var value))
                {
                    args[i] = value;
                    continue;
                }

                if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                    continue;
                }

                if (!parameters[i].ParameterType.IsValueType || Nullable.GetUnderlyingType(parameters[i].ParameterType) != null)
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Named RPC payload does not contain required parameter '{parameterName}' for method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}'.");
            }

            return true;
        }

        private static bool TryGetValueIgnoreCase(
            IDictionary<string, object> valuesByName,
            string key,
            out object value)
        {
            if (valuesByName.TryGetValue(key, out value))
                return true;

            foreach (var pair in valuesByName)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool TryGetValueIgnoreCase(
            IReadOnlyDictionary<string, object> valuesByName,
            string key,
            out object value)
        {
            if (valuesByName.TryGetValue(key, out value))
                return true;

            foreach (var pair in valuesByName)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static ParameterInfo[] GetCachedMethodParameters(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            return MethodParametersCache.GetOrAdd(method, static m => m.GetParameters());
        }

        private static PayloadPropertyAccessor[] GetCachedPayloadAccessors(Type payloadType)
        {
            if (payloadType == null)
                throw new ArgumentNullException(nameof(payloadType));

            return PayloadAccessorCache.GetOrAdd(payloadType, static type =>
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var accessors = new List<PayloadPropertyAccessor>(properties.Length);

                foreach (var property in properties)
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        continue;

                    var instance = Expression.Parameter(typeof(object), "instance");
                    var typedInstance = Expression.Convert(instance, type);
                    var propertyAccess = Expression.Property(typedInstance, property);
                    var boxedValue = Expression.Convert(propertyAccess, typeof(object));
                    var getter = Expression.Lambda<Func<object, object>>(boxedValue, instance).Compile();

                    accessors.Add(new PayloadPropertyAccessor(property.Name, getter));
                }

                return accessors.ToArray();
            });
        }

        private static Func<object> GetCachedExpressionValueGetter(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));

            return ExpressionValueGetterCache.GetValue(expression, static expr =>
            {
                var boxed = Expression.Convert(expr, typeof(object));
                return Expression.Lambda<Func<object>>(boxed).Compile();
            });
        }

        private sealed class PayloadPropertyAccessor
        {
            public PayloadPropertyAccessor(string name, Func<object, object> getter)
            {
                Name = name;
                Getter = getter;
            }

            public string Name { get; }
            public Func<object, object> Getter { get; }
        }
    }
}
