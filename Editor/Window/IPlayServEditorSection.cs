using System;

namespace Playserv.Editor
{
    internal interface IPlayServEditorSection : IDisposable
    {
        void Draw(PlayServWindowContext context);
    }
}
