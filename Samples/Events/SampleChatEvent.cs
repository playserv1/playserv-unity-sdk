using System;
using Playserv.Events;

namespace Playserv.Samples
{
    /// <summary>
    /// Demo event payload for event sample scene.
    /// </summary>
    [Event(EventType.All)]
    [Serializable]
    public struct SampleChatEvent
    {
        public string SenderId;
        public string Text;
        public long SentAtUnixMs;
    }
}
