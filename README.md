# PlayServ Unity SDK Usage Samples

This document contains practical examples for the public runtime API exposed by:
- `Playserv.Wrapper.PlayServ`
- `Playserv.Wrapper.PlayServSettings`
- `Playserv.DataSubscription.ISharedEntity<T>`
- `Playserv.Spawn` components

## 1) Configure SDK

### Option A: through Unity asset (recommended for editor workflow)

1. Open `Tools/PlayServ/Settings`.
2. In `PlayServ Config`, ensure `Assets/Resources/PlayServConfig.asset` exists.
3. Fill `GameAccessToken`, `GameId`, `UserId`, `GameVersion`.
4. On runtime start, call `PlayServ.Connect()`.

### Option B: configure from code

```csharp
using Playserv.Wrapper;

PlayServ.Config(new PlayServSettings
{
    GameAccessToken = "your-token",
    GameId = "game-001",
    UserId = "player-001",
    GameVersion = "1.0.0",
    SdkVersion = PlayServ.SdkVersion,
    UseLocalBackend = true,
    LocalEndpoint = "ws://localhost:8080/ws/",
    RemoteEndpoint = "wss://playserv-proxy.test.playserv.io/ws",
    AllowMultipleConnections = true,
    KeepAlivePingIntervalMs = 30000,
    KeepAlivePongTimeoutMs = 10000
});
```

## 2) Connection lifecycle (MonoBehaviour)

```csharp
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using UnityEngine;

public sealed class PlayServBootstrap : MonoBehaviour
{
    private async void Start()
    {
        PlayServ.OnTransportError += OnTransportError;
        PlayServ.OnKeepAlivePingSent += OnPing;
        PlayServ.OnKeepAlivePongReceived += OnPong;

        // If you already have Resources/PlayServConfig.asset, Config(...) is optional.
        PlayServ.Config("your-token", "game-001", "player-001", "1.0.0");

        bool connected = await PlayServ.Connect();
        if (!connected)
        {
            Debug.LogError("PlayServ connection failed.");
            return;
        }

        Debug.Log($"PlayServ connected. State={PlayServ.State}, SDK={PlayServ.SdkVersion}");
    }

    private void OnDestroy()
    {
        PlayServ.OnTransportError -= OnTransportError;
        PlayServ.OnKeepAlivePingSent -= OnPing;
        PlayServ.OnKeepAlivePongReceived -= OnPong;
        PlayServ.Disconnect();
    }

    private static void OnTransportError(TransportError error)
    {
        Debug.LogError($"PlayServ transport error: {error}");
    }

    private static void OnPing()
    {
        Debug.Log("PlayServ keepalive ping sent.");
    }

    private static void OnPong()
    {
        Debug.Log("PlayServ keepalive pong received.");
    }
}
```

## 3) Send commands

```csharp
using System;
using Playserv.Wrapper;

[Serializable]
public sealed class JoinMatchCommand
{
    public string MatchId;
}

[Serializable]
public sealed class PingCommand
{
    public long ClientTimeUnixMs;
}

public static class CommandExamples
{
    public static void SendExamples()
    {
        // Sends with explicit backend module name.
        PlayServ.Send(
            new JoinMatchCommand { MatchId = "match-001" },
            moduleName: "module_matchmaking");

        // Sends without module prefix.
        PlayServ.Send(
            new PingCommand { ClientTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }
}
```

## 4) Publish and subscribe events

```csharp
using System;
using Playserv.Events;
using Playserv.Wrapper;
using UnityEngine;

[Serializable]
[Event(EventType.All)]
public sealed class ChatMessageEvent
{
    public string FromUserId;
    public string Text;
}

public sealed class ChatEventsExample : MonoBehaviour
{
    private IDisposable _chatSubscription;

    private void OnEnable()
    {
        _chatSubscription = PlayServ.Subscribe<ChatMessageEvent>(OnChatMessage);
    }

    private void OnDisable()
    {
        _chatSubscription?.Dispose();
        _chatSubscription = null;
    }

    public void SendGlobal(string text)
    {
        PlayServ.Publish(new ChatMessageEvent
        {
            FromUserId = "player-001",
            Text = text
        });
    }

    public void SendToGroup(string groupName, string text)
    {
        PlayServ.PublishForGroup(groupName, new ChatMessageEvent
        {
            FromUserId = "player-001",
            Text = text
        });
    }

    public void SendToUser(string userId, string text)
    {
        PlayServ.PublishForUser(userId, new ChatMessageEvent
        {
            FromUserId = "player-001",
            Text = text
        });
    }

    private static void OnChatMessage(ChatMessageEvent evt)
    {
        Debug.Log($"[CHAT] {evt.FromUserId}: {evt.Text}");
    }
}
```

## 5) Shared entity (data subscription) with `SelectEntity`

