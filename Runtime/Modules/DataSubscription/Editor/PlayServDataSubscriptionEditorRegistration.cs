using Playserv.Editor;
using UnityEditor;

namespace Playserv.DataSubscription.Editor
{
    [InitializeOnLoad]
    internal static class PlayServDataSubscriptionEditorRegistration
    {
        static PlayServDataSubscriptionEditorRegistration()
        {
            PlayServSchemaSelectionRegistry.Register(new SchemaSelectionContributor());
        }

        private sealed class SchemaSelectionContributor : IPlayServSchemaSelectionContributor
        {
            public void ResetSchemaSelection()
            {
                SchemaSelectionProvider.Reset();
            }
        }
    }
}
