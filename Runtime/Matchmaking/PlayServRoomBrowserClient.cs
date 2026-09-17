using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Serialization;
using Playserv.Wrapper;

namespace Playserv.Matchmaking
{
    internal sealed partial class PlayServMatchmakingClient
    {
        private static readonly Regex RoomTypeSlug = new Regex(@"\A[a-z][a-z0-9-]{2,49}\z", RegexOptions.CultureInvariant);
        private static readonly Regex RoomNamePattern = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9:._-]{0,63}\z", RegexOptions.CultureInvariant);
        private static readonly Regex CredentialText = new Regex(
            @"\bBearer\s+[^\s,"";]+|\b(?:pk|sk|rsv)_[A-Za-z0-9._~-]+|(?<![A-Za-z0-9_-])(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]+\.(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?![A-Za-z0-9_-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal async Task<PlayServRoomBrowsePage> BrowseRoomsAsync(
            string functionSlug, PlayServRoomBrowseQuery query, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateRoomType(functionSlug);
            // Materialize the whole query before token resolution can yield to game code.
            var path = BuildBrowsePath(functionSlug, query ?? new PlayServRoomBrowseQuery());
            var bearer = await ResolvePlayerTokenAsync(ct);
            var response = await SendMatchmakingAsync(new PlayServRuntimeDataRequest
            {
                Method = "GET", RelativePath = path,
                ClientToken = _settings.ClientToken, BearerToken = bearer,
                TimeoutSeconds = DefaultRequestTimeoutSeconds
            }, PlayServMatchmakingOperation.BrowseRooms, functionSlug, ct);

            try
            {
                var wire = _json.Deserialize<BrowseResponseWire>(response?.Body);
                if (wire?.data == null || wire.page == null ||
                    (wire.page.has_more && string.IsNullOrEmpty(wire.page.cursor_next)))
                    throw new FormatException();
                var rows = new List<PlayServRoomListing>(wire.data.Count);
                foreach (var row in wire.data)
                {
                    if (row == null || string.IsNullOrEmpty(row.room_name) ||
                        !row.players.HasValue || row.players < 0 || !row.capacity.HasValue || row.capacity <= 0 ||
                        string.IsNullOrEmpty(row.placement_state))
                        throw new FormatException();
                    rows.Add(new PlayServRoomListing(row.room_name, row.players.Value, row.capacity.Value,
                        row.state, row.placement_state, row.region, FreezeAttributes(row.attributes), ToConnect(row.connect)));
                }
                return new PlayServRoomBrowsePage(rows.AsReadOnly(), wire.page.cursor_next, wire.page.has_more);
            }
            catch (Exception)
            {
                throw InvalidResponse(PlayServMatchmakingOperation.BrowseRooms, functionSlug,
                    "matchmaking_invalid_response", "Room browser returned an invalid page.");
            }
        }

        internal Task<PlayServMatchResult> JoinRoomAsync(string functionSlug, string roomName, CancellationToken ct) =>
            JoinRoomAsync(new PlayServJoinRoomRequest { FunctionSlug = functionSlug, RoomName = roomName }, ct);

