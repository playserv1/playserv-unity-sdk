using System;

namespace Playserv.Events
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class EventAttribute : Attribute
    {
        public EventType Type { get; }

        public EventAttribute(EventType type = EventType.All)
        {
            Type = type;
        }
    }
}

