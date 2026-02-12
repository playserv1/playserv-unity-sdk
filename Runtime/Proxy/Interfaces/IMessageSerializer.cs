using Playserv.Proxy.Implementation;

namespace Playserv.Proxy.Interfaces
{
    /// <summary>
    /// Serializer that converts commands to/from transport envelope representation.
    /// </summary>
    public interface IMessageSerializer
    {
        /// <summary>
        /// Serializes command object to envelope.
        /// </summary>
        /// <typeparam name="T">Command type.</typeparam>
        /// <param name="command">Command payload.</param>
        /// <param name="moduleName">Optional module path override.</param>
        /// <returns>Serialized command envelope.</returns>
        MessageEnvelope Serialize<T>(T command, string moduleName = null);

        /// <summary>
        /// Deserializes envelope to strongly typed command object.
        /// </summary>
        /// <param name="envelope">Input envelope.</param>
        /// <returns>Deserialized command instance.</returns>
        object Deserialize(MessageEnvelope envelope);
    }
}
