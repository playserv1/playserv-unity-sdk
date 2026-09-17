using System;
using System.Collections.Generic;

namespace Playserv.Http.Interfaces
{
    [Serializable]
    public sealed class PlayerExternalLoginRequestDto
    {
        public string provider;
        public string provider_token;
        public string mode;
        public string nonce;
        public PlayerFingerprintDto fingerprint;
        /// <summary>Optional creation/first-link name; omit when blank. Wire key is exactly display_name.</summary>
        public string display_name;
    }

    [Serializable]
    public sealed class PlayerFingerprintDto
    {
        public Dictionary<string, object> stable;
        public Dictionary<string, object> soft;
    }

    [Serializable]
    public sealed class PlayerAnonymousLoginRequestDto
    {
        public PlayerFingerprintDto fingerprint;
        /// <summary>Optional name of a newly created player; trim/cap at 64 UTF-16 units and omit when blank.</summary>
        public string display_name;
    }

    [Serializable]
    public sealed class PlayerLinkRequestDto
    {
        public string provider;
        public string provider_token;
        public string mode;
        public string nonce;
        /// <summary>Optional first-link profile hint, not a rename. Wire key is exactly display_name.</summary>
        public string display_name;
    }

    [Serializable]
    public sealed class PlayerUnlinkRequestDto
    {
        public string provider;
    }

    [Serializable]
    public sealed class PlayerMergeRequestDto
    {
        public string primary_plr_id;
        public string absorbed_plr_id;
        public string provider;
        public string provider_token;
        public string mode;
        public string nonce;
    }

    [Serializable]
    public sealed class PlayerAuthProviderAvailabilityDto
    {
        public string id;
        public string label;
        public bool enabled;
        public string connectivity;
        public bool available;
    }

    [Serializable]
    public sealed class ResolvedProjectDto
    {
        public string id;
        public string slug;
        public string env;
    }

    [Serializable]
    public sealed class PlayerAuthProvidersProbeDto
    {
        public ResolvedProjectDto project;
        public PlayerAuthProviderAvailabilityDto[] providers;
    }

    [Serializable]
    public sealed class PlayerAuthConflictPartyDto
    {
        public string id;
        public string kind;
        public string joined;
        public string last_seen;
    }

    [Serializable]
    public sealed class PlayerAuthConflictDto
    {
        public string error;
        public string provider;
        public PlayerAuthConflictPartyDto current;
        public PlayerAuthConflictPartyDto conflicting;
        public string[] providers;
    }

    [Serializable]
    public sealed class PlayServProblemDetailsDto
    {
        public string type;
        public string title;
        public int status;
        public string detail;
        public string instance;
        public string code;
        public string error;
        public string provider;
        public string[] providers;
    }
}