```csharp
using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Wrapper;
using UnityEngine;

[Serializable]
public sealed class PlayerEntity
{
    public string Id;
    public string Nickname;
    public int Score;
    public int Hp;
}

public sealed class PlayerHudDto
{
    public string Nickname { get; set; } = "";
    public int Score { get; set; }
    public int Hp { get; set; }
}

public sealed class PlayerHudSync : MonoBehaviour
{
    private ISharedEntity<PlayerHudDto> _shared;
    private IDisposable _sharedDisposable;

    public async Task BindAsync(string playerId)
    {
        _shared = await PlayServ.SelectEntity<PlayerEntity, PlayerHudDto>(
            playerId,
            map: src => new PlayerHudDto
            {
                Nickname = src.Nickname,
                Score = src.Score,
                Hp = src.Hp
            });

        _shared.Changed += OnChanged;
        _shared.Error += OnError;
        _shared.Terminated += OnTerminated;

        // Public return type does not include Dispose, but runtime object supports IDisposable.
        if (_shared is IDisposable disposable)
            _sharedDisposable = disposable;
    }

    public async Task DealDamageAsync(int damage)
    {
        if (_shared == null)
            return;

        await _shared.UpdateAsync(dto => dto.Hp = Mathf.Max(0, dto.Hp - damage));
    }

    public async Task ForceRefreshAsync()
    {
        if (_shared != null)
            await _shared.RefreshAsync();
    }

    private static void OnChanged(PlayerHudDto dto)
    {
        Debug.Log($"HUD updated: {dto.Nickname}, HP={dto.Hp}, Score={dto.Score}");
    }

    private static void OnError(DataSubscriptionException ex)
    {
        Debug.LogError($"Data subscription error [{ex.ErrorCode}]: {ex.Message}");
    }

    private static void OnTerminated()
    {
        Debug.LogWarning("Data subscription terminated by server.");
    }

    private void OnDestroy()
    {
        if (_shared != null)
        {
            _shared.Changed -= OnChanged;
            _shared.Error -= OnError;
            _shared.Terminated -= OnTerminated;
        }

        _sharedDisposable?.Dispose();
        _sharedDisposable = null;
    }
}
```

## 6) Spawn networked prefab

```csharp
using Playserv.Spawn;
using Playserv.Wrapper;
using UnityEngine;

public sealed class SpawnExample : MonoBehaviour
{
    public async void SpawnCrate()
    {
        // Path is relative to a Resources folder.
        // Example file path: Assets/Resources/NetworkPrefabs/Crate.prefab
        var go = await PlayServ.Spawn("NetworkPrefabs/Crate", new Vector3(0f, 1f, 0f), Quaternion.identity);
        if (go == null)
        {
            Debug.LogError("Spawn failed. Check prefab path/components.");
            return;
        }

        var networkObject = go.GetComponent<NetworkObject>();
        Debug.Log($"Spawned network object: id={networkObject.NetworkId}, localOwner={networkObject.IsLocallyOwned}");
    }
}
```

Prefab requirements:
- Must be inside `Resources`.
- Must contain `NetworkObject`.
- Add `NetworkTransform` if you want transform replication.

## 7) Editor workflow (from package window)

Open `Tools/PlayServ/Settings` and use:
- `Code Generation` -> Generate/cleanup DTO files (`Assets/Shared/Generated/DTOs`).
- `Events` -> Generate event API (`Assets/Shared/Generated/Events`).
- `Model` -> Check schema updates and regenerate models (`Assets/Shared/Generated/Models`).
- `Deployment` -> ZIP and upload selected files to deployment API.

## 8) Practical notes

- `PlayServ.Connect()` throws if config is missing required fields.
- `PlayServ.Subscribe<T>()` is event subscription (module events), not a raw command response channel.
- Always dispose subscriptions and disconnect in object teardown.
- `PlayServ.Spawn(...)` returns `null` when prefab path is invalid or missing `NetworkObject`.

## 9) RPC-style call (pattern from `Assets/Tests/RPC`)

`Assets/Tests/RPC/RPCTest.cs` uses the following style:
- request DTO in payload
- module path passed as `"rpc.InvokeRpc"`
- payload body encoded as base64 string

```csharp
using System;
using System.Text;
using Newtonsoft.Json;
using Playserv.Wrapper;

[Serializable]
public sealed class RpcInvokeRequest
{
    public string ServiceName { get; set; }
    public string MethodName { get; set; }
    public string Payload { get; set; } // base64 JSON body
}

public static class RpcUsageExample
{
    public static void BroadcastMessage()
    {
        var jsonBody = JsonConvert.SerializeObject(new { message = "Hello from RPC" });
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonBody));

        var request = new RpcInvokeRequest
        {
            ServiceName = "NotificationService",
            MethodName = "BroadcastToAll",
            Payload = payloadBase64
        };

        PlayServ.Send(request, "rpc.InvokeRpc");
    }
}
```

You can pair this with event subscription for side effects from RPC handlers:

```csharp
using System;
using UnityEngine;
using Playserv.Wrapper;

[Serializable]
public sealed class NotificationEvent
{
    public string EventId;
    public string Message;
    public DateTime Timestamp;
    public string EventType;
}

public sealed class RpcNotificationListener : MonoBehaviour
{
    private IDisposable _subscription;

    private void OnEnable()
    {
        _subscription = PlayServ.Subscribe<NotificationEvent>(evt =>
        {
            Debug.Log($"[RPC Event] {evt.EventType}: {evt.Message}");
        });
    }

    private void OnDisable()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
```

