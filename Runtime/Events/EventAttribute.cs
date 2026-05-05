#if !PLAYSERV_DISABLE_EVENTS
using System;

namespace Playserv.Events
{
    /// <summary>
    /// Marks class/struct as PlayServ event payload and optionally declares routing scope.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class EventAttribute : Attribute
    {
        /// <summary>
        /// Routing scope supported by this event payload.
        /// </summary>
        public EventType Type { get; }

        /// <summary>
        /// Creates event attribute with selected scope.
        /// </summary>
        /// <param name="type">Event routing scope. Default is <see cref="EventType.All"/>.</param>
        public EventAttribute(EventType type = EventType.All)
        {
            Type = type;
        }
    }
}

#endif
