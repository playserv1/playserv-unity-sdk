using System;
using Playserv.Serialization;

namespace Playserv.Status
{
    /// <summary>Credential-free project health snapshot. It does not authorize access to project data.</summary>
    [Serializable]
    public sealed class PlayServProjectStatus
    {
        [PlayServJsonName("project_slug")] public string ProjectSlug { get; set; }
        [PlayServJsonName("generated_at")] public DateTimeOffset GeneratedAt { get; set; }
        public PlayServProjectFunctionStatus[] Functions { get; set; }
    }

    [Serializable]
    public sealed class PlayServProjectFunctionStatus
    {
        [PlayServJsonName("env")] public string Environment { get; set; }
        public string Slug { get; set; }
        [PlayServJsonName("function_id")] public string FunctionId { get; set; }
        public string Kind { get; set; }
        [PlayServJsonName("deploy_status")] public string DeployStatus { get; set; }
        public bool Enabled { get; set; }
        public string Revision { get; set; }
        [PlayServJsonName("revision_ready")] public bool? RevisionReady { get; set; }
        [PlayServJsonName("deployed_at")] public DateTimeOffset? DeployedAt { get; set; }
        /// <summary>Null means no counters are available, not zero invocations.</summary>
        public PlayServFunctionStatusCounters Counters { get; set; }
        /// <summary>Null means no probe evidence is available.</summary>
        public PlayServFunctionStatusProbe Probe { get; set; }
        /// <summary>Null means latency evidence is unavailable.</summary>
        public PlayServFunctionStatusLatency Latency { get; set; }
    }

    [Serializable]
    public sealed class PlayServFunctionStatusCounters
    {
        public DateTimeOffset Since { get; set; }
        [PlayServJsonName("series_id")] public string SeriesId { get; set; }
        public long Invocations { get; set; }
        public long Failures { get; set; }
        public long Timeouts { get; set; }
        [PlayServJsonName("upstream_5xx")] public long Upstream5xx { get; set; }
        [PlayServJsonName("last_success_at")] public DateTimeOffset? LastSuccessAt { get; set; }
        [PlayServJsonName("last_failure_at")] public DateTimeOffset? LastFailureAt { get; set; }
        [PlayServJsonName("last_duration_ms")] public int? LastDurationMs { get; set; }
        [PlayServJsonName("last_overhead_ms")] public int? LastOverheadMs { get; set; }
    }

    [Serializable]
    public sealed class PlayServFunctionStatusProbe
    {
        [PlayServJsonName("last_at")] public DateTimeOffset LastAt { get; set; }
        public bool Ok { get; set; }
    }

    [Serializable]
    public sealed class PlayServFunctionStatusLatency
    {
        [PlayServJsonName("p50_ms")] public double? P50Ms { get; set; }
        [PlayServJsonName("p95_ms")] public double? P95Ms { get; set; }
        [PlayServJsonName("p99_ms")] public double? P99Ms { get; set; }
        public PlayServFunctionStatusLatencyPoint[] Series { get; set; }
    }

    [Serializable]
    public sealed class PlayServFunctionStatusLatencyPoint
    {
        public DateTimeOffset At { get; set; }
        [PlayServJsonName("p95_ms")] public double? P95Ms { get; set; }
    }
}