        internal Task<PlayServMatchResult> JoinRoomAsync(PlayServJoinRoomRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));
            var functionSlug = request.FunctionSlug;
            var roomName = request.RoomName;
            ValidateRoomType(functionSlug);
            if (roomName == null || !RoomNamePattern.IsMatch(roomName))
                throw new ArgumentException("Room name must match the room API's 1–64 character identifier.", nameof(roomName));
            var parameters = SnapshotParameters(request.Params, nameof(request));
            var body = parameters == null ? null : _json.Serialize(new { @params = parameters });
            return JoinRoomCoreAsync(functionSlug, roomName, body, ct);
        }

        private async Task<PlayServMatchResult> JoinRoomCoreAsync(string functionSlug, string roomName, string body, CancellationToken ct)
        {
            var bearer = await ResolvePlayerTokenAsync(ct);
            var response = await SendMatchmakingAsync(new PlayServRuntimeDataRequest
            {
                Method = "POST",
                RelativePath = $"rooms/{Uri.EscapeDataString(functionSlug)}/{Uri.EscapeDataString(roomName)}:join",
                ClientToken = _settings.ClientToken, BearerToken = bearer,
                JsonBody = body,
                TimeoutSeconds = DefaultRequestTimeoutSeconds
            }, PlayServMatchmakingOperation.JoinRoom, functionSlug, ct);
            return ReadMatchResponse(response, PlayServMatchmakingOperation.JoinRoom, functionSlug, _monotonicSeconds());
        }

        private PlayServMatchResult ReadMatchResponse(PlayServRuntimeDataResponse response,
            PlayServMatchmakingOperation operation, string slug, double receivedAt)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.Body))
                throw InvalidResponse(operation, slug, "matchmaking_empty_response", "Matchmaking response body is empty.");
            try
            {
                // Newtonsoft ordinarily coerces strings/fractions to integers. TTL is an integer on the wire.
                var plain = operation == PlayServMatchmakingOperation.HostRoom
                    ? _json.ToPlainValue(_json.Deserialize<object>(response.Body, new JsonCodecOptions { ParseDates = false, IgnoreMetadataProperties = true }))
                    : _json.ParseToPlainValue(response.Body);
                if (operation == PlayServMatchmakingOperation.HostRoom) ValidateHostReservation(plain);
                if (_json.TryGetProperty(plain, "expires_in", true, out var ttl) && ttl != null &&
                    !(ttl is long) && !(ttl is int) && !(ttl is short) && !(ttl is byte))
                    throw new FormatException();
                var wire = _json.Deserialize<FindMatchResponseWire>(response.Body,
                    operation == PlayServMatchmakingOperation.HostRoom
                        ? new JsonCodecOptions { ParseDates = false, IgnoreMetadataProperties = true } : null);
                if (wire == null) throw new FormatException();
                return ToResult(wire, operation, slug, receivedAt);
            }
            catch (PlayServMatchmakingException) { throw; }
            catch (Exception)
            {
                throw InvalidResponse(operation, slug, "matchmaking_invalid_response", "Matchmaking response could not be parsed.");
            }
        }

        private static void ValidateRoomType(string slug)
        {
            if (slug == null || !RoomTypeSlug.IsMatch(slug))
                throw new ArgumentException("Room type must match [a-z][a-z0-9-]{2,49}.", nameof(slug));
        }

        private static string BuildBrowsePath(string slug, PlayServRoomBrowseQuery query)
        {
            var limit = query.Limit;
            var state = query.PlacementState;
            var region = query.Region;
            var cursor = query.Cursor;
            var attributes = query.Attributes;
            if (limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(nameof(query.Limit), "Limit must be 1–200.");
            if (state != null && state != "open" && state != "full" && state != "self_closed" &&
                state != "draining" && state != "session_closing")
                throw new ArgumentException("Unknown placement state filter.", nameof(query.PlacementState));
            if (region != null && region.Length > 32) throw new ArgumentException("Region must be at most 32 characters.", nameof(query.Region));
            if (attributes != null && attributes.Count > 4) throw new ArgumentException("At most four attribute filters are allowed.", nameof(query.Attributes));
            var pairs = new List<string> { "limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (state != null) pairs.Add("placement_state=" + Uri.EscapeDataString(state));
            if (region != null) pairs.Add("region=" + Uri.EscapeDataString(region));
            if (cursor != null) pairs.Add("cursor=" + Uri.EscapeDataString(cursor));
            if (attributes != null)
            {
                var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in attributes)
                {
                    if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                        throw new ArgumentException("Attribute filters require a nonempty key and a string value.", nameof(query.Attributes));
                    sorted.Add(pair.Key, pair.Value);
                }
                foreach (var pair in sorted)
                    pairs.Add(Uri.EscapeDataString("attributes." + pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
            }
            return "rooms/" + Uri.EscapeDataString(slug) + ":browse?" + string.Join("&", pairs);
        }

        private static PlayServRoomConnect ToConnect(RoomConnectWire wire)
        {
            if (wire == null) return null;
            if (string.IsNullOrEmpty(wire.host) || !wire.port.HasValue || wire.port < 1 || wire.port > 65535 ||
                string.IsNullOrEmpty(wire.transport)) throw new FormatException();
            return new PlayServRoomConnect(wire.host, wire.port.Value, wire.transport, wire.connect_string, wire.region);
        }

        private IReadOnlyDictionary<string, object> FreezeAttributes(Dictionary<string, object> attributes, bool preserveJsonValues = false)
        {
            if (attributes == null) return null;
            // Normalize codec-specific values without changing Host's literal strings or attribute names.
            var json = _json.Serialize(attributes);
            var plain = preserveJsonValues
                ? _json.ToPlainValue(_json.Deserialize<object>(json, new JsonCodecOptions { ParseDates = false, IgnoreMetadataProperties = true }))
                : _json.ParseToPlainValue(json);
            return (IReadOnlyDictionary<string, object>)Freeze(plain);
        }

        private static object Freeze(object value)
        {
            if (value is IDictionary dictionary)
            {
                var copy = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry pair in dictionary) copy.Add((string)pair.Key, Freeze(pair.Value));
                return new ReadOnlyDictionary<string, object>(copy);
            }
            if (value is IList list)
            {
                var copy = new List<object>(list.Count);
                foreach (var item in list) copy.Add(Freeze(item));
                return copy.AsReadOnly();
            }
            return value;
        }

        private string ReadProblemDetail(string body)
        {
            try
            {
                return _json.TryGetProperty(_json.ParseToPlainValue(body), "detail", true, out var value)
                    ? value as string : null;
            }
            catch { return null; }
        }

        private static PlayServMatchmakingException HttpFailure(PlayServRuntimeDataRequest request,
            PlayServMatchmakingOperation operation, string slug, int status, string code, string detail,
            bool network, bool timeout, bool? retryable)
        {
            code = SafeDiagnostic(code, request);
            detail = SafeDiagnostic(detail, request);
            var roomCode = PlayServMatchmakingException.MapRoomFailure(code);
            if (roomCode != PlayServRoomFailureCode.Unknown &&
                roomCode <= PlayServRoomFailureCode.RoomUnreachable)
                retryable = roomCode == PlayServRoomFailureCode.RoomUnreachable;
            // Do not retain arbitrary response bodies or an inner HTTP exception: either can echo a ticket.
            return new PlayServMatchmakingException(operation, slug, PlayServError.FromHttp(status, code,
                string.IsNullOrEmpty(detail) ? "PlayServ matchmaking rejected the request." : detail,
                network, timeout, retryable: retryable));
        }

        private static string SafeDiagnostic(string text, PlayServRuntimeDataRequest request)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!string.IsNullOrEmpty(request.ClientToken)) text = text.Replace(request.ClientToken, "[REDACTED]");
            if (!string.IsNullOrEmpty(request.BearerToken)) text = text.Replace(request.BearerToken, "[REDACTED]");
            return CredentialText.Replace(text, "[REDACTED]");
        }

        [Serializable]
        private sealed class RoomConnectWire
        {
            public string host;
            public int? port;
            public string transport;
            public string connect_string;
            public string region;
        }

        [Serializable]
        private sealed class BrowseRoomWire
        {
            public string room_name;
            public int? players;
            public int? capacity;
            public string state;
            public string placement_state;
            public string region;
            public Dictionary<string, object> attributes;
            public RoomConnectWire connect;
        }

        [Serializable]
        private sealed class BrowseResponseWire
        {
            public List<BrowseRoomWire> data;
            public BrowsePageWire page;
        }

        [Serializable]
        private sealed class BrowsePageWire
        {
            public string cursor_next;
            public bool has_more;
        }
    }
}
