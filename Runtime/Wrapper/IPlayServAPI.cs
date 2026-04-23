using System;

namespace Playserv.Wrapper
{
    /// <summary>
    /// Compatibility aggregate surface for older generated or external code.
    /// Prefer domain-specific interfaces for new code.
    /// </summary>
    [Obsolete("Use IPlayServConnectionApi, IPlayServRpcApi, IPlayServEventsApi, IPlayServDataApi and IPlayServSpawnApi instead.")]
    public interface IPlayServApi : IPlayServConnectionApi, IPlayServRpcApi, IPlayServEventsApi, IPlayServDataApi
#if UNITY_5_3_OR_NEWER
        , IPlayServSpawnApi
#endif
    {
    }
}
