using System;

namespace Playserv.Editor
{
    internal sealed class PlayServOptionalEditorSection : IDisposable
    {
        private readonly string _sectionId;
        private IPlayServEditorSection _instance;

        public PlayServOptionalEditorSection(string sectionId)
        {
            _sectionId = sectionId ?? throw new ArgumentNullException(nameof(sectionId));
        }

        public bool IsAvailable => PlayServEditorSectionRegistry.IsRegistered(_sectionId);

        public bool Draw(PlayServWindowContext context)
        {
            var instance = ResolveInstance();
            if (instance == null)
                return false;

            instance.Draw(context);
            return true;
        }

        public void Dispose()
        {
            _instance?.Dispose();
            _instance = null;
        }

        private IPlayServEditorSection ResolveInstance()
        {
            if (_instance != null)
                return _instance;

            if (!PlayServEditorSectionRegistry.TryCreate(_sectionId, out _instance))
                return null;

            return _instance;
        }
    }
}
