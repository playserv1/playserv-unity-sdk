namespace Playserv.Modules
{
    public interface IPlayServModuleServiceRegistry : IPlayServModuleServiceProvider
    {
        void Register<TService>(TService service)
            where TService : class;
    }
}
