using System;

namespace Playserv.Events
{
    /// <summary>
    /// Defines routing scope for PlayServ events.
    /// </summary>
    [Flags]
    public enum EventType
    {
        /// <summary>
        /// Event is broadcast globally.
        /// </summary>
        Global = 1,

        /// <summary>
        /// Event is scoped to a group.
        /// </summary>
        Group = 2,

        /// <summary>
        /// Event is scoped to a single user.
        /// </summary>
        User = 4,

        /// <summary>
        /// Event supports all scopes.
        /// </summary>
        All = Global | Group | User
    }
}
