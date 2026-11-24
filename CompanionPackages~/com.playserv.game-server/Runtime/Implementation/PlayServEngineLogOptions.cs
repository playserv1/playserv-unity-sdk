using System;
using Playserv.Wrapper;

namespace Playserv.GameServer
{
    /// <summary>Opt-in process-wide engine capture. Configure snapshots these values; clients never capture.</summary>
    public sealed class PlayServEngineLogOptions
    {
        public bool Enabled { get; set; }
        public PlayServServerLogLevel MinimumLevel { get; set; } = PlayServServerLogLevel.Info;
        /// <summary>Include the engine stack for Error, Exception and Assert, within the shared text budget.</summary>
        public bool IncludeStackTrace { get; set; } = true;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(250);
        public TimeSpan RateWindow { get; set; } = TimeSpan.FromSeconds(10);
        public int MaxEntriesPerWindow { get; set; } = 200;
        /// <summary>Combined UTF-8 message and stack budget, applied after credential redaction.</summary>
        public int MaxTextBytes { get; set; } = 16 * 1024;

        internal PlayServEngineLogOptions Copy() => (PlayServEngineLogOptions)MemberwiseClone();
        internal PlayServEngineLogOptions Snapshot()
        {
            var copy = Copy();
            if (copy.MinimumLevel < PlayServServerLogLevel.Debug || copy.MinimumLevel > PlayServServerLogLevel.Error ||
                copy.FlushInterval < TimeSpan.FromMilliseconds(1) || copy.FlushInterval.TotalMilliseconds > int.MaxValue ||
                copy.RateWindow <= TimeSpan.Zero || copy.RateWindow.TotalMilliseconds > int.MaxValue ||
                copy.MaxEntriesPerWindow < 1 || copy.MaxTextBytes < 4 || copy.MaxTextBytes > PlayServUplinkSocket.MaxFrameBytes)
                throw new ArgumentOutOfRangeException(nameof(PlayServGameServerOptions.EngineLogs), "Engine log limits and durations must be valid positive values.");
            return copy;
        }
    }

    /// <summary>Local capture accounting for this configuration; no raw engine text or credentials.</summary>
    public sealed class PlayServEngineLogDiagnostics
    {
        internal PlayServEngineLogDiagnostics(bool active = false, long accepted = 0, long filtered = 0,
            long rateLimited = 0, long queueRejected = 0, long failed = 0, PlayServError error = null)
        {
            IsCapturing = active; AcceptedCount = accepted; FilteredCount = filtered; RateLimitedCount = rateLimited;
            QueueRejectedCount = queueRejected; FailedCount = failed; LastError = error ?? PlayServError.None;
        }
        public bool IsCapturing { get; }
        public long AcceptedCount { get; }
        public long FilteredCount { get; }
        public long RateLimitedCount { get; }
        public long QueueRejectedCount { get; }
        public long FailedCount { get; }
        public PlayServError LastError { get; }
    }
}
