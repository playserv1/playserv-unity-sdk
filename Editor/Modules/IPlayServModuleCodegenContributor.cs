namespace Playserv.Editor
{
    public interface IPlayServModuleCodegenContributor
    {
        string ModuleId { get; }

        int Order { get; }

        void Contribute(PlayServModuleCodegenContext context);
    }
}
