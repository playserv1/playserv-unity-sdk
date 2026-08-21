using System;
using System.Collections.Generic;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Status
{
    /// <summary>Known values returned by the platform health evaluator.</summary>
    public static class PlayServPlatformHealthStatuses
    {
        public const string Green = "green";
        public const string Yellow = "yellow";
        public const string Red = "red";
    }

    [Serializable]
    public sealed class PlayServPlatformStatusItem
    {
        public string Pop { get; set; }
        public string System { get; set; }

        [PlayServJsonName("current_status")]
        public string CurrentStatus { get; set; }

        [PlayServJsonName("last_status")]
        public string LastStatus { get; set; }

        [PlayServJsonName("last_signal")]
        public string LastSignal { get; set; }

        [PlayServJsonName("last_probe_at")]
        public DateTimeOffset LastProbeAt { get; set; }

        [PlayServJsonName("seconds_since_probe")]
        public int SecondsSinceProbe { get; set; }
    }

    [Serializable]
    public sealed class PlayServPlatformStatus
    {
        [PlayServJsonName("generated_at")]
        public DateTimeOffset GeneratedAt { get; set; }

        public string Overall { get; set; }

        public PlayServPlatformStatusItem[] Items { get; set; } =
            Array.Empty<PlayServPlatformStatusItem>();
    }

    [Serializable]
    public sealed class PlayServPlatformStatusDay
    {
        /// <summary>UTC calendar date in ISO <c>yyyy-MM-dd</c> format.</summary>
        public string Day { get; set; }

        [PlayServJsonName("availability_pct")]
        public decimal AvailabilityPercent { get; set; }

        [PlayServJsonName("green_minutes")]
        public int GreenMinutes { get; set; }

        [PlayServJsonName("yellow_minutes")]
        public int YellowMinutes { get; set; }

        [PlayServJsonName("red_minutes")]
        public int RedMinutes { get; set; }
    }

    [Serializable]
    public sealed class PlayServPlatformStatusSystemHistory
    {
        public string System { get; set; }

        public PlayServPlatformStatusDay[] Days { get; set; } =
            Array.Empty<PlayServPlatformStatusDay>();
    }

    [Serializable]
    public sealed class PlayServPlatformStatusHistory
    {
        public string Pop { get; set; }

        [PlayServJsonName("days_requested")]
        public int DaysRequested { get; set; }

        public PlayServPlatformStatusSystemHistory[] Systems { get; set; } =
            Array.Empty<PlayServPlatformStatusSystemHistory>();
    }

    public sealed class PlayServStatusFederation
    {
        internal PlayServStatusFederation(IReadOnlyList<Uri> origins)
        {
            Origins = origins ?? Array.Empty<Uri>();
        }

        /// <summary>
        /// Validated HTTP(S) peer origins. The SDK does not contact them
        /// automatically; the game decides whether and when to federate.
        /// </summary>
        public IReadOnlyList<Uri> Origins { get; }
    }

    /// <summary>Structured public status transport or response failure.</summary>
    public sealed class PlayServStatusException : Exception
    {
        internal PlayServStatusException(
            PlayServError error,
            Exception innerException = null)
            : base(error?.Message ?? "PlayServ platform status request failed.", innerException)
        {
            UnifiedError = error ?? new PlayServError(
                PlayServErrorCode.Unknown,
                "status_unknown",
                Message);
        }

        public PlayServError UnifiedError { get; }
    }
}
