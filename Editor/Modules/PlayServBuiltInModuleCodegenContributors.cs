using Playserv.Modules;

namespace Playserv.Editor
{
    internal sealed class PlayServClientExecutionCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.ClientExecutionId;
        public int Order => 10;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.SetLocalExecutionFactory(
                "new global::Playserv.Proxy.Common.NoOpPlayServLocalExecution()",
                priority: 0);
        }
    }

    internal sealed class PlayServEventsCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.EventsId;
        public int Order => 20;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.Events.PlayServEventsModule");
            context.AddCompatibilityUsing("System");
            context.AddCompatibilityUsing("System.Threading");
            context.AddCompatibilityUsing("System.Threading.Tasks");
            context.AddCompatibilityUsing("Playserv.Events");
            context.AddFacadeMember(
                "public static IObservable<T> Subscribe<T>() =>",
                "    PlayServEvents.Subscribe<T>();",
                "",
                "public static IDisposable Subscribe<T>(Action<T> onNext) =>",
                "    PlayServEvents.Subscribe(onNext);",
                "",
                "public static void Publish<T>(T @event) =>",
                "    PlayServEvents.Publish(@event);",
                "",
                "public static void PublishForGroup<T>(string groupName, T @event) =>",
                "    PlayServEvents.PublishForGroup(groupName, @event);",
                "",
                "public static void PublishForUser<T>(string userId, T @event) =>",
                "    PlayServEvents.PublishForUser(userId, @event);",
                "",
                "public static Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default) =>",
                "    PlayServEvents.SubscribeGroupAsync(groupName, ct);",
                "",
                "public static Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default) =>",
                "    PlayServEvents.UnsubscribeGroupAsync(groupName, ct);");
        }
    }

    internal sealed class PlayServDataSubscriptionCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.DataSubscriptionId;
        public int Order => 30;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.DataSubscription.PlayServDataSubscriptionModule");
            context.AddCompatibilityUsing("System");
            context.AddCompatibilityUsing("System.Collections.Generic");
            context.AddCompatibilityUsing("System.Threading");
            context.AddCompatibilityUsing("System.Threading.Tasks");
            context.AddCompatibilityUsing("Playserv.DataSubscription");
            context.AddCompatibilityUsing("Playserv.DataSubscription.Responses");
            context.AddFacadeMember(
                "public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(",
                "    string playerId,",
                "    Func<TEntity, TDto> map,",
                "    DataSubscriptionMode mode = DataSubscriptionMode.Polling)",
                "    where TEntity : class",
                "    where TDto : class, new() =>",
                "    PlayServData.SelectEntity<TEntity, TDto>(playerId, map, mode);",
                "",
                "public static Task<DataGetResponse> GetDataByKeyAsync(",
                "    string key,",
                "    string query,",
                "    Dictionary<string, object> variables,",
                "    CancellationToken ct = default) =>",
                "    PlayServData.GetDataByKeyAsync(key, query, variables, ct);",
                "",
                "public static IDisposable StartDataByKeyPolling(",
                "    string key,",
                "    string query,",
                "    Dictionary<string, object> variables,",
                "    Action<DataGetResponse> onData,",
                "    Action<Exception> onError = null) =>",
                "    PlayServData.StartDataByKeyPolling(key, query, variables, onData, onError);");
        }
    }

    internal sealed class PlayServRpcCoreCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.RpcCoreId;
        public int Order => 40;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.RPC.PlayServRpcCoreModule");
        }
    }

    internal sealed class PlayServClientRpcCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.ClientRpcId;
        public int Order => 50;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.RPC.PlayServClientRpcModule");
            context.AddCompatibilityUsing("System");
            context.AddCompatibilityUsing("System.Collections.Generic");
            context.AddCompatibilityUsing("System.Linq.Expressions");
            context.AddCompatibilityUsing("Playserv.RPC");
            context.AddFacadeMember(
                "public static event Action<InvokeRpcResponse> OnRpcInvokeResponse",
                "{",
                "    add => PlayServRpc.OnRpcInvokeResponse += value;",
                "    remove => PlayServRpc.OnRpcInvokeResponse -= value;",
                "}",
                "",
                "public static void Send<T>(T command) =>",
                "    PlayServRpc.Send(command);",
                "",
                "public static void Send<T>(T command, string moduleName) =>",
                "    PlayServRpc.Send(command, moduleName);",
                "",
                "public static void Invoke(string serviceName, string methodName, object payload) =>",
                "    PlayServRpc.Invoke(serviceName, methodName, payload);",
                "",
                "public static void InvokeArgs(string serviceName, string methodName, params object[] args) =>",
                "    PlayServRpc.InvokeArgs(serviceName, methodName, args);",
                "",
                "public static void InvokeNamed(string serviceName, string methodName, IDictionary<string, object> payload) =>",
                "    PlayServRpc.InvokeNamed(serviceName, methodName, payload);",
                "",
                "public static void Invoke(string serviceName, string methodName, string payloadBase64) =>",
                "    PlayServRpc.Invoke(serviceName, methodName, payloadBase64);",
                "",
                "public static void Invoke(string serviceName, string methodName, object payload, string coalesceKey) =>",
                "    PlayServRpc.Invoke(serviceName, methodName, payload, coalesceKey);",
                "",
                "public static void Invoke(string serviceName, string methodName, object payload, string coalesceKey, bool fireAndForget) =>",
                "    PlayServRpc.Invoke(serviceName, methodName, payload, coalesceKey, fireAndForget);",
                "",
                "public static void Invoke<TService>(Expression<Action<TService>> method) =>",
                "    PlayServRpc.Invoke(method);",
                "",
                "public static void Invoke<TService>(Expression<Action<TService>> method, object payload) =>",
                "    PlayServRpc.Invoke(method, payload);",
                "",
                "public static void Invoke<TService>(Expression<Action<TService>> method, string payloadBase64) =>",
                "    PlayServRpc.Invoke(method, payloadBase64);");
        }
    }

    internal sealed class PlayServServerCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.ServerId;
        public int Order => 60;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.Server.PlayServServerModule");
            context.AddCompatibilityUsing("Playserv.RPC");
            context.AddCompatibilityUsing("Playserv.Server");
            context.AddFacadeMember(
                "public static void SetCommandHandler(ICommandHandler commandHandler)",
                "    => PlayServServerRpc.SetCommandHandler(commandHandler);",
                "",
                "public static void SetEventHandler(IEventHandler eventHandler)",
                "    => PlayServServerRpc.SetEventHandler(eventHandler);",
                "",
                "public static void SetRpcInvoker(IRpcInvoker rpcInvoker) =>",
                "    PlayServServerRpc.SetRpcInvoker(rpcInvoker);");
            context.SetLocalExecutionFactory(
                "new global::Playserv.Server.PlayServServerLocalExecution()",
                priority: 100);
        }
    }

    internal sealed class PlayServSpawnCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.SpawnId;
        public int Order => 70;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.Spawn.PlayServSpawnModule");
            context.AddCompatibilityUsing("System.Threading");
            context.AddCompatibilityUsing("System.Threading.Tasks");
            context.AddCompatibilityUsing("Playserv.Spawn");
            context.AddCompatibilityUsing("UnityEngine");
            context.AddFacadeMember(
                "public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>",
                "    PlayServSpawn.Spawn(assetName, position, rotation);",
                "",
                "public static Task<GameObject> Spawn(string assetName, Vector3 position) =>",
                "    PlayServSpawn.Spawn(assetName, position);",
                "",
                "public static string CurrentSpawnScope => PlayServSpawn.CurrentScope;",
                "",
                "public static Task<bool> JoinSpawnScopeAsync(string groupName, CancellationToken ct = default) =>",
                "    PlayServSpawn.JoinSpawnScopeAsync(groupName, ct);",
                "",
                "public static Task<bool> JoinSpawnScope(string groupName, CancellationToken ct = default) =>",
                "    PlayServSpawn.JoinSpawnScope(groupName, ct);",
                "",
                "public static Task<bool> LeaveSpawnScopeAsync(CancellationToken ct = default) =>",
                "    PlayServSpawn.LeaveSpawnScopeAsync(ct);",
                "",
                "public static Task<bool> LeaveSpawnScope(CancellationToken ct = default) =>",
                "    PlayServSpawn.LeaveSpawnScope(ct);",
                "",
                "public static bool Despawn(string spawnId) =>",
                "    PlayServSpawn.Despawn(spawnId);",
                "",
                "public static bool Despawn(GameObject instance) =>",
                "    PlayServSpawn.Despawn(instance);");
        }
    }

    internal sealed class PlayServPulseCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.PulseId;
        public int Order => 80;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.Pulse.PlayServPulseModule");
        }
    }

    internal sealed class PlayServAppleSignInCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.AppleSignInId;
        public int Order => 90;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.AppleSignIn.PlayServAppleSignInModule");
        }
    }

    internal sealed class PlayServGoogleSignInCodegenContributor : IPlayServModuleCodegenContributor
    {
        public string ModuleId => PlayServModuleManifest.GoogleSignInId;
        public int Order => 100;

        public void Contribute(PlayServModuleCodegenContext context)
        {
            context.RegisterRuntimeModule("Playserv.GoogleSignIn.PlayServGoogleSignInModule");
        }
    }
}
