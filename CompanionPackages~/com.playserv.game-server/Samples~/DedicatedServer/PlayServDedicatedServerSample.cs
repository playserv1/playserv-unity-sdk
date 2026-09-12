using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;
using UnityEngine;

public sealed class PlayServDedicatedServerSample : MonoBehaviour
{
    [SerializeField] private string functionSlug = "ranked-arena";
    [SerializeField] private string roomName = "unity-room-1";
    [SerializeField] private int capacity = 16;

    private PlayServGameRoomHandle _room;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

    private async void Start()
    {
        try
        {
            PlayServGameServer.Configure(new PlayServGameServerOptions());
            PlayServGameServer.Analytics.Track(
                "server_starting",
                new Dictionary<string, object>
                {
                    ["function"] = functionSlug,
                    ["capacity"] = capacity
                });
            await PlayServGameServer.Realtime.ConnectAsync(
                new PlayServGameServerRealtimeOptions
                {
                    InstanceId = roomName,
                    GameVersion = Application.version
                },
                _lifetime.Token);
            _room = await PlayServGameServer.StartRoomAsync(
                new PlayServStartRoomRequest(
                    functionSlug,
                    new PlayServGameRoomSnapshot(roomName, 0, capacity)),
                _lifetime.Token);
        }
        catch (Exception exception)
        {
            Debug.LogError(exception.Message);
        }
    }

    public void SetPlayerCount(int players)
    {
        if (_room == null)
            return;
        _room.Update(new PlayServGameRoomSnapshot(roomName, players, capacity));
    }

    public Task<PlayServReservationConsumeResult> AdmitAsync(
        string reservationToken,
        string playerId,
        CancellationToken cancellationToken)
    {
        return PlayServGameServer.ConsumeReservationAsync(
            functionSlug,
            reservationToken,
            playerId,
            roomName,
            cancellationToken);
    }

    public async Task StopServerAsync(CancellationToken cancellationToken)
    {
        await PlayServGameServer.ShutdownAsync(cancellationToken);
    }

    private void OnDestroy()
    {
        _lifetime.Cancel();
        _room?.Dispose();
        _lifetime.Dispose();
    }
}
