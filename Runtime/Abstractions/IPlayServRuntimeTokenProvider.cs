using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Runtime.Abstractions
{
    /// <summary>
    /// Supplies the current player JWT at connection time without serializing it into Unity assets.
    /// </summary>
    public interface IPlayServRuntimeTokenProvider
    {
        /// <summary>
        /// Returns a raw player JWT or a value prefixed with <c>Bearer </c>.
        /// </summary>
        Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
    }
}
