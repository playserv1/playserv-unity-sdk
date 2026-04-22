using System.Diagnostics;

namespace Playserv.Proxy.Logging
{
    /// <summary>
    /// Central logging facade used by runtime, transport and wrapper layers.
    /// </summary>
    public static class PlayServLog
    {
        private static readonly ILogger GeneralLogger = new ConsoleLogger(PlayServLogCategory.General);
        private static readonly ILogger TransportLogger = new ConsoleLogger(PlayServLogCategory.Transport);
        private static readonly ILogger HttpLogger = new ConsoleLogger(PlayServLogCategory.Http);
        private static readonly ILogger RpcLogger = new ConsoleLogger(PlayServLogCategory.Rpc);
        private static readonly ILogger SpawnLogger = new ConsoleLogger(PlayServLogCategory.Spawn);
        private static readonly ILogger EventsLogger = new ConsoleLogger(PlayServLogCategory.Events);
        private static readonly ILogger DataLogger = new ConsoleLogger(PlayServLogCategory.Data);

        public static ILogger ForCategory(PlayServLogCategory category)
        {
            switch (category)
            {
                case PlayServLogCategory.Transport:
                    return TransportLogger;
                case PlayServLogCategory.Http:
                    return HttpLogger;
                case PlayServLogCategory.Rpc:
                    return RpcLogger;
                case PlayServLogCategory.Spawn:
                    return SpawnLogger;
                case PlayServLogCategory.Events:
                    return EventsLogger;
                case PlayServLogCategory.Data:
                    return DataLogger;
                default:
                    return GeneralLogger;
            }
        }

        public static void Info(PlayServLogCategory category, string message)
        {
            ForCategory(category).Log(message);
        }

        public static void Warning(PlayServLogCategory category, string message)
        {
            ForCategory(category).LogWarning(message);
        }

        public static void Error(PlayServLogCategory category, string message)
        {
            ForCategory(category).LogError(message);
        }

        [Conditional("PlayServ_Logs")]
        public static void Trace(PlayServLogCategory category, string message)
        {
            ForCategory(category).Log(message);
        }

        [Conditional("PlayServ_Logs")]
        public static void TraceWarning(PlayServLogCategory category, string message)
        {
            ForCategory(category).LogWarning(message);
        }
    }
}
