using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.RPC;
using Playserv.Proxy.Common;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

#nullable enable

namespace Playserv.Wrapper
{
    internal sealed class PlayServApi : IPlayServApi
    {
#if UNITY_5_3_OR_NEWER
        private const string ConfigResourceName = "PlayServConfig";
#endif

        private PlayServImplementation? _instance;
        private PlayServSettings? _settings;
        private string? _instanceEndpoint;

        public string SdkVersion => SdkInfo.Version;

        public PlayServState State => _instance?.State ?? PlayServState.Offline;

        public event Action<TransportError>? OnTransportError;
        public event Action? OnKeepAlivePingSent;
        public event Action? OnKeepAlivePongReceived;

        public void Config(PlayServSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settings = settings.Clone();
            ApplySettings(_settings);
        }

        public void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string? sdkVersion = null)
        {
            if (string.IsNullOrWhiteSpace(gameAccessToken))
                throw new ArgumentException("Game access token is required.", nameof(gameAccessToken));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Game ID is required.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required.", nameof(userId));

            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("Game version is required.", nameof(gameVersion));

            var settings = GetOrCreateSettings();
            settings.GameAccessToken = gameAccessToken;
            settings.GameId = gameId;
            settings.UserId = userId;
            settings.GameVersion = gameVersion;

            if (!string.IsNullOrWhiteSpace(sdkVersion))
                settings.SdkVersion = sdkVersion;

            ApplySettings(settings);
        }

        public async Task<bool> Connect()
        {
            if (State is PlayServState.Online or PlayServState.Connecting or PlayServState.Handshaking)
                throw new InvalidOperationException("PlayServ is already connected or connecting.");

            var settings = GetOrCreateSettings();
            ApplySettings(settings);
            return await Instance.Connect();
        }

        public IObservable<T> Subscribe<T>() =>
            Instance.Subscribe<T>();

        public IDisposable Subscribe<T>(Action<T> onNext) =>
            Instance.Subscribe(onNext);

        public void Send<T>(T command) => Instance.Send(command);

        public void Send<T>(T command, string moduleName) =>
            Instance.Send(command, moduleName);

        public void Invoke(string serviceName, string methodName, object? payload)
        {
            var payloadBase64 = RpcPayloadSerializer.SerializeToBase64(payload);
            Invoke(serviceName, methodName, payloadBase64);
        }

        public void Invoke(string serviceName, string methodName, string payloadBase64)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Service name is required.", nameof(serviceName));

            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name is required.", nameof(methodName));

            if (string.IsNullOrWhiteSpace(payloadBase64))
                throw new ArgumentException("Payload base64 is required.", nameof(payloadBase64));

            var request = new RpcInvokeRequest
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Payload = payloadBase64
            };

            Instance.Send(request, RpcConstants.InvokeModuleName);
        }

        public void Invoke<TService>(Expression<Action<TService>> method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var serviceName = ResolveServiceName<TService>();
            var methodCall = ResolveMethodCall(method.Body, nameof(method));
            var payload = BuildPayloadFromMethodCall(methodCall);
            Invoke(serviceName, methodCall.Method.Name, payload);
        }

        public void Invoke<TService>(Expression<Action<TService>> method, object? payload)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var serviceName = ResolveServiceName<TService>();
            var methodCall = ResolveMethodCall(method.Body, nameof(method));
            Invoke(serviceName, methodCall.Method.Name, payload);
        }

        public void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var serviceName = ResolveServiceName<TService>();
            var methodCall = ResolveMethodCall(method.Body, nameof(method));
            Invoke(serviceName, methodCall.Method.Name, payloadBase64);
        }

        public Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Instance.GetTransportImplementation();

        public void Publish<T>(T @event) =>
            Instance.Publish(@event);

        public void PublishForGroup<T>(string groupName, T @event) =>
            Instance.PublishForGroup(groupName, @event);

        public void PublishForUser<T>(string userId, T @event) =>
            Instance.PublishForUser(userId, @event);

#if UNITY_5_3_OR_NEWER
        public Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Instance.Spawn(assetName, position, rotation);

        public Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Instance.Spawn(assetName, position);
