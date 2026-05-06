namespace Playserv.Modules
{
    public interface IPlayServModuleServiceRegistry : IPlayServModuleServiceProvider
    {
        void Register<TService>(TService service)
            where TService : class;

        void Register<TService>(TService service, PlayServModuleServiceRegistrationPolicy policy)
            where TService : class;

        bool TryRegister<TService>(TService service)
            where TService : class;

        void Register<TService>(string name, TService service)
            where TService : class;

        void Register<TService>(string name, TService service, PlayServModuleServiceRegistrationPolicy policy)
            where TService : class;

        bool TryRegister<TService>(string name, TService service)
            where TService : class;

        void RegisterCapability<TCapability>(string capabilityName, TCapability capability)
            where TCapability : class;
    }
}
