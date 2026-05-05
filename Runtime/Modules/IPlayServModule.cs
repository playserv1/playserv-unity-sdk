namespace Playserv.Modules
{
    public interface IPlayServModule
    {
        PlayServModuleDescriptor Descriptor { get; }

        void Initialize(PlayServModuleContext context);

        void Shutdown();
    }
}
