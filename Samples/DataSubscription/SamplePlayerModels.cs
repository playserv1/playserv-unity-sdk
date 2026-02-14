using System;

namespace Playserv.Samples
{
    /// <summary>
    /// Demo server-side player model used by SelectEntity mapping.
    /// </summary>
    [Serializable]
    public sealed class SamplePlayerEntity
    {
        public string Id;
        public string Name;
        public int Level;
    }

    /// <summary>
    /// Demo client DTO projected from SamplePlayerEntity.
    /// </summary>
    public sealed class SamplePlayerDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; }
    }
}
