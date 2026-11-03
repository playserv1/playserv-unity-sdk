using System.Threading;
using System.Threading.Tasks;
using Playserv.Status;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>Minimal credential-free platform status flow.</summary>
    public sealed class PlayServStatusSample : MonoBehaviour
    {
        public async Task LoadProjectStatusAsync(string projectSlug, CancellationToken ct = default)
        {
            var project = await PlayServStatus.GetProjectAsync(projectSlug, cancellationToken: ct);
            Debug.Log($"Project {project.ProjectSlug}: {project.Functions.Length} functions reported.");
        }

        public async Task LoadStatusAsync(CancellationToken cancellationToken)
        {
            PlayServPlatformStatus current = await PlayServStatus.GetCurrentAsync(
                cancellationToken);
            Debug.Log($"PlayServ status: {current.Overall ?? "no evidence"}");

            PlayServPlatformStatusHistory history = await PlayServStatus.GetHistoryAsync(
                days: 30,
                cancellationToken: cancellationToken);
            Debug.Log($"Loaded {history.DaysRequested} days for {history.Pop}.");

            PlayServStatusFederation federation = await PlayServStatus.GetFederationAsync(
                cancellationToken);
            foreach (System.Uri origin in federation.Origins)
                Debug.Log($"Advertised status peer: {origin}");
        }
    }
}
