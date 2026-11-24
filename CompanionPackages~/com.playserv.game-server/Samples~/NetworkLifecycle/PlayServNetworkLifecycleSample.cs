using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.GameServer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Playserv.Samples.NetworkLifecycle
{
    /// <summary>Attach to one root object in the dedicated server's initial scene.</summary>
    public sealed class PlayServNetworkLifecycleSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private PlayServTcpRoomTransport _network;
        private PlayServRoomNetworkAdapter _adapter;
        private Task<PlayServHostedRoom> _starting;

        private async void Start()
        {
            DontDestroyOnLoad(gameObject);
            try
            {
                _network = new PlayServTcpRoomTransport();
                _starting = PlayServGameServer.StartHostedRoomAsync(_network, new PlayServHostedRoomOptions
                {
                    Transport = "tcp",
                    ServerOptions = new PlayServGameServerOptions
                    {
                        ParticipantRemoveHandler = (request, ct) => Task.FromResult(
                            request.RoomName != _network.RoomName || _network.ClosePlayer(request.PlayerId)),
                        RoomCloseHandler = async (request, ct) =>
                        {
                            if (request.RoomName == _network.RoomName) await _network.StopAsync(ct);
                            return true;
                        }
                    }
                }, _lifetime.Token);
                var hosted = await _starting;
                _adapter = new PlayServRoomNetworkAdapter(hosted.Room, _network);
                _adapter.Error += error => Debug.LogError(error.SourceCode);
                Debug.Log("TCP room ready: " + hosted.Room.RoomName);
            }
            catch (OperationCanceledException) { }
            catch (PlayServGameServerException error) { Debug.LogError(error.UnifiedError.SourceCode); }
            catch { Debug.LogError("Network lifecycle sample startup failed."); }
        }

        public void RemovePlayer(string verifiedPlayerId) => _adapter?.RemovePlayer(verifiedPlayerId);

        // Only the scene changes. The room, adapter, listener and live connections stay with this root.
        public void ChangeScene(string sceneName) => SceneManager.LoadSceneAsync(sceneName);

        public async Task ShutdownAsync()
        {
            _lifetime.Cancel();
            _adapter?.Dispose(); _adapter = null;
            if (_starting == null) return;
            PlayServHostedRoom hosted;
            try { hosted = await _starting; }
            catch { return; }
            await hosted.StopAsync();
        }

        private async void OnDestroy()
        {
            try { await ShutdownAsync(); }
            catch { Debug.LogError("Sample shutdown could not be confirmed."); }
            finally { _lifetime.Dispose(); }
        }
    }
}
