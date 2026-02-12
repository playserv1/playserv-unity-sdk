using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Proxy.Logging
{
    /// <summary>
    /// Logger implementation that writes to Unity console or standard output.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        /// <summary>
        /// Writes informational message.
        /// </summary>
        /// <param name="message">Message text.</param>
        public void Log(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

        /// <summary>
        /// Writes warning message.
        /// </summary>
        /// <param name="message">Message text.</param>
        public void LogWarning(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.LogWarning(message);
#else
            Console.WriteLine($"[WARN] {message}");
#endif
        }

        /// <summary>
        /// Writes error message.
        /// </summary>
        /// <param name="message">Message text.</param>
        public void LogError(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.LogError(message);
#else
            Console.Error.WriteLine($"[ERROR] {message}");
#endif
        }
    }
}
