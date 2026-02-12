using System;

namespace Playserv.Shared
{
    /// <summary>
    /// Minimal contract for generated shared value wrappers.
    /// </summary>
    /// <typeparam name="T">Wrapped value type.</typeparam>
    public interface IShared<T>
    {
        /// <summary>
        /// Current value snapshot.
        /// </summary>
        T Value { get; }

        /// <summary>
        /// Raised when value changes.
        /// </summary>
        event Action Changed;
    }
}
