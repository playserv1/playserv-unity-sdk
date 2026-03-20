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
        private static readonly ConcurrentDictionary<MethodInfo, RpcMethodBindingPlan> MethodBindingPlanCache = new();
        private static readonly ConcurrentDictionary<Type, PayloadPropertyAccessor[]> PayloadAccessorCache = new();
        private static readonly ConditionalWeakTable<Expression, Func<object>> ExpressionValueGetterCache = new();

        public static RpcMappedPayload BuildPayloadFromMethodCall(MethodCallExpression methodCall)
        {
            if (methodCall == null)
                throw new ArgumentNullException(nameof(methodCall));

            var bindingPlan = GetCachedMethodBindingPlan(methodCall.Method);
            if (bindingPlan.ParameterCount == 0)
                return RpcMappedPayload.Named(new Dictionary<string, object>(0, StringComparer.Ordinal));

            var payload = new Dictionary<string, object>(bindingPlan.ParameterCount, StringComparer.Ordinal);
            for (var i = 0; i < bindingPlan.ParameterCount; i++)
            {
                payload[bindingPlan.ParameterNames[i]] = EvaluateExpressionValue(methodCall.Arguments[i]);
            }

            return RpcMappedPayload.Named(payload);
        }

        public static RpcMappedPayload BuildPayloadFromExplicitPayload(MethodCallExpression methodCall, object payload)
        {
            if (methodCall == null)
                throw new ArgumentNullException(nameof(methodCall));

            var bindingPlan = GetCachedMethodBindingPlan(methodCall.Method);
            if (bindingPlan.ParameterCount == 0)
                return RpcMappedPayload.Positional(Array.Empty<object>());

            if (payload == null)
                return RpcMappedPayload.Positional(BuildArgumentsFromEmptyPayload(bindingPlan));

            if (TryConvertToPositionalArray(payload, out var positionalArray))
                return RpcMappedPayload.Positional(MapPositionalPayload(bindingPlan, positionalArray));

            if (bindingPlan.ParameterCount == 1)
                return RpcMappedPayload.Positional(new object[] { payload });

            if (TryBuildArgumentsFromNamedPayload(payload, bindingPlan, out var namedArguments))
                return RpcMappedPayload.Positional(namedArguments);

            throw new InvalidOperationException(
                $"Cannot map payload of type '{payload.GetType().FullName}' to method " +
                $"'{bindingPlan.MethodDisplayName}' with {bindingPlan.ParameterCount} parameters. " +
                "Provide an array payload in parameter order or an object/dictionary with matching parameter names.");
        }

        private static object EvaluateExpressionValue(Expression expression)
        {
            if (expression is ConstantExpression constantExpression)
                return constantExpression.Value;

            return GetCachedExpressionValueGetter(expression).Invoke();
        }

        private static object[] BuildArgumentsFromEmptyPayload(RpcMethodBindingPlan bindingPlan)
        {
            var args = new object[bindingPlan.ParameterCount];
            for (var i = 0; i < bindingPlan.ParameterCount; i++)
            {
                if (bindingPlan.HasDefaultValues[i])
                {
                    args[i] = bindingPlan.DefaultValues[i];
                    continue;
                }

                if (bindingPlan.AllowsNull[i])
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"RPC payload is empty but parameter '{bindingPlan.ParameterNames[i]}' is required for method " +
                    $"'{bindingPlan.MethodDisplayName}'.");
            }

            return args;
        }

        private static object[] MapPositionalPayload(RpcMethodBindingPlan bindingPlan, object[] payloadArray)
        {
            if (payloadArray.Length > bindingPlan.ParameterCount)
            {
                throw new InvalidOperationException(
                    $"RPC positional payload contains {payloadArray.Length} value(s), but method " +
                    $"'{bindingPlan.MethodDisplayName}' expects {bindingPlan.ParameterCount} parameter(s).");
            }

            var args = new object[bindingPlan.ParameterCount];
            for (var i = 0; i < bindingPlan.ParameterCount; i++)
            {
                if (i < payloadArray.Length)
                {
                    args[i] = payloadArray[i];
                    continue;
                }

                if (bindingPlan.HasDefaultValues[i])
                {
                    args[i] = bindingPlan.DefaultValues[i];
                    continue;
                }

                if (bindingPlan.AllowsNull[i])
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"RPC positional payload is missing required parameter '{bindingPlan.ParameterNames[i]}' for method " +
                    $"'{bindingPlan.MethodDisplayName}'.");
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
            RpcMethodBindingPlan bindingPlan,
            out object[] args)
        {
            args = Array.Empty<object>();

            if (payload is IDictionary<string, object> genericDictionary)
                return TryBuildArgumentsFromGenericDictionary(genericDictionary, bindingPlan, out args);

            if (payload is IReadOnlyDictionary<string, object> readOnlyDictionary)
                return TryBuildArgumentsFromReadOnlyDictionary(readOnlyDictionary, bindingPlan, out args);

            args = new object[bindingPlan.ParameterCount];
            var assigned = new bool[bindingPlan.ParameterCount];
            var hasAssignedValues = false;

            if (payload is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key == null)
                        continue;

                    var key = entry.Key.ToString();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (!bindingPlan.ParameterIndexByName.TryGetValue(key, out var parameterIndex))
                        continue;

                    args[parameterIndex] = entry.Value;
                    assigned[parameterIndex] = true;
                    hasAssignedValues = true;
                }
            }
            else
            {
                var accessors = GetCachedPayloadAccessors(payload.GetType());
                for (var i = 0; i < accessors.Length; i++)
                {
                    var accessor = accessors[i];
                    if (!bindingPlan.ParameterIndexByName.TryGetValue(accessor.Name, out var parameterIndex))
                        continue;

                    args[parameterIndex] = accessor.Getter(payload);
                    assigned[parameterIndex] = true;
                    hasAssignedValues = true;
                }
            }

            if (!hasAssignedValues)
                return false;

            return FinalizeNamedArguments(bindingPlan, args, assigned);
        }

        private static bool TryBuildArgumentsFromGenericDictionary(
            IDictionary<string, object> valuesByName,
            RpcMethodBindingPlan bindingPlan,
            out object[] args)
        {
            if (valuesByName == null || valuesByName.Count == 0)
            {
                args = Array.Empty<object>();
                return false;
            }

            args = new object[bindingPlan.ParameterCount];
            var assigned = new bool[bindingPlan.ParameterCount];
            var hasAssignedValues = false;

            foreach (var pair in valuesByName)
            {
                if (!bindingPlan.ParameterIndexByName.TryGetValue(pair.Key, out var parameterIndex))
                    continue;

                args[parameterIndex] = pair.Value;
                assigned[parameterIndex] = true;
                hasAssignedValues = true;
            }

            if (!hasAssignedValues)
                return false;

            return FinalizeNamedArguments(bindingPlan, args, assigned);
        }

        private static bool TryBuildArgumentsFromReadOnlyDictionary(
            IReadOnlyDictionary<string, object> valuesByName,
            RpcMethodBindingPlan bindingPlan,
            out object[] args)
        {
            if (valuesByName == null || valuesByName.Count == 0)
            {
                args = Array.Empty<object>();
                return false;
            }

            args = new object[bindingPlan.ParameterCount];
            var assigned = new bool[bindingPlan.ParameterCount];
            var hasAssignedValues = false;

            foreach (var pair in valuesByName)
            {
                if (!bindingPlan.ParameterIndexByName.TryGetValue(pair.Key, out var parameterIndex))
                    continue;

                args[parameterIndex] = pair.Value;
                assigned[parameterIndex] = true;
                hasAssignedValues = true;
            }

            if (!hasAssignedValues)
                return false;

            return FinalizeNamedArguments(bindingPlan, args, assigned);
        }

        private static bool FinalizeNamedArguments(
            RpcMethodBindingPlan bindingPlan,
            object[] args,
            bool[] assigned)
        {
            for (var i = 0; i < bindingPlan.ParameterCount; i++)
            {
                if (assigned[i])
                    continue;

                if (bindingPlan.HasDefaultValues[i])
                {
                    args[i] = bindingPlan.DefaultValues[i];
                    continue;
                }

                if (bindingPlan.AllowsNull[i])
                {
                    args[i] = null;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Named RPC payload does not contain required parameter '{bindingPlan.ParameterNames[i]}' for method " +
                    $"'{bindingPlan.MethodDisplayName}'.");
            }

            return true;
        }

        private static RpcMethodBindingPlan GetCachedMethodBindingPlan(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            return MethodBindingPlanCache.GetOrAdd(method, static m => new RpcMethodBindingPlan(m));
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

        private sealed class RpcMethodBindingPlan
        {
            public RpcMethodBindingPlan(MethodInfo method)
            {
                Method = method ?? throw new ArgumentNullException(nameof(method));
                Parameters = method.GetParameters();
                ParameterCount = Parameters.Length;
                MethodDisplayName = $"{method.DeclaringType?.Name}.{method.Name}";
                ParameterNames = new string[ParameterCount];
                HasDefaultValues = new bool[ParameterCount];
                DefaultValues = new object[ParameterCount];
                AllowsNull = new bool[ParameterCount];
                ParameterIndexByName = new Dictionary<string, int>(ParameterCount, StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < ParameterCount; i++)
                {
                    var parameter = Parameters[i];
                    var parameterName = parameter.Name;
                    if (string.IsNullOrWhiteSpace(parameterName))
                        parameterName = $"arg{i}";

                    ParameterNames[i] = parameterName;
                    HasDefaultValues[i] = parameter.HasDefaultValue;
                    DefaultValues[i] = parameter.DefaultValue;
                    AllowsNull[i] = !parameter.ParameterType.IsValueType ||
                                    Nullable.GetUnderlyingType(parameter.ParameterType) != null;
                    ParameterIndexByName[parameterName] = i;
                }
            }

            public MethodInfo Method { get; }
            public ParameterInfo[] Parameters { get; }
            public int ParameterCount { get; }
            public string MethodDisplayName { get; }
            public string[] ParameterNames { get; }
            public bool[] HasDefaultValues { get; }
            public object[] DefaultValues { get; }
            public bool[] AllowsNull { get; }
            public Dictionary<string, int> ParameterIndexByName { get; }
        }
    }
}
