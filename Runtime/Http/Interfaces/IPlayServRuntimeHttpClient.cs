using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Http.Interfaces
{
    public interface IPlayServRuntimeHttpClient
    {
        Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default);
    }
}
