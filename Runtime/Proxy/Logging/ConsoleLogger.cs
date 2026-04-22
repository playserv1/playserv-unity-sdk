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
        private readonly string _prefix;

        public ConsoleLogger()
            : this(PlayServLogCategory.General)
        {
        }

        public ConsoleLogger(PlayServLogCategory category)
        {
            _prefix = BuildPrefix(category);
        }

        /// <summary>
        /// Writes informational message.
        /// </summary>
        /// <param name="message">Message text.</param>
        public void Log(string message)
        {
            var formattedMessage = FormatMessage(message);
#if UNITY_5_3_OR_NEWER
            Debug.Log(formattedMessage);
#else
            Console.WriteLine(formattedMessage);
#endif
        }

        /// <summary>
        /// Writes warning message.
        /// </summary>
        /// <param name="message">Message text.</param>
        public void LogWarning(string message)
        {
            var formattedMessage = FormatMessage(message);
#if UNITY_5_3_OR_NEWER
            Debug.LogWarning(formattedMessage);
#else
            Console.WriteLine($"[WARN] {formattedMessage}");
#endif
        }

        /// <summary>
        /// Writes error message.
        /// </summary>
        /// <param name="message">Message text.</param>
        public void LogError(string message)
        {
            var formattedMessage = FormatMessage(message);
#if UNITY_5_3_OR_NEWER
            Debug.LogError(formattedMessage);
#else
            Console.Error.WriteLine($"[ERROR] {formattedMessage}");
#endif
        }

        private string FormatMessage(string message)
        {
            var normalized = StripLegacyPrefixes(message);
            return string.IsNullOrWhiteSpace(normalized)
                ? _prefix
                : $"{_prefix} {normalized}";
        }

        private static string BuildPrefix(PlayServLogCategory category)
        {
            return $"[PlayServ][{category.ToString().ToLowerInvariant()}]";
        }

        private static string StripLegacyPrefixes(string message)
        {
            var value = (message ?? string.Empty).Trim();
            while (value.StartsWith("[", StringComparison.Ordinal))
            {
                var end = value.IndexOf(']');
                if (end <= 0)
                    break;

                value = value.Substring(end + 1).TrimStart();
            }

            return value;
        }
    }
}
