#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
namespace Playserv.DataSubscription
{
    internal static class SharedDiffBuilder
    {
        public static object BuildPatch<T>(T value)
        {
            return value;
        }
    }
}

#endif
