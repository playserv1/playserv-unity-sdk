using System;

namespace Playserv.Events
{
    [Flags]
    public enum EventType
    {
        Global = 1,
        Group = 2,
        User = 4,
        All = Global | Group | User
    }
}
