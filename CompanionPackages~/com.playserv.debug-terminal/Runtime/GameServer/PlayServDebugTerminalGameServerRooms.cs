using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;

namespace Playserv.DebugTerminal.GameServer
{
    internal sealed partial class PlayServDebugTerminalGameServerExtension
    {
        private async Task ExecuteRoomAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "start":
                case "upsert":
                    if (!TryParseRoomSnapshot(parts, 3, null, out var slug, out var snapshot))
                        return;
                    await RunOperationAsync($"room {operation}", async token =>
                    {
                        if (operation == "start")
                        {
                            var handle = await PlayServGameServer.StartRoomAsync(
                                new PlayServStartRoomRequest(slug, snapshot), token);
                            AttachRoom(handle);
                            _rooms[RoomKey(slug, snapshot.RoomName)] = handle;
                            Log($"Managed room started; function={slug}; room={snapshot.RoomName}.");
                            PrintRoom(handle);
                        }
                        else
                        {
                            var result = await PlayServGameServer.UpsertRoomAsync(slug, snapshot, token);
                            Log($"Room upserted; function={slug}; room={snapshot.RoomName}; created={result.Created}; placement={FormatPlacement(result.Placement)}");
                        }
                    });
                    return;
                case "update":
                    if (!TryGetRoom(parts, 3, out var updateRoom, out var updateIndex))
                        return;
                    if (!TryParseRoomSnapshot(
                            parts,
                            updateIndex,
                            updateRoom.DesiredSnapshot,
                            out _,
                            out var updated,
                            explicitSlug: updateRoom.FunctionSlug,
                            explicitRoom: updateRoom.RoomName))
                        return;
                    updateRoom.Update(updated);
                    Log($"Desired room snapshot updated; function={updateRoom.FunctionSlug}; room={updateRoom.RoomName}.");
                    return;
                case "heartbeat":
                    if (!TryGetRoom(parts, 3, out var heartbeatRoom, out _))
                        return;
                    await RunOperationAsync("room heartbeat", async token =>
                    {
                        var result = await heartbeatRoom.HeartbeatAsync(token);
                        Log($"Room heartbeat completed; created={result.Created}; placement={FormatPlacement(result.Placement)}");
                    });
                    return;
                case "list":
                    if (parts.Count < 4)
                    {
                        Log("Usage: server room list <functionSlug>");
                        return;
                    }
                    await RunOperationAsync("room list", async token =>
                    {
                        var rooms = await PlayServGameServer.ListRoomsAsync(parts[3], token);
                        Log($"Server rooms: function={parts[3]}; count={rooms.Count}");
                        foreach (var room in rooms)
                        {
                            Log(
                                $"Room name={room.RoomName}; players={room.Players}/{room.Capacity}; state={room.State}; " +
                                $"placement={room.PlacementState}; open={room.Open}; closing={room.Closing}; age={room.AgeSeconds}s");
                        }
                    });
                    return;
                case "status":
                    if (parts.Count >= 5 && _rooms.TryGetValue(RoomKey(parts[3], parts[4]), out var selected))
                        PrintRoom(selected);
                    else if (parts.Count >= 5)
                        Log("Managed room handle not found.");
                    else
                    {
                        Log($"Terminal-owned managed rooms: {_rooms.Count}");
                        foreach (var room in _rooms.Values)
                            PrintRoom(room);
                    }
                    return;
                case "close":
                    if (parts.Count < 5)
                    {
                        Log("Usage: server room close <functionSlug> <roomName>");
                        return;
                    }
                    var roomKey = RoomKey(parts[3], parts[4]);
                    await RunOperationAsync("room close", async token =>
                    {
                        if (_rooms.TryGetValue(roomKey, out var handle))
                        {
                            var result = await handle.CloseAsync(token);
                            _rooms.Remove(roomKey);
                            Log(result.IsSuccess
                                ? $"Managed room closed; alreadyClosed={result.WasAlreadyClosed}."
                                : $"Managed room close failed: {PlayServDebugTerminal.FormatError(result.Error)}");
                        }
                        else
                        {
                            await PlayServGameServer.CloseRoomAsync(parts[3], parts[4], token);
                            Log("Unmanaged room close completed.");
                        }
                    });
                    return;
                default:
                    Log("Usage: server room <start|upsert|update|heartbeat|list|status|close> ...");
                    return;
            }
        }

        private async Task ExecuteServerMatchAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 2 ? parts[2].ToLowerInvariant() : string.Empty;
            if (operation == "launch")
            {
                if (!DebugTerminalArguments.TryParse(
                        parts, 3, new[] { "region" }, Array.Empty<string>(),
                        out var arguments, out var error) || arguments.Positionals.Count != 1)
                {
                    Log(error ?? "Usage: server match launch <functionSlug> [--region value]");
                    return;
                }
                await RunOperationAsync("server launch", async token =>
                {
                    var result = await PlayServGameServer.LaunchServerAsync(
                        arguments.Positionals[0], arguments.Get("region"), token);
                    Log($"Server launch accepted; deployment={result.DeploymentId}; region={result.Region ?? "-"}.");
                });
                return;
            }
            if (operation != "find")
            {
                Log("Usage: server match <find|launch> ...");
                return;
            }
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    3,
                    new[] { "matchmaker", "wait-ms", "search-age-ms", "params" },
                    Array.Empty<string>(),
                    out var find,
                    out var findError) ||
                find.Positionals.Count != 2 ||
                !find.TryGetInt("wait-ms", 0, 0, 25000, out var waitMs, out findError) ||
                !find.TryGetInt("search-age-ms", 0, 0, int.MaxValue, out var ageMs, out findError) ||
                !DebugTerminalArguments.TryParseJson(find.Get("params"), true, out var parameters, out findError))
            {
                Log(findError ?? "Usage: server match find <functionSlug> <playerId> [--matchmaker value] [--wait-ms n] [--search-age-ms n] [--params json-object]");
                return;
            }

            await RunOperationAsync("server matchmaking", async token =>
            {
                var result = await PlayServGameServer.FindMatchForPlayerAsync(
                    new PlayServServerFindMatchRequest
                    {
                        FunctionSlug = find.Positionals[0],
                        PlayerId = find.Positionals[1],
                        Matchmaker = find.Get("matchmaker"),
                        WaitMs = waitMs,
                        SearchAgeMs = ageMs,
                        Parameters = parameters
                    }, token);
                Log(
                    $"Server match: status={result.Status}; room={result.Reservation?.RoomName ?? "-"}; " +
                    $"expires={result.Reservation?.ExpiresAt.ToString("O") ?? "-"}; retryAfter={result.RetryAfterMs?.ToString() ?? "-"}ms. " +
                    "Reservation token is intentionally hidden.");
            });
        }

        private void ExecuteReservation(IReadOnlyList<string> parts)
        {
            string error = null;
            if (parts.Count < 5 || !string.Equals(parts[2], "consume", StringComparison.OrdinalIgnoreCase) ||
                !DebugTerminalArguments.TryParse(
                    parts, 3, new[] { "room" }, Array.Empty<string>(),
                    out var arguments, out error) || arguments.Positionals.Count != 2)
            {
                Log(error ?? "Usage: server reservation consume <functionSlug> <playerId> [--room value]");
                return;
            }

            var slug = arguments.Positionals[0];
            var playerId = arguments.Positionals[1];
            var roomName = arguments.Get("room");
            _terminal.RequestSecureSecret(
                "Consume Reservation",
                "Reservation Token",
                token => RunOperationAsync("reservation consume", async cancellationToken =>
                {
                    var result = await PlayServGameServer.ConsumeReservationAsync(
                        slug, token, playerId, roomName, cancellationToken);
                    Log(
                        $"Reservation admission: ok={result.Ok}; player={result.PlayerId ?? playerId}; " +
                        $"room={result.RoomName ?? roomName ?? "-"}; errorCode={result.ErrorCode ?? "-"}. " +
                        "Reservation token is intentionally hidden.");
                }));
        }

        private async Task ExecutePlayerAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count != 4 || !string.Equals(parts[2], "get", StringComparison.OrdinalIgnoreCase))
            {
                Log("Usage: server player get <playerId>");
                return;
            }
            await RunOperationAsync("player lookup", async token =>
            {
                var player = await PlayServGameServer.GetPlayerAsync(parts[3], token);
                if (player == null)
                {
                    Log("Player not found.");
                    return;
                }
                Log(
                    $"Player id={player.Id}; name='{player.Name}'; status={player.Status}; country={player.Country ?? "-"}; " +
                    $"sso={(player.Sso.Count == 0 ? "none" : string.Join(",", player.Sso))}; joined={player.Joined}; updated={player.UpdatedAt:O}");
            });
        }

        private void ExecuteJwt(IReadOnlyList<string> parts)
        {
            string error = null;
            if (parts.Count < 3 || !string.Equals(parts[2], "validate", StringComparison.OrdinalIgnoreCase) ||
                !DebugTerminalArguments.TryParse(
                    parts,
                    3,
                    new[] { "project", "environment", "issuer", "skew-sec" },
                    Array.Empty<string>(),
                    out var arguments,
                    out error) ||
                arguments.Positionals.Count != 0 ||
                string.IsNullOrWhiteSpace(arguments.Get("project")) ||
                string.IsNullOrWhiteSpace(arguments.Get("environment")) ||
                !arguments.TryGetInt("skew-sec", 60, 0, 3600, out var skew, out error))
            {
                Log(error ?? "Usage: server jwt validate --project id --environment env [--issuer value] [--skew-sec n]");
                return;
            }

            _terminal.RequestSecureSecret(
                "Validate Player Token",
                "Player JWT",
                jwt => RunOperationAsync("JWT validation", async token =>
                {
                    var result = await PlayServGameServer.ValidatePlayerTokenAsync(
                        jwt,
                        new PlayServPlayerTokenValidationOptions
                        {
                            ExpectedProjectId = arguments.Get("project"),
                            ExpectedEnvironment = arguments.Get("environment"),
                            Issuer = arguments.Get("issuer", "playserv"),
                            ClockSkew = TimeSpan.FromSeconds(skew)
                        },
                        token);
                    if (!result.IsValid)
                    {
                        Log($"Player JWT invalid: {PlayServDebugTerminal.FormatError(result.UnifiedError)}");
                        return;
                    }
                    var claims = result.Claims;
                    Log(
                        $"Player JWT valid; player={claims.PlayerId}; project={claims.ProjectId}; environment={claims.Environment}; " +
                        $"session={claims.SessionId}; providers={(claims.Providers.Count == 0 ? "none" : string.Join(",", claims.Providers))}; expires={claims.ExpiresAt:O}");
                }));
        }

        private bool TryParseRoomSnapshot(
            IReadOnlyList<string> parts,
            int start,
            PlayServGameRoomSnapshot existing,
            out string functionSlug,
            out PlayServGameRoomSnapshot snapshot,
            string explicitSlug = null,
            string explicitRoom = null)
        {
            functionSlug = explicitSlug;
            snapshot = null;
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    start,
                    new[] { "players", "capacity", "state", "attributes" },
                    new[] { "open", "closed" },
                    out var arguments,
                    out var error))
            {
                Log(error);
                return false;
            }

            var requiredPositionals = explicitSlug == null ? 2 : 0;
            if (arguments.Positionals.Count != requiredPositionals)
            {
                Log("Usage: server room <start|upsert> <functionSlug> <roomName> --capacity n [--players n] [--state value] [--attributes json] [--closed]");
                return false;
            }
            if (explicitSlug == null)
            {
                functionSlug = arguments.Positionals[0];
                explicitRoom = arguments.Positionals[1];
            }

            var defaultPlayers = existing?.Players ?? 0;
            var defaultCapacity = existing?.Capacity ?? -1;
            if (!arguments.TryGetInt("players", defaultPlayers, 0, int.MaxValue, out var players, out error) ||
                !arguments.TryGetInt("capacity", defaultCapacity, 1, int.MaxValue, out var capacity, out error) ||
                !DebugTerminalArguments.TryParseJson(arguments.Get("attributes"), true, out var attributes, out error))
            {
                Log(error);
                return false;
            }
            if (players > capacity)
            {
                Log("Room players cannot exceed capacity.");
                return false;
            }
            if (arguments.Has("open") && arguments.Has("closed"))
            {
                Log("Use only one of --open or --closed.");
                return false;
            }

            var open = arguments.Has("closed") ? false : arguments.Has("open") || existing?.Open != false;
            var state = arguments.Get("state", existing?.State);
            if (!arguments.Has("attributes") && existing != null)
                attributes = existing.Attributes;
            snapshot = new PlayServGameRoomSnapshot(
                explicitRoom,
                players,
                capacity,
                state,
                attributes,
                open,
                existing?.CreatedAt);
            return true;
        }

        private bool TryGetRoom(
            IReadOnlyList<string> parts,
            int start,
            out PlayServGameRoomHandle room,
            out int nextIndex)
        {
            room = null;
            nextIndex = start;
            if (parts.Count < start + 2)
            {
                Log("Function slug and room name are required.");
                return false;
            }
            nextIndex = start + 2;
            if (_rooms.TryGetValue(RoomKey(parts[start], parts[start + 1]), out room))
                return true;
            Log("Managed room handle not found.");
            return false;
        }

        private void AttachRoom(PlayServGameRoomHandle room)
        {
            room.HeartbeatFailed += OnRoomHeartbeatFailed;
            room.PlacementChanged += OnRoomPlacementChanged;
            room.Terminated += OnRoomTerminated;
        }

        private void OnRoomHeartbeatFailed(PlayServGameRoomHandle room, Playserv.Wrapper.PlayServError error)
        {
            Log($"Room heartbeat failed; function={room.FunctionSlug}; room={room.RoomName}; {PlayServDebugTerminal.FormatError(error)}");
        }

        private void OnRoomPlacementChanged(PlayServGameRoomHandle room, PlayServRoomPlacementAcknowledgment placement)
        {
            Log($"Room placement changed; function={room.FunctionSlug}; room={room.RoomName}; {FormatPlacement(placement)}");
        }

        private void OnRoomTerminated(PlayServGameRoomHandle room, Playserv.Wrapper.PlayServError error)
        {
            _rooms.Remove(RoomKey(room.FunctionSlug, room.RoomName));
            Log($"Room terminated; function={room.FunctionSlug}; room={room.RoomName}; {PlayServDebugTerminal.FormatError(error)}");
        }

        private void PrintRoom(PlayServGameRoomHandle room)
        {
            var snapshot = room.DesiredSnapshot;
            Log(
                $"Managed room function={room.FunctionSlug}; name={room.RoomName}; state={room.State}; " +
                $"desired={snapshot.Players}/{snapshot.Capacity},open={snapshot.Open},state={snapshot.State ?? "-"}; " +
                $"placement={FormatPlacement(room.Placement)}; lastHeartbeat={room.LastHeartbeatAt?.ToString("O") ?? "-"}; " +
                $"lastError={(room.LastError.IsError ? room.LastError.SourceCode : "none")}");
        }

        private static string FormatPlacement(PlayServRoomPlacementAcknowledgment placement) =>
            placement == null
                ? "none"
                : $"state={placement.State},open={placement.Open},draining={placement.Draining},openRefused={placement.OpenRefused},cause={placement.DrainCause ?? "-"}";
    }
}
