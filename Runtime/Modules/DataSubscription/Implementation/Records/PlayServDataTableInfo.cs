using System;
using Playserv.Wrapper;

namespace Playserv.Data
{
    /// <summary>Runtime-visible table metadata returned by the existing data catalogue.</summary>
    public sealed class PlayServDataTableInfo
    {
        internal PlayServDataTableInfo(
            string entityId,
            string name,
            string description,
            bool isSingleton,
            long rowCount,
            DateTimeOffset updatedAt,
            string readPolicy,
            PlayServDataCapabilities capabilities)
        {
            EntityId = entityId ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            IsSingleton = isSingleton;
            RowCount = rowCount;
            UpdatedAt = updatedAt;
            ReadPolicy = readPolicy ?? string.Empty;
            Capabilities = capabilities ?? PlayServDataCapabilities.Unknown;
        }

        public string EntityId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool IsSingleton { get; }
        public long RowCount { get; }
        public DateTimeOffset UpdatedAt { get; }
        public string ReadPolicy { get; }
        public PlayServDataCapabilities Capabilities { get; }
    }

    /// <summary>Raised when an ID/name is absent from the caller-visible table catalogue.</summary>
    public sealed class PlayServDataTableNotFoundException : PlayServDataException
    {
        internal PlayServDataTableNotFoundException(string idOrName)
            : base(
                $"No runtime data table matching '{idOrName}' is visible.",
                404,
                "table_not_found")
        {
            IdOrName = idOrName ?? string.Empty;
        }

        public string IdOrName { get; }
    }
}
