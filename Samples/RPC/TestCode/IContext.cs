namespace Playserv.Test.RPC
{
    public interface IContext
    {
        void Publish<T>(T @event);
        void PublishForGroup<T>(string groupName, T @event);
        void PublishForUser<T>(string userId, T @event);
    }
}
