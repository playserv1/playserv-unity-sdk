namespace Playserv.Proxy.Interfaces
{
    /// <summary>
    /// Generates unique request identifiers.
    /// </summary>
    public interface IRequestIdGenerator
    {
        /// <summary>
        /// Returns next request id.
        /// </summary>
        /// <returns>Unique request id.</returns>
        int Next();
    }
}
