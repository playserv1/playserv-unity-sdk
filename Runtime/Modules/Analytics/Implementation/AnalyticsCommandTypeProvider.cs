using Playserv.Proxy.Common;

namespace Playserv.Analytics
{
    internal sealed class AnalyticsCommandTypeProvider : ICommandTypeProvider
    {
        public void RegisterCommandTypes(CommandTypeRegistryBuilder builder)
        {
            builder.Register<PlayServAnalyticsBatch>("TrackAnalyticsBatch");
        }
    }
}
