using Playserv.Serialization;

namespace Playserv.Proxy.Common
{
    internal sealed class ProxyCommandTypeProvider : ICommandTypeProvider
    {
        public void RegisterCommandTypes(CommandTypeRegistryBuilder builder)
        {
            builder.Register<HandshakeRequest>();
            builder.Register<HandshakeResponse>();
            builder.Register<KeepAliveRequest>();
            builder.Register<KeepAliveResponse>();
            builder.Register<EventMessage>("BroadcastEvent");
            builder.Register<ClientSettingsResponse>();
            builder.Register<SchemaRequest>();
            builder.Register<ParseErrorResponse>();
            builder.Register<ValidationErrorResponse>();
            builder.Register<ErrorResponse>();
            builder.Register<CommandErrorResponse>("error", "Error", "RpcErrorResponse");
            builder.Register<ForcedDisconnectResponse>("ForcedDisconnect", "Disconnect");
        }
    }
}
