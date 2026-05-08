using System;

namespace Playserv.Editor
{
    internal static class PlayServGeneratedEventsOutputRegistry
    {
        private static IPlayServGeneratedEventsOutputContributor _contributor;

        public static void Register(IPlayServGeneratedEventsOutputContributor contributor)
        {
            _contributor = contributor ?? throw new ArgumentNullException(nameof(contributor));
        }

        public static bool TrySync(out bool changed)
        {
            changed = false;

            var contributor = _contributor;
            if (contributor == null)
                return false;

            changed = contributor.SyncGeneratedOutputForCurrentState();
            return true;
        }
    }
}
