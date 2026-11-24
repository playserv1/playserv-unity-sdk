#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;
using UnityEngine;

/// <summary>Platform-launched single-room bootstrap with a minimal UDP binding.</summary>
public sealed class PlayServHostedServerSample : MonoBehaviour, IPlayServHostedRoomTransport
{
    [SerializeField] private bool forwardEngineLogs;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private Task<PlayServHostedRoom> _startup;
    private UdpClient _listener;

    private async void Start()
    {
        DontDestroyOnLoad(gameObject);
        try
        {
            _startup = PlayServGameServer.StartHostedRoomAsync(this,
                new PlayServHostedRoomOptions
                {
                    Transport = "udp",
                    ServerOptions = new PlayServGameServerOptions
                    { EngineLogs = new PlayServEngineLogOptions { Enabled = forwardEngineLogs } }
                }, _lifetime.Token);
            var hosted = await _startup;
            // Registration is now acknowledged. Wire hosted.Room.Admission into your transport
            // before accepting players; this binding example does not implement gameplay.
            Debug.Log("Hosted room registered: " + hosted.Room.RoomName);
        }
        catch (OperationCanceledException) { }
        catch (PlayServGameServerException error) { Debug.LogError(error.UnifiedError.SourceCode); }
        catch { Debug.LogError("Hosted room startup failed. Check the platform launch configuration."); }
    }

    public Task StartAsync(PlayServHostedLaunch launch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_listener != null) throw new InvalidOperationException("Listener already started.");
        // Replace with your networking stack's bind/world startup and await its readiness.
        // Bind ListenPort, never the mapped public port in launch.Connect.
        _listener = new UdpClient(launch.ListenPort);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    // Await this before intentional process exit. Unity cannot await OnDestroy/OnApplicationQuit.
    public async Task ShutdownAsync()
    {
        _lifetime.Cancel();
        if (_startup == null) return;
        PlayServHostedRoom hosted;
        try { hosted = await _startup; }
        catch { return; } // Failed startup already ran its owned cleanup.
        await hosted.StopAsync();
    }

    private async void OnDestroy()
    {
        try { await ShutdownAsync(); }
        catch { Debug.LogError("Hosted room cleanup was not confirmed; restart the server process."); }
        finally { _lifetime.Dispose(); }
    }
}
#endif
