namespace Playserv.Proxy.Common
{
    public interface ICommandRouteProvider
    {
        void RegisterRoutes(PlayServCommandRouteRegistry routes);
    }
}
