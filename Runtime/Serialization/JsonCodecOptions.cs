using System.Collections.Generic;

namespace Playserv.Serialization
{
    public sealed class JsonCodecOptions
    {
        public bool ParseDates { get; set; } = true;

        public bool IncludeNullValues { get; set; } = true;

        public bool IgnoreMissingMembers { get; set; } = true;

        public IReadOnlyList<object> CustomConverters { get; set; }
    }
}
