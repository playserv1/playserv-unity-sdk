using Playserv.Events.Editor;
using UnityEditor;

namespace Playserv.Editor
{
    [InitializeOnLoad]
    internal static class PlayServEventsGeneratedOutputRegistration
    {
        static PlayServEventsGeneratedOutputRegistration()
        {
            PlayServGeneratedEventsOutputRegistry.Register(new Contributor());
        }

        private sealed class Contributor : IPlayServGeneratedEventsOutputContributor
        {
            public bool SyncGeneratedOutputForCurrentState()
            {
                return EventsCodeGenerator.SyncGeneratedOutputForCurrentState();
            }
        }
    }
}
