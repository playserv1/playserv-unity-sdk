using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Persists the refresh credential used by automatic PlayServ player authentication.
    /// Implement this interface to use platform-secure storage instead of the default PlayerPrefs store.
    /// </summary>
    public interface IPlayServPlayerSessionStore
    {
        Task<PlayServPlayerSessionData> LoadAsync(
            string scopeKey,
            CancellationToken cancellationToken = default);

        Task SaveAsync(
            string scopeKey,
            PlayServPlayerSessionData session,
            CancellationToken cancellationToken = default);

        Task ClearAsync(
            string scopeKey,
            CancellationToken cancellationToken = default);
    }
}