#endif

        public void Disconnect()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
                _instanceEndpoint = null;
            }
        }

        public Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new() =>
            Instance.SelectEntity(playerId, map);

        private PlayServImplementation Instance =>
            _instance ?? throw new InvalidOperationException("SDK is not connected. Call Connect() first.");

        private void SubscribeToInstanceEvents()
        {
            _instance!.OnTransportError -= HandleTransportError;
            _instance.OnKeepAlivePingSent -= HandleKeepAlivePingSent;
            _instance.OnKeepAlivePongReceived -= HandleKeepAlivePongReceived;

            _instance.OnTransportError += HandleTransportError;
            _instance.OnKeepAlivePingSent += HandleKeepAlivePingSent;
            _instance.OnKeepAlivePongReceived += HandleKeepAlivePongReceived;
        }

        private void HandleTransportError(TransportError error) => OnTransportError?.Invoke(error);
        private void HandleKeepAlivePingSent() => OnKeepAlivePingSent?.Invoke();
        private void HandleKeepAlivePongReceived() => OnKeepAlivePongReceived?.Invoke();

        private static MethodCallExpression ResolveMethodCall(Expression expression, string paramName)
        {
            if (expression is not MethodCallExpression methodCall)
                throw new ArgumentException("RPC expression must be a method call.", paramName);

            if (string.IsNullOrWhiteSpace(methodCall.Method.Name))
                throw new ArgumentException("Unable to resolve RPC method name from expression.", paramName);

            return methodCall;
        }

        private static string ResolveServiceName<TService>()
        {
            var serviceType = typeof(TService);
            EnsureRpcServiceAttribute(serviceType);
            return serviceType.Name;
        }

        private static void EnsureRpcServiceAttribute(Type serviceType)
        {
            foreach (var attribute in serviceType.GetCustomAttributes(inherit: true))
            {
                if (attribute is RpcAttribute)
                    return;

                var attributeTypeName = attribute.GetType().Name;
                if (string.Equals(attributeTypeName, "RPCAttribute", StringComparison.Ordinal) ||
                    string.Equals(attributeTypeName, "RpcAttribute", StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"RPC service type '{serviceType.FullName}' must be decorated with [RPC] (or [Rpc]) attribute.");
        }

        private static object BuildPayloadFromMethodCall(MethodCallExpression methodCall)
        {
            var parameters = methodCall.Method.GetParameters();
            if (parameters.Length == 0)
                return new Dictionary<string, object?>(0);

            var payload = new Dictionary<string, object?>(parameters.Length, StringComparer.Ordinal);
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterName = parameters[i].Name;
                if (string.IsNullOrWhiteSpace(parameterName))
                    parameterName = $"arg{i}";

                payload[parameterName] = EvaluateExpressionValue(methodCall.Arguments[i]);
            }

            return payload;
        }

        private static object? EvaluateExpressionValue(Expression expression)
        {
            if (expression is ConstantExpression constantExpression)
                return constantExpression.Value;

            var boxed = Expression.Convert(expression, typeof(object));
            var getter = Expression.Lambda<Func<object?>>(boxed).Compile();
            return getter();
        }

        private void ApplySettings(PlayServSettings settings)
        {
            EnsureConfigured(settings);
            EnsureInstanceForEndpoint(settings.Endpoint);

            Instance.SetConfig(
                settings.GameAccessToken,
                settings.GameId,
                settings.UserId,
                settings.GameVersion,
                settings.SdkVersion,
                settings.AllowMultipleConnections,
                settings.KeepAlivePingIntervalMs,
                settings.KeepAlivePongTimeoutMs);

            SubscribeToInstanceEvents();
        }

        private void EnsureInstanceForEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

            if (_instance == null)
            {
                _instance = new PlayServImplementation(endpoint);
                _instanceEndpoint = endpoint;
                return;
            }

            if (string.Equals(_instanceEndpoint, endpoint, StringComparison.Ordinal))
                return;

            _instance.Dispose();
            _instance = new PlayServImplementation(endpoint);
            _instanceEndpoint = endpoint;
        }

        private PlayServSettings GetOrCreateSettings()
        {
            if (_settings != null)
                return _settings;

            if (TryLoadSettingsFromUnityResources(out var settings))
            {
                _settings = settings;
                return _settings;
            }

            _settings = new PlayServSettings();
            return _settings;
        }

        private static void EnsureConfigured(PlayServSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.GameAccessToken))
                throw new InvalidOperationException("Game access token is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.GameId))
                throw new InvalidOperationException("Game ID is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.UserId))
                throw new InvalidOperationException("User ID is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.GameVersion))
                throw new InvalidOperationException("Game version is required. Call Config(...) first.");

            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                throw new InvalidOperationException("Endpoint is required. Provide LocalEndpoint or RemoteEndpoint.");
        }

        private static bool TryLoadSettingsFromUnityResources(out PlayServSettings settings)
        {
#if UNITY_5_3_OR_NEWER
            var config = Resources.Load<PlayServConfig>(ConfigResourceName);
            if (config != null)
            {
                settings = config.ToSettings();
                return true;
            }
#endif

            settings = null!;
            return false;
        }
    }
}

#nullable restore
