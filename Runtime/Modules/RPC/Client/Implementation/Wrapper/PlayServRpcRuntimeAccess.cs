using System;
using Playserv.Proxy.Common;
using Playserv.Serialization;

namespace Playserv.Wrapper
{
    internal sealed class PlayServRpcRuntimeAccess : IPlayServRpcRuntimeAccess
    {
        public event Action<string, object> ModuleCommandReceived
        {
            add => PlayServRuntimeHost.ModuleCommandReceived += value;
            remove => PlayServRuntimeHost.ModuleCommandReceived -= value;
        }

        public ILocalRpcExecution LocalExecution =>
            PlayServRuntimeHost.LocalExecution as ILocalRpcExecution ??
            throw new InvalidOperationException("RPC local execution is not available.");

        public bool HasCurrentInstance => PlayServRuntimeHost.HasCurrentInstance;

        public IJsonCodec ResolveJsonCodec() => PlayServRuntimeHost.ResolveJsonCodec();

        public void Send<T>(T command) => PlayServRuntimeHost.Send(command);

        public void Send<T>(T command, string moduleName) => PlayServRuntimeHost.Send(command, moduleName);
    }
}
