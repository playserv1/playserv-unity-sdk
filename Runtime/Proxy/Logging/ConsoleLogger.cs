using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Playserv.Proxy.Logging
{
    public sealed class ConsoleLogger : ILogger
    {
        public void Log(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

        public void LogWarning(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.LogWarning(message);
#else
            Console.WriteLine($"[WARN] {message}");
#endif
        }

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
