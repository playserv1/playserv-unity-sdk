using System;

namespace Playserv.Shared
{
    public interface IShared<T>
    {
        T Value { get; }

        event Action Changed;
    }
}