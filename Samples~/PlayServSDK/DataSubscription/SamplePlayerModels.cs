using System;

namespace Playserv.Samples
{
    /// <summary>
    /// Local mirror of schema entity "Player" used by DataGet/Subscribe sample.
    /// Type name must stay "Player" so query generation maps to schema entity.
    /// </summary>
    [Serializable]
    public sealed class Player
    {
        public string Nickname;
        public int? Level;
    }

    /// <summary>
    /// Client DTO projected from schema Player and updated by polling subscription.
    /// </summary>
    public sealed class SamplePlayerDto
    {
        public string Nickname { get; set; } = string.Empty;
        public int Level { get; set; }
    }
}
