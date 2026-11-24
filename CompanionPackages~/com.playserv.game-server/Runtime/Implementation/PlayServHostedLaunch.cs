using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GameServer
{
    /// <summary>Validated, immutable launch metadata. Credentials are deliberately not exposed.</summary>
    public sealed class PlayServHostedLaunch
    {
        private readonly string _attributes;
        internal PlayServHostedLaunch(string api, string executor, string deployment, string room, int port,
            PlayServGameRoomConnect connect, string attributes, IPlayServUplinkCredentialProvider credential)
        {
            ApiUrl = api; ExecutorSlug = executor; DeploymentId = deployment; RoomName = room;
            ListenPort = port; Connect = connect; _attributes = attributes; Credential = credential;
        }
        public string ApiUrl { get; }
        public string ExecutorSlug { get; }
        public string DeploymentId { get; }
        public string RoomName { get; }
        public int ListenPort { get; }
        public PlayServGameRoomConnect Connect { get; }
        public object Attributes => RoomCreateAttributes.Read<Dictionary<string, object>>(_attributes);
        internal IPlayServUplinkCredentialProvider Credential { get; }
    }

    public static partial class PlayServServerLaunch
    {
        /// <summary>Reads a platform launch without opening a connection or a listener.</summary>
        public static PlayServHostedLaunch ReadHosted(string transport = "udp")
        {
            PlayServGameServer.EnsureSupportedBuildForServices();
            return ReadHosted(transport, Environment.GetEnvironmentVariable, path => File.Exists(path) ? File.ReadAllText(path) : null);
        }

        internal static PlayServHostedLaunch ReadHosted(string transport, Func<string, string> environment, Func<string, string> readFile)
        {
            if (transport != "udp" && transport != "tcp" && transport != "ws" && transport != "wss")
                throw InvalidHostedField("transport");
            var room = GetRoomName(null, environment);
            if (room == null) throw InvalidHostedField("PLAYSERV_ROOM_NAME (not a platform room launch)");
            var api = environment("PLAYSERV_API_URL");
            if (!Uri.TryCreate(api, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw InvalidHostedField("PLAYSERV_API_URL");
            var executor = environment("PLAYSERV_EXECUTOR_SLUG");
            if (executor == null || !Regex.IsMatch(executor, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z"))
                throw InvalidHostedField("PLAYSERV_EXECUTOR_SLUG");
            if (!int.TryParse(environment("PLAYSERV_ROOM_LISTEN_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                port < 1 || port > 65535) throw InvalidHostedField("PLAYSERV_ROOM_LISTEN_PORT");
            var connect = ResolveConnect(port, null, environment, transport);
            if (connect == null) throw InvalidHostedField("PLAYSERV_PUBLIC_HOST / PLAYSERV_PUBLIC_IP");
            string attributes;
            try
            {
                var json = environment("PLAYSERV_ROOM_ATTRIBUTES") ?? "{}";
                attributes = RoomCreateAttributes.Capture(RoomCreateAttributes.Read<object>(json));
                if (attributes == null) throw new FormatException();
            }
            catch { throw InvalidHostedField("PLAYSERV_ROOM_ATTRIBUTES"); }
            var credential = new HostedCredential(environment, readFile, room);
            var deployment = credential.ReadIdentity();
            var stated = environment("PLAYSERV_DEPLOYMENT_ID");
            if (stated != null && stated != deployment) throw InvalidHostedField("PLAYSERV_DEPLOYMENT_ID");
            credential.DeploymentId = deployment;
            return new PlayServHostedLaunch(api, executor, deployment, room, port, connect, attributes, credential);
        }

        internal static ArgumentException InvalidHostedField(string field) =>
            new ArgumentException("Hosted launch field " + field + " is missing or invalid.");

        private sealed class HostedCredential : IPlayServUplinkCredentialProvider
        {
            private readonly Func<string, string> _environment, _readFile;
            private readonly string _room;
            internal string DeploymentId;
            internal HostedCredential(Func<string, string> environment, Func<string, string> readFile, string room)
            { _environment = environment; _readFile = readFile; _room = room; }
            internal string ReadIdentity() { Read(out var identity); return identity; }
            public Task<string> GetCredentialAsync(CancellationToken cancellationToken = default)
            { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Read(out _)); }
            private string Read(out string identity)
            {
                try
                {
                    var token = _environment("PLAYSERV_DEPLOYMENT_TOKEN")?.Trim();
                    if (string.IsNullOrEmpty(token)) token = _readFile("/run/playserv/deployment-token")?.Trim();
                    if (string.IsNullOrEmpty(token) || token.Length > 16384 || Regex.IsMatch(token, @"\s|\p{Cc}")) throw new FormatException();
                    var parts = token.Split('.');
                    if (parts.Length != 3 || Array.Exists(parts, p => !Regex.IsMatch(p, @"\A[A-Za-z0-9_-]+\z"))) throw new FormatException();
                    var payload = parts[1].Replace('-', '+').Replace('_', '/');
                    payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                    var claims = RoomCreateAttributes.Read<Dictionary<string, object>>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                    if (claims == null || !claims.TryGetValue("sub", out var sub) || !(sub is string subject) ||
                        !subject.StartsWith("executor/", StringComparison.Ordinal) ||
                        !claims.TryGetValue("deployment_id", out var id) || !(id is string deployment) ||
                        !Regex.IsMatch(deployment, @"\A[A-Za-z0-9][A-Za-z0-9._:-]{0,63}\z") ||
                        (DeploymentId != null && DeploymentId != deployment) ||
                        (claims.TryGetValue("room_name", out var room) && room != null && !Equals(room, _room))) throw new FormatException();
                    identity = deployment;
                    return token;
                }
                catch { throw InvalidHostedField("deployment credential"); }
            }
        }
    }
}
