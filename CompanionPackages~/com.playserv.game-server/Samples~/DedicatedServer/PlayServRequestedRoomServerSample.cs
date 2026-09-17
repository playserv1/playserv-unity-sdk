using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;
using UnityEngine;

/// <summary>Alternative to PlayServDedicatedServerSample: wait for named room requests without opening an initial room.</summary>
public sealed class PlayServRequestedRoomServerSample : MonoBehaviour
{
    [SerializeField] private string executorSlug = "arena";
    [SerializeField] private string advertisedHost = "game.example.com";
    [SerializeField] private int advertisedPort = 7777;
    private readonly Dictionary<string, GameObject> _prepared = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, PlayServGameRoomHandle> _rooms = new Dictionary<string, PlayServGameRoomHandle>();
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    // Game-owned transport callback; disconnect only the connection carrying this AdmissionId.
    public Action<string, string> DisconnectPlayer;

    private async void Start()
    {
        try
        {
            PlayServGameServer.Configure(new PlayServGameServerOptions { ExecutorSlug = executorSlug, RoomFactory = Prepare });
            PlayServGameServer.Uplink.RoomCreationCompleted += Complete;
            await PlayServGameServer.Uplink.ConnectAsync(_lifetime.Token);
            // Backend senders are implemented (PSV-2626/2590); verify this game against its deployed environment.
        }
        catch (OperationCanceledException) { }
        catch (PlayServGameServerException ex) { Debug.LogError(ex.UnifiedError.SourceCode); }
        catch { Debug.LogError("Requested-room host startup failed."); }
    }

    private Task<PlayServRoomCreateDecision> Prepare(PlayServRoomCreateContext request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_prepared.ContainsKey(request.RoomName))
            return Task.FromResult(PlayServRoomCreateDecision.Refuse(PlayServRoomCreateRefusal.RoomNameConflict));
        object approvedAttributes = null;
        if (request.Attributes is IDictionary<string, object> wish)
        {
            // Requested content is untrusted. Keep only supported game-owned map/mode choices.
            var map = wish.TryGetValue("map", out var requestedMap) ? requestedMap as string : "arena";
            var mode = wish.TryGetValue("mode", out var requestedMode) ? requestedMode as string : "practice";
            if ((map != "arena" && map != "forest") || (mode != "practice" && mode != "ranked"))
                return Task.FromResult(PlayServRoomCreateDecision.Refuse(
                    PlayServRoomCreateRefusal.ContentRefused, "Unsupported map or mode"));
            approvedAttributes = new { map, mode }; // Do not echo unknown requested fields.
        }
        // Replace this empty local root with the game's room/world creation. Keep it cancellation-safe.
        var root = new GameObject(request.RoomName);
        root.transform.SetParent(transform, false);
        _prepared.Add(request.RoomName, root);
        return Task.FromResult(PlayServRoomCreateDecision.Accept(new PlayServGameRoomSnapshot(
            request.RoomName, 0, request.Configuration.Capacity, attributes: approvedAttributes,
            connect: new PlayServGameRoomConnect(advertisedHost, advertisedPort, "udp"))));
    }

    private void Complete(PlayServRoomCreationOutcome outcome)
    {
        if (!outcome.IsSuccess)
        {
            if (outcome.FactoryInvoked) Cleanup(outcome.RoomName);
            Debug.LogWarning(outcome.Error.SourceCode);
            return;
        }
        _rooms[outcome.RoomName] = outcome.Room;
        outcome.Room.Admission.AdmissionRejected += result => DisconnectPlayer?.Invoke(result.PlayerId, result.AdmissionId);
        outcome.Room.LifetimeClosed += (room, _) => Cleanup(room.RoomName);
        outcome.Room.Terminated += (room, _) => Cleanup(room.RoomName);
    }

    public PlayServRoomAdmissionResult Admit(string roomName, string reservationToken)
    {
        if (DisconnectPlayer == null) throw new InvalidOperationException("Wire DisconnectPlayer to game networking before admission.");
        if (!_rooms.TryGetValue(roomName, out var room)) throw new InvalidOperationException("Wait for room registration.");
        return room.Admission.TryAdmit(reservationToken); // Includes exactly one presence join; no second ReportJoin.
    }
    public void ReportRemoval(string roomName, string playerId)
    {
        if (_rooms.TryGetValue(roomName, out var room)) room.ReportLeave(playerId);
    }
    public async Task StopAsync(CancellationToken ct)
    {
        PlayServGameServer.Uplink.AcceptingRoomRequests = false;
        await PlayServGameServer.ShutdownAsync(ct);
        foreach (var name in new List<string>(_prepared.Keys)) Cleanup(name);
    }
    private void Cleanup(string name)
    {
        if (_prepared.TryGetValue(name, out var root)) Destroy(root);
        _prepared.Remove(name); _rooms.Remove(name);
    }
    private void OnDestroy()
    {
        _lifetime.Cancel();
        PlayServGameServer.Uplink.RoomCreationCompleted -= Complete;
        foreach (var room in _rooms.Values) room.Dispose();
        _lifetime.Dispose();
        // The process owner must await StopAsync before destroying this component.
    }
}
