using System;
using System.Collections.Generic;
using System.Reflection;
using Playserv.Serialization;

namespace Playserv.RPC
{
    /// <summary>
    /// In-process RPC invoker for server/runtime usage without websocket transport.
    /// </summary>
    public sealed class LocalRpcInvoker : IRpcInvoker
    {
        private readonly Dictionary<string, object> _services = new(StringComparer.Ordinal);
        private readonly IJsonCodec _jsonCodec;

        public LocalRpcInvoker()
            : this(new NewtonsoftJsonCodec())
        {
        }

        public LocalRpcInvoker(IJsonCodec jsonCodec)
        {
            _jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        /// <summary>
        /// Registers service instance under its runtime type name.
        /// </summary>
        /// <typeparam name="TService">Service type.</typeparam>
        /// <param name="service">Service instance.</param>
        /// <returns>Current invoker instance for chaining.</returns>
        public LocalRpcInvoker RegisterService<TService>(TService service)
            where TService : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            return RegisterService(service.GetType().Name, service);
        }

        /// <summary>
        /// Registers service instance under explicit RPC service name.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="service">Service instance.</param>
        /// <returns>Current invoker instance for chaining.</returns>
        public LocalRpcInvoker RegisterService(string serviceName, object service)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            EnsureRpcServiceAttribute(service.GetType());
            _services[serviceName] = service;
            return this;
        }

        /// <summary>
        /// Removes registered service by name.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <returns>True if service existed and was removed.</returns>
        public bool UnregisterService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            return _services.Remove(serviceName);
        }

        /// <summary>
        /// Clears all registered services.
        /// </summary>
        public void Clear() => _services.Clear();

        /// <summary>
        /// Invokes registered service method by service/method names and base64 payload.
        /// </summary>
        /// <param name="serviceName">RPC service name.</param>
        /// <param name="methodName">RPC method name.</param>
        /// <param name="payloadBase64">Base64-encoded UTF8 JSON payload.</param>
        /// <returns>True when service was found and invocation was executed locally.</returns>
        public bool TryInvoke(string serviceName, string methodName, string payloadBase64)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name is required.", nameof(methodName));

            if (string.IsNullOrWhiteSpace(payloadBase64))
                throw new ArgumentException("Payload base64 is required.", nameof(payloadBase64));

            if (!_services.TryGetValue(serviceName, out var service))
                return false;

            var method = ResolveMethod(service.GetType(), methodName);
            var arguments = BuildArguments(method, payloadBase64);
            _ = method.Invoke(service, arguments);
            return true;
        }

        private static MethodInfo ResolveMethod(Type serviceType, string methodName)
        {
            MethodInfo matchedMethod = null;
            foreach (var method in serviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                if (matchedMethod != null)
                {
                    throw new InvalidOperationException(
                        $"Service '{serviceType.FullName}' contains multiple overloads for method '{methodName}'. " +
                        "LocalRpcInvoker does not support method overload resolution.");
                }

                matchedMethod = method;
            }

            return matchedMethod ??
                   throw new MissingMethodException(serviceType.FullName, methodName);
        }

        private object[] BuildArguments(MethodInfo method, string payloadBase64)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                return Array.Empty<object>();

            var payloadJson = RpcPayloadSerializer.DecodeToJson(payloadBase64);
            if (string.IsNullOrWhiteSpace(payloadJson) ||
                string.Equals(payloadJson, "null", StringComparison.OrdinalIgnoreCase))
            {
                return BuildArgumentsFromMissingPayload(parameters, method);
            }

            var payloadValue = _jsonCodec.ParseToPlainValue(payloadJson);
            if (payloadValue is IDictionary<string, object> payloadObject)
                return BuildArgumentsFromObject(parameters, payloadObject, method);

            if (payloadValue is IList<object> payloadArray)
                return BuildArgumentsFromArray(parameters, payloadArray, method);

            if (parameters.Length == 1)
            {
                return new[]
                {
                    ConvertValue(payloadValue, parameters[0], method)
                };
            }

            throw new InvalidOperationException(
                $"RPC payload for '{method.DeclaringType?.Name}.{method.Name}' must be a JSON object or array.");
        }

        private object[] BuildArgumentsFromObject(
            ParameterInfo[] parameters,
            IDictionary<string, object> payload,
            MethodInfo method)
        {
            var arguments = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var parameterName = parameter.Name ?? $"arg{i}";
                if (_jsonCodec.TryGetProperty(payload, parameterName, ignoreCase: true, out var value))
                {
                    arguments[i] = ConvertValue(value, parameter, method);
                    continue;
                }

                arguments[i] = ResolveFallbackParameterValue(parameter, method);
            }

            return arguments;
        }

        private object[] BuildArgumentsFromArray(
            ParameterInfo[] parameters,
            IList<object> payload,
            MethodInfo method)
        {
            if (payload.Count > parameters.Length)
            {
                throw new InvalidOperationException(
                    $"RPC payload array contains {payload.Count} arguments, but method " +
                    $"'{method.DeclaringType?.Name}.{method.Name}' expects {parameters.Length}.");
            }

            var arguments = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i < payload.Count)
                {
                    arguments[i] = ConvertValue(payload[i], parameters[i], method);
                    continue;
                }

                arguments[i] = ResolveFallbackParameterValue(parameters[i], method);
            }

            return arguments;
        }

        private static object[] BuildArgumentsFromMissingPayload(ParameterInfo[] parameters, MethodInfo method)
        {
            var arguments = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                arguments[i] = ResolveFallbackParameterValue(parameters[i], method);
            }

            return arguments;
        }

        private static object ResolveFallbackParameterValue(ParameterInfo parameter, MethodInfo method)
        {
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;

            if (CanAssignNull(parameter.ParameterType))
                return null;

            throw new InvalidOperationException(
                $"RPC payload does not contain required parameter '{parameter.Name}' for method " +
                $"'{method.DeclaringType?.Name}.{method.Name}'.");
        }

        private object ConvertValue(object value, ParameterInfo parameter, MethodInfo method)
        {
            if (value == null)
            {
                if (CanAssignNull(parameter.ParameterType))
                    return null;

                throw new InvalidOperationException(
                    $"RPC parameter '{parameter.Name}' for method '{method.DeclaringType?.Name}.{method.Name}' " +
                    "cannot be null.");
            }

            try
            {
                return _jsonCodec.Convert(value, parameter.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to convert RPC parameter '{parameter.Name}' to '{parameter.ParameterType.FullName}'.",
                    ex);
            }
        }

        private static bool CanAssignNull(Type type) =>
            !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

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
}
