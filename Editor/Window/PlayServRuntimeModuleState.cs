#if UNITY_EDITOR
namespace Playserv.Editor
{
    internal struct PlayServRuntimeModuleState
    {
        public bool Events;
        public bool Data;
        public bool Rpc;
        public bool ServerRpc;
        public bool Spawn;
        public bool Pulse;
    }
}
#endif
