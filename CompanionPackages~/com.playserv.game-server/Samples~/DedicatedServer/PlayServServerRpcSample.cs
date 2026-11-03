using System.Collections.Generic;
using System.Threading.Tasks;
using Playserv.GameServer;
using UnityEngine.Scripting;

/// <summary>Explicit AOT registrations. Assign CreateRegistry() to server options before Configure.</summary>
[Preserve]
public static class PlayServServerRpcSample
{
    [Preserve] public sealed class Request
    {
        [Preserve] public Request() { }
        [Preserve] public int Round;
        [Preserve] public List<string> Players;
    }
    [Preserve] public sealed class Response
    {
        [Preserve] public Response() { }
        [Preserve] public int Round;
        [Preserve] public string RequestedBy;
    }

    [Preserve] public static PlayServServerRpcRegistry CreateRegistry()
    {
        var round = new PlayServServerRpcParameter<Request>("round");
        var registry = new PlayServServerRpcRegistry();
        registry.Register<Response>("round.preview", new[] { round }, (caller, args, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            // Identity comes from caller, not from the Request DTO. Apply game authorization here.
            return Task.FromResult(new Response { Round = args.Get(round).Round, RequestedBy = caller.PlayerId });
        });
        return registry;
    }
}
