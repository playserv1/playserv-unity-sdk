using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed class PlayServServerRuntimeAccess : IPlayServServerRuntimeAccess
    {
        public object LocalExecution => PlayServRuntimeHost.LocalExecution;
    }
}
