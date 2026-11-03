using System;

namespace Playserv.Editor
{
    internal static class PlayServSchemaSelectionRegistry
    {
        private static IPlayServSchemaSelectionContributor _contributor;

        public static void Register(IPlayServSchemaSelectionContributor contributor)
        {
            _contributor = contributor ?? throw new ArgumentNullException(nameof(contributor));
        }

        public static bool TryReset()
        {
            var contributor = _contributor;
            if (contributor == null)
                return false;

            contributor.ResetSchemaSelection();
            return true;
        }
    }
}
