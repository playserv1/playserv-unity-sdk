using System;
using Playserv.Proxy.Common;
using Playserv.Serialization;

namespace Playserv.Wrapper
{
    internal interface IPlayServRpcRuntimeAccess
    {
        event Action<string, object> ModuleCommandReceived;

        ILocalRpcExecution LocalExecution { get; }

        bool HasCurrentInstance { get; }

        IJsonCodec ResolveJsonCodec();

        void Send<T>(T command);

        void Send<T>(T command, string moduleName);
    }
}
