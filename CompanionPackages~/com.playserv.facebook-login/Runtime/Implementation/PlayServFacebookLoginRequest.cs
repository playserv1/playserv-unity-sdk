using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Playserv.FacebookLogin
{
    /// <summary>Permissions and optional nonce for a Facebook Limited Login request.</summary>
    public sealed class PlayServFacebookLoginRequest
    {
        public PlayServFacebookLoginRequest(
            IEnumerable<string> permissions = null,
            string nonce = null)
        {
            var normalized = (permissions ?? new[] { "public_profile" })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (normalized.Length == 0)
                throw new ArgumentException("At least one Facebook permission is required.", nameof(permissions));

            Permissions = new ReadOnlyCollection<string>(normalized);
            Nonce = nonce?.Trim() ?? string.Empty;
        }

        public IReadOnlyList<string> Permissions { get; }

        /// <summary>Optional raw Limited Login nonce. A secure nonce is generated when empty.</summary>
        public string Nonce { get; }
    }
}
