namespace Playserv.Proxy.Common
{
    internal static class ProxyCommandTypeRegistration
    {
        public static void Register(CommandTypeRegistryBuilder builder)
        {
            builder.Register<HandshakeRequest>();
            builder.Register<HandshakeResponse>();
            builder.Register<KeepAliveRequest>();
            builder.Register<KeepAliveResponse>();
            builder.Register<ClientSettingsResponse>();
            builder.Register<SchemaRequest>();
            builder.Register<ParseErrorResponse>();
            builder.Register<ValidationErrorResponse>();
            builder.Register<CommandErrorResponse>("error", "Error", "RpcErrorResponse");
            builder.Register<ForcedDisconnectResponse>("ForcedDisconnect", "Disconnect");
        }
    }
}
