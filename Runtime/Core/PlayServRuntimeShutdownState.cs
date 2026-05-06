using System.Threading;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Wrapper
{
    internal static class PlayServRuntimeShutdownState
    {
        private static int _isShuttingDown;

        public static bool IsShuttingDown => Volatile.Read(ref _isShuttingDown) != 0;

#if UNITY_5_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Reset();
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            MarkShuttingDown();
        }
#endif

        internal static void MarkShuttingDown()
        {
            Interlocked.Exchange(ref _isShuttingDown, 1);
        }

        internal static void Reset()
        {
            Interlocked.Exchange(ref _isShuttingDown, 0);
        }
    }
}
