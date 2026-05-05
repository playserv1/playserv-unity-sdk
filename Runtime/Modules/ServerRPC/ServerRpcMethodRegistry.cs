#if !PLAYSERV_DISABLE_RPC_CORE && !PLAYSERV_DISABLE_SERVER_RPC
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Playserv.Serialization;

namespace Playserv.RPC
{
    internal sealed class ServerRpcMethodRegistry
    {
        private static readonly ConcurrentDictionary<Type, ServerRpcMethodRegistry> Cache = new();

        private readonly Type _serviceType;
        private readonly Dictionary<string, ServerRpcMethodDescriptor> _methods;

        private ServerRpcMethodRegistry(Type serviceType, Dictionary<string, ServerRpcMethodDescriptor> methods)
        {
            _serviceType = serviceType;
            _methods = methods;
        }

        public static ServerRpcMethodRegistry For(Type serviceType)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            return Cache.GetOrAdd(serviceType, Build);
        }

        public ServerRpcMethodDescriptor GetRequiredMethod(string methodName)
        {
            if (_methods.TryGetValue(methodName, out var descriptor))
                return descriptor;

            throw new MissingMethodException(_serviceType.FullName, methodName);
        }

        private static ServerRpcMethodRegistry Build(Type serviceType)
        {
            EnsureRpcServiceAttribute(serviceType);

            var methods = new Dictionary<string, ServerRpcMethodDescriptor>(StringComparer.Ordinal);
            foreach (var method in serviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.IsSpecialName)
                    continue;

                if (methods.ContainsKey(method.Name))
                {
                    throw new InvalidOperationException(
                        $"Service '{serviceType.FullName}' contains multiple overloads for method '{method.Name}'. " +
                        "ServerRpcInvoker does not support method overload resolution.");
                }

                if (method.ContainsGenericParameters)
                {
                    throw new InvalidOperationException(
                        $"Service '{serviceType.FullName}' contains generic RPC method '{method.Name}'. " +
                        "ServerRpcInvoker does not support generic RPC methods.");
                }

                methods[method.Name] = ServerRpcMethodDescriptor.Create(method);
            }

