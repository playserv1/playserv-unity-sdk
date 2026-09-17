using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Data
{
    /// <summary>Presence in the caller-visible catalogue, not field compatibility or physical existence.</summary>
    public enum PlayServSchemaAvailability { Visible, NotVisible, Unavailable }

    /// <summary>An immutable, non-blocking schema advisory for one explicitly supplied CLR type.</summary>
    public sealed class PlayServSchemaAdvisory
    {
        public Type EntityType { get; }
        public string EntityName => EntityType.Name;
        public PlayServSchemaAvailability Availability { get; }
        public PlayServDataTableInfo Table { get; }
        /// <summary>Safe diagnostic code; never contains an HTTP response or credential.</summary>
        public string Reason { get; }
        internal PlayServSchemaAdvisory(Type type, PlayServSchemaAvailability availability,
            PlayServDataTableInfo table = null, string reason = null)
        { EntityType = type; Availability = availability; Table = table; Reason = reason; }
    }

    internal sealed partial class PlayServRecordsClient
    {
        internal async Task<IReadOnlyList<PlayServSchemaAdvisory>> CheckSchemaAsync(
            IEnumerable<Type> entityTypes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (entityTypes == null) throw new ArgumentNullException(nameof(entityTypes));
            var types = entityTypes.ToArray();
            if (types.Any(type => type == null || type.ContainsGenericParameters))
                throw new ArgumentException("Supply non-null, closed entity types.", nameof(entityTypes));
            if (types.Length == 0) return Array.Empty<PlayServSchemaAdvisory>();
            PlayServTableCatalogue catalogue;
            try { catalogue = await GetCatalogueAsync(true, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                ct.ThrowIfCancellationRequested();
                return Array.AsReadOnly(types.Select(type => new PlayServSchemaAdvisory(type,
                    PlayServSchemaAvailability.Unavailable, reason: "catalogue_unavailable")).ToArray());
            }
            ct.ThrowIfCancellationRequested();
            return Array.AsReadOnly(types.Select(type =>
            {
                var matches = MatchTypeName(catalogue, type.Name);
                if (matches.Count == 1) return new PlayServSchemaAdvisory(type,
                    PlayServSchemaAvailability.Visible, matches[0].Info);
                return new PlayServSchemaAdvisory(type, matches.Count == 0
                    ? PlayServSchemaAvailability.NotVisible : PlayServSchemaAvailability.Unavailable,
                    reason: matches.Count == 0 ? "not_in_visible_catalogue" : "ambiguous_type_name");
            }).ToArray());
        }
    }
}
