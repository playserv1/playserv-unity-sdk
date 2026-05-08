using System;
using Playserv.Modules;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    internal static class PlayServRuntimeHost
    {
        private static Func<IPlayServRuntimeAccess> _getRuntimeAccess;
        private static Func<IJsonCodec> _resolveJsonCodec;
        private static Func<object> _getLocalExecution;
        private static Action<object> _send;
        private static Action<object, string> _sendToModule;

        public static event Action<string, object> ModuleCommandReceived;

        public static void Configure(
            Func<IPlayServRuntimeAccess> getRuntimeAccess,
            Func<IJsonCodec> resolveJsonCodec,
            Func<object> getLocalExecution,
            Action<object> send,
            Action<object, string> sendToModule)
        {
            _getRuntimeAccess = getRuntimeAccess ?? throw new ArgumentNullException(nameof(getRuntimeAccess));
            _resolveJsonCodec = resolveJsonCodec ?? throw new ArgumentNullException(nameof(resolveJsonCodec));
            _getLocalExecution = getLocalExecution;
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _sendToModule = sendToModule ?? throw new ArgumentNullException(nameof(sendToModule));
        }

        public static IPlayServRuntimeAccess RuntimeAccess => RequireConfigured(_getRuntimeAccess, nameof(RuntimeAccess))();

        public static bool HasCurrentInstance => RuntimeAccess.HasCurrentInstance;

        public static IPlayServCommandBus CurrentCommandBus => RuntimeAccess.CurrentCommandBus;

        public static IPlayServCommandBus RequiredCommandBus => RuntimeAccess.RequiredCommandBus;

        public static IPlayServModuleServiceProvider CurrentModuleServices => RuntimeAccess.CurrentModuleServices;

        public static IPlayServModuleServiceProvider RequiredModuleServices => RuntimeAccess.RequiredModuleServices;

        public static object LocalExecution => _getLocalExecution == null ? null : _getLocalExecution();

        public static IJsonCodec ResolveJsonCodec() => RequireConfigured(_resolveJsonCodec, nameof(ResolveJsonCodec))();

        public static IPlayServCommandBus GetCommandBusForFireAndForget(string operationName) =>
            RuntimeAccess.GetCommandBusForFireAndForget(operationName);

        public static IPlayServModuleServiceProvider GetModuleServicesForFireAndForget(string operationName) =>
            RuntimeAccess.GetModuleServicesForFireAndForget(operationName);

        public static void Send<T>(T command) => RequireConfigured(_send, nameof(Send))(command);

        public static void Send<T>(T command, string moduleName) =>
            RequireConfigured(_sendToModule, nameof(Send))(command, moduleName);

        public static void NotifyModuleCommand(string commandName, object command) =>
            ModuleCommandReceived?.Invoke(commandName, command);

        private static T RequireConfigured<T>(T value, string memberName)
            where T : class
        {
            if (value != null)
                return value;

            throw new InvalidOperationException(
                $"PlayServ runtime host is not configured. Access '{memberName}' after PlayServ runtime initialization.");
        }
    }
}