            return new ServerRpcMethodRegistry(serviceType, methods);
        }

        private static void EnsureRpcServiceAttribute(Type serviceType)
        {
            foreach (var attribute in serviceType.GetCustomAttributes(inherit: true))
            {
                if (attribute is RpcAttribute)
                    return;

                if (string.Equals(attribute.GetType().Name, "RpcAttribute", StringComparison.Ordinal))
                    return;
            }

            throw new InvalidOperationException(
                $"RPC service type '{serviceType.FullName}' must be decorated with [Rpc] attribute.");
        }
    }

    internal sealed class ServerRpcMethodDescriptor
    {
        private readonly string _methodDisplayName;
        private readonly ServerRpcParameterDescriptor[] _parameters;
        private readonly Action<object, object[]> _invoke;

        private ServerRpcMethodDescriptor(
            string methodDisplayName,
            ServerRpcParameterDescriptor[] parameters,
            Action<object, object[]> invoke)
        {
            _methodDisplayName = methodDisplayName;
            _parameters = parameters;
            _invoke = invoke;
        }

        public static ServerRpcMethodDescriptor Create(MethodInfo method)
        {
            var parameters = method.GetParameters();
            var descriptors = new ServerRpcParameterDescriptor[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                descriptors[i] = ServerRpcParameterDescriptor.Create(parameters[i], i);

            return new ServerRpcMethodDescriptor(
                $"{method.DeclaringType?.Name}.{method.Name}",
                descriptors,
                CompileInvoker(method));
        }

        public object[] BuildArguments(string payloadBase64, IJsonCodec jsonCodec)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            if (_parameters.Length == 0)
                return Array.Empty<object>();

            var payloadJson = RpcPayloadSerializer.DecodeToJson(payloadBase64);
            if (string.IsNullOrWhiteSpace(payloadJson) ||
                string.Equals(payloadJson, "null", StringComparison.OrdinalIgnoreCase))
            {
                return BuildArgumentsFromMissingPayload();
            }

            var payloadValue = jsonCodec.ParseToPlainValue(payloadJson);
            if (payloadValue is IDictionary<string, object> payloadObject)
                return BuildArgumentsFromObject(payloadObject, jsonCodec);

            if (payloadValue is IList<object> payloadArray)
                return BuildArgumentsFromArray(payloadArray, jsonCodec);

            if (_parameters.Length == 1)
            {
                return new[]
                {
                    ConvertValue(payloadValue, _parameters[0], jsonCodec)
                };
            }

            throw new InvalidOperationException(
                $"RPC payload for '{_methodDisplayName}' must be a JSON object or array.");
        }

        public void Execute(object service, object[] arguments)
        {
            _invoke(service, arguments);
        }

        private object[] BuildArgumentsFromObject(IDictionary<string, object> payload, IJsonCodec jsonCodec)
        {
            var arguments = new object[_parameters.Length];
            for (var i = 0; i < _parameters.Length; i++)
            {
                var parameter = _parameters[i];
                if (jsonCodec.TryGetProperty(payload, parameter.Name, ignoreCase: true, out var value))
                {
                    arguments[i] = ConvertValue(value, parameter, jsonCodec);
                    continue;
                }

                arguments[i] = ResolveFallbackParameterValue(parameter);
            }

            return arguments;
        }

        private object[] BuildArgumentsFromArray(IList<object> payload, IJsonCodec jsonCodec)
        {
            if (payload.Count > _parameters.Length)
            {
                throw new InvalidOperationException(
                    $"RPC payload array contains {payload.Count} arguments, but method " +
                    $"'{_methodDisplayName}' expects {_parameters.Length}.");
            }

            var arguments = new object[_parameters.Length];
            for (var i = 0; i < _parameters.Length; i++)
            {
                if (i < payload.Count)
                {
                    arguments[i] = ConvertValue(payload[i], _parameters[i], jsonCodec);
                    continue;
                }

                arguments[i] = ResolveFallbackParameterValue(_parameters[i]);
            }

            return arguments;
        }

        private object[] BuildArgumentsFromMissingPayload()
        {
            var arguments = new object[_parameters.Length];
            for (var i = 0; i < _parameters.Length; i++)
                arguments[i] = ResolveFallbackParameterValue(_parameters[i]);

            return arguments;
        }

        private object ResolveFallbackParameterValue(ServerRpcParameterDescriptor parameter)
        {
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;

            if (parameter.AllowsNull)
                return null;

            throw new InvalidOperationException(
                $"RPC payload does not contain required parameter '{parameter.Name}' for method '{_methodDisplayName}'.");
        }

        private object ConvertValue(object value, ServerRpcParameterDescriptor parameter, IJsonCodec jsonCodec)
        {
            if (value == null)
            {
                if (parameter.AllowsNull)
                    return null;

                throw new InvalidOperationException(
                    $"RPC parameter '{parameter.Name}' for method '{_methodDisplayName}' cannot be null.");
            }

            try
            {
                return jsonCodec.Convert(value, parameter.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to convert RPC parameter '{parameter.Name}' to '{parameter.ParameterType.FullName}'.",
                    ex);
            }
        }

        private static Action<object, object[]> CompileInvoker(MethodInfo method)
        {
#if ENABLE_IL2CPP
            return CreateReflectionInvoker(method);
#else
            try
            {
                return CompileExpressionInvoker(method);
            }
            catch (PlatformNotSupportedException)
            {
                return CreateReflectionInvoker(method);
            }
            catch (NotSupportedException)
            {
                return CreateReflectionInvoker(method);
            }
#endif
        }

        private static Action<object, object[]> CompileExpressionInvoker(MethodInfo method)
        {
            var service = Expression.Parameter(typeof(object), "service");
            var arguments = Expression.Parameter(typeof(object[]), "arguments");
            var parameters = method.GetParameters();
            var callArguments = new Expression[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var argument = Expression.ArrayIndex(arguments, Expression.Constant(i));
                callArguments[i] = Expression.Convert(argument, parameters[i].ParameterType);
            }

            var call = Expression.Call(
                Expression.Convert(service, method.DeclaringType),
                method,
                callArguments);

            Expression body = method.ReturnType == typeof(void)
                ? call
                : Expression.Block(call, Expression.Empty());

            return Expression.Lambda<Action<object, object[]>>(body, service, arguments).Compile();
        }

        private static Action<object, object[]> CreateReflectionInvoker(MethodInfo method)
        {
            return (service, arguments) =>
            {
                try
                {
                    method.Invoke(service, arguments);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            };
        }
    }

    internal readonly struct ServerRpcParameterDescriptor
    {
        private ServerRpcParameterDescriptor(
            string name,
            Type parameterType,
            bool hasDefaultValue,
            object defaultValue,
            bool allowsNull)
        {
            Name = name;
            ParameterType = parameterType;
            HasDefaultValue = hasDefaultValue;
            DefaultValue = defaultValue;
            AllowsNull = allowsNull;
        }

        public string Name { get; }
        public Type ParameterType { get; }
        public bool HasDefaultValue { get; }
        public object DefaultValue { get; }
        public bool AllowsNull { get; }

        public static ServerRpcParameterDescriptor Create(ParameterInfo parameter, int index)
        {
            var parameterType = parameter.ParameterType;
            return new ServerRpcParameterDescriptor(
                parameter.Name ?? $"arg{index}",
                parameterType,
                parameter.HasDefaultValue,
                parameter.DefaultValue,
                !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null);
        }
    }
}

#endif
