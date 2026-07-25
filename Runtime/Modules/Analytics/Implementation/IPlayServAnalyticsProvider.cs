using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Analytics
{
    /// <summary>
    /// Sends prepared analytics batches to PlayServ or a custom ingestion target.
    /// </summary>
    public interface IPlayServAnalyticsProvider
    {
        bool IsReady { get; }

        Task SendAsync(
            PlayServAnalyticsBatch batch,
            CancellationToken cancellationToken = default);
    }
}
