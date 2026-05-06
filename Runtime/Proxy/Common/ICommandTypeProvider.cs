namespace Playserv.Proxy.Common
{
    public interface ICommandTypeProvider
    {
        void RegisterCommandTypes(CommandTypeRegistryBuilder builder);
    }
}
