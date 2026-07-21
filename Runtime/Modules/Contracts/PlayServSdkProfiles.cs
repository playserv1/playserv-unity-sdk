using System;
using System.Collections.Generic;

namespace Playserv.Modules
{
    public static class PlayServSdkProfiles
    {
        public const string ClientSdkId = "client-sdk";
        public const string ServerSdkId = "server-sdk";
        public const string FullSdkId = "full-sdk";
        public const string CoreOnlyId = "core-only";

        private static readonly PlayServSdkProfile[] Profiles =
        {
            new PlayServSdkProfile(
                ClientSdkId,
                "Client SDK",
                "Client gameplay package. Server-only runtime modules are excluded.",
                new[]
                {
                    PlayServModuleManifest.ClientExecutionId,
                    PlayServModuleManifest.EventsId,
                    PlayServModuleManifest.DataSubscriptionId,
                    PlayServModuleManifest.ClientRpcId,
                    PlayServModuleManifest.SpawnId,
                    PlayServModuleManifest.PulseId,
                    PlayServModuleManifest.TransportWebSocketId,
                    PlayServModuleManifest.TransportUdpId,
                    PlayServModuleManifest.TransportRudpId,
                    PlayServModuleManifest.TransportWebRtcId
                }),

            new PlayServSdkProfile(
                ServerSdkId,
                "Server SDK",
                "Server runtime package. Client gameplay facades are excluded.",
                new[]
                {
                    PlayServModuleManifest.ServerId
                }),

            new PlayServSdkProfile(
                FullSdkId,
                "Full SDK",
                "Client and server runtime modules in one package.",
                new[]
                {
                    PlayServModuleManifest.ClientExecutionId,
                    PlayServModuleManifest.EventsId,
                    PlayServModuleManifest.DataSubscriptionId,
                    PlayServModuleManifest.ClientRpcId,
                    PlayServModuleManifest.ServerId,
                    PlayServModuleManifest.SpawnId,
                    PlayServModuleManifest.PulseId,
                    PlayServModuleManifest.AppleSignInId,
                    PlayServModuleManifest.TransportWebSocketId,
                    PlayServModuleManifest.TransportUdpId,
                    PlayServModuleManifest.TransportRudpId,
                    PlayServModuleManifest.TransportWebRtcId
                }),

            new PlayServSdkProfile(
                CoreOnlyId,
                "Core Only",
                "Base config, serialization, and module contracts only.",
                Array.Empty<string>())
        };

        public static IReadOnlyList<PlayServSdkProfile> All => Profiles;

        public static PlayServSdkProfile ClientSdk => Profiles[0];

        public static PlayServSdkProfile ServerSdk => Profiles[1];

        public static PlayServSdkProfile FullSdk => Profiles[2];

        public static PlayServSdkProfile CoreOnly => Profiles[3];

        public static bool TryGet(string profileId, out PlayServSdkProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(profileId))
                return false;

            for (var i = 0; i < Profiles.Length; i++)
            {
                if (!string.Equals(Profiles[i].Id, profileId, StringComparison.Ordinal))
                    continue;

                profile = Profiles[i];
                return true;
            }

            return false;
        }

        public static PlayServSdkProfile GetRequired(string profileId)
        {
            if (TryGet(profileId, out var profile))
                return profile;

            throw new ArgumentException($"Unknown PlayServ SDK profile: {profileId}", nameof(profileId));
        }
    }
}
