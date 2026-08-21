using System;
using Playserv.Events;

namespace Playserv.DebugTerminal
{
    [Serializable]
    public sealed class Player
    {
        public string Nickname;
        public int? Level;
    }

    public sealed class DebugTerminalPlayerDto
    {
        public string Nickname { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    [Event(EventType.All)]
    [Serializable]
    public struct DebugTerminalChatEvent
    {
        public string SenderId;
        public string Text;
        public long SentAtUnixMs;
    }
}