## 10) Low-level transport debug (advanced)

If you need protocol-level validation tests (like in `Assets/Tests/EntryPointConsoleCommands.cs`), you can bypass serializer and send raw JSON bytes:

```csharp
using System.Text;
using System.Threading.Tasks;
using Playserv.Proxy.Interfaces;
using Playserv.Wrapper;
using UnityEngine;

public static class TransportDebugExample
{
    public static async Task SendRawJsonAsync(string rawJson)
    {
        ITransportImplementation transport = PlayServ.GetTransportImplementation();
        if (transport == null)
        {
            Debug.LogError("Transport implementation is not available. Connect SDK first.");
            return;
        }

        await transport.Send(Encoding.UTF8.GetBytes(rawJson));
    }
}
```

## 11) Connection state UI (pattern from `Assets/Tests/PlayServStateUI.cs`)

Simple UI binding for connection state and connect/disconnect buttons:

```csharp
using Playserv.Proxy.Common;
using Playserv.Wrapper;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayServStateUiExample : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _stateText;
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _disconnectButton;

    private void Awake()
    {
        _connectButton.onClick.AddListener(OnConnectClick);
        _disconnectButton.onClick.AddListener(OnDisconnectClick);
    }

    private void Update()
    {
        PlayServState state = PlayServ.State;
        _stateText.text = $"PlayServ.State: {state}";
        _connectButton.interactable = state == PlayServState.Offline;
        _disconnectButton.interactable = state == PlayServState.Online;
    }

    private async void OnConnectClick()
    {
        bool ok = await PlayServ.Connect();
        Debug.Log($"Connect result: {ok}");
    }

    private static void OnDisconnectClick()
    {
        PlayServ.Disconnect();
    }
}
```

## 12) KeepAlive stats widget (pattern from `Assets/Tests/KeepAliveStatsUI.cs`)

```csharp
using System.Threading;
using Playserv.Wrapper;
using TMPro;
using UnityEngine;

public sealed class KeepAliveStatsExample : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _statsText;
    private SynchronizationContext _mainThread;
    private int _pingSent;
    private int _pongReceived;

    private void Awake()
    {
        _mainThread = SynchronizationContext.Current;
    }

    private void OnEnable()
    {
        PlayServ.OnKeepAlivePingSent += OnPingSent;
        PlayServ.OnKeepAlivePongReceived += OnPongReceived;
    }

    private void OnDisable()
    {
        PlayServ.OnKeepAlivePingSent -= OnPingSent;
        PlayServ.OnKeepAlivePongReceived -= OnPongReceived;
    }

    private void OnPingSent()
    {
        _pingSent++;
        Redraw();
    }

    private void OnPongReceived()
    {
        _pongReceived++;
        Redraw();
    }

    private void Redraw()
    {
        _mainThread.Post(_ =>
        {
            _statsText.text = $"KeepAlive - Sent: {_pingSent} | Received: {_pongReceived}";
        }, null);
    }
}
```

## 13) Data subscription UI flow (pattern from `Assets/Tests/DataSubscriptionEntryPoint.cs`)

Working UI flow from tests:
- generate player id at startup
- bind via `SelectEntity<Player, PlayerDto>(playerId, map)`
- update UI in `Changed`
- handle `Error` and `Terminated`
- mutate via `Update/UpdateAsync`
- force overwrite sync via `RefreshAsync`

Minimal command-style operations:

```csharp
// Rename
_player.Update(p => p.Name = newName);

// Increment level
_player.Update(p => p.Level++);

// Set level async
await _player.UpdateAsync(p => p.Level = level);

// Request full overwrite from server
await _player.RefreshAsync();
```

## 14) IngameDebugConsole command bridge (pattern from `Assets/Tests/EntryPointConsoleCommands.cs`)

Non-RPC commands used in tests:
- `connectsdk`, `disconnectsdk`
- `subscribe`, `unsubscribe`, `sendevent`
- `set_allowmultipleconnections`
- `datasub.init`, `datasub.rename`, `datasub.addlevel`, `datasub.setlevel`, `datasub.refresh`, `datasub.status`
- `spawn`

Sample bridge method:

```csharp
using IngameDebugConsole;
using Playserv.Wrapper;
using UnityEngine;

public static class ConsoleBridgeExample
{
    [ConsoleMethod("connectsdk", "Connects to PlayServ SDK")]
    public static async void ConnectSdk()
    {
        bool result = await PlayServ.Connect();
        Debug.Log($"Console SDK connection result: {result}");
    }
}
```

## 15) `[Shared]` DTO generation from a ViewModel (pattern from `Assets/Tests/ViewModels/ViewModel.cs`)

```csharp
using Playserv.Shared;

public class ViewModel
{
    [Shared(typeof(Shared.Generated.Models.Player), "player", Selection = "{ Level }")]
    private ViewModel_PlayerLevel Level { get; set; }
}
```

Then run code generation from `Tools/PlayServ/Settings` -> `Code Generation` -> `Generate DTOs Now`.
