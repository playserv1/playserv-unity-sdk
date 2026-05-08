using System;
using Playserv.Modules;
using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    public static class PlayServRuntimeHost
    {
        private static Func<PlayServImplementation> _getCurrentInstance;
        private static Func<PlayServImplementation> _getRequiredInstance;
        private static Func<string, PlayServImplementation> _getInstanceForFireAndForget;
        private static Func<IJsonCodec> _resolveJsonCodec;
        private static Func<object> _getLocalExecution;
        private static Action<object> _send;
        private static Action<object, string> _sendToModule;

        public static event Action<string, object> ModuleCommandReceived;

        public static void Configure(
            Func<PlayServImplementation> getCurrentInstance,
            Func<PlayServImplementation> getRequiredInstance,
            Func<string, PlayServImplementation> getInstanceForFireAndForget,
            Func<IJsonCodec> resolveJsonCodec,
            Func<object> getLocalExecution,
            Action<object> send,
            Action<object, string> sendToModule)
        {
            _getCurrentInstance = getCurrentInstance ?? throw new ArgumentNullException(nameof(getCurrentInstance));
            _getRequiredInstance = getRequiredInstance ?? throw new ArgumentNullException(nameof(getRequiredInstance));
            _getInstanceForFireAndForget = getInstanceForFireAndForget ?? throw new ArgumentNullException(nameof(getInstanceForFireAndForget));
            _resolveJsonCodec = resolveJsonCodec ?? throw new ArgumentNullException(nameof(resolveJsonCodec));
            _getLocalExecution = getLocalExecution;
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _sendToModule = sendToModule ?? throw new ArgumentNullException(nameof(sendToModule));
        }

        public static PlayServImplementation CurrentInstance => RequireConfigured(_getCurrentInstance, nameof(CurrentInstance))();

        public static PlayServImplementation RequiredInstance => RequireConfigured(_getRequiredInstance, nameof(RequiredInstance))();

        public static IPlayServModuleServiceProvider RequiredModuleServices => RequiredInstance.ModuleServices;

        public static object LocalExecution => _getLocalExecution == null ? null : _getLocalExecution();

        public static IJsonCodec ResolveJsonCodec() => RequireConfigured(_resolveJsonCodec, nameof(ResolveJsonCodec))();

        public static PlayServImplementation GetInstanceForFireAndForget(string operationName) =>
            RequireConfigured(_getInstanceForFireAndForget, nameof(GetInstanceForFireAndForget))(operationName);

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
