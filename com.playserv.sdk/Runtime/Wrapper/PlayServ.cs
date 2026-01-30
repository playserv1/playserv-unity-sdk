using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    public static class PlayServ
    {
        private static readonly IPlayServApi Api = new PlayServApi();

        public static string SdkVersion => Api.SdkVersion;

        public static PlayServState State => Api.State;

        public static event Action<TransportError>? OnTransportError
        {
            add => Api.OnTransportError += value;
            remove => Api.OnTransportError -= value;
        }

        public static event Action? OnKeepAlivePingSent
        {
            add => Api.OnKeepAlivePingSent += value;
            remove => Api.OnKeepAlivePingSent -= value;
        }

        public static event Action? OnKeepAlivePongReceived
        {
            add => Api.OnKeepAlivePongReceived += value;
            remove => Api.OnKeepAlivePongReceived -= value;
        }

        public static void Config(string gameAccessToken, string gameVersion, string sdkVersion = null) =>
            Api.Config(gameAccessToken, gameVersion, sdkVersion);

        public static Task<bool> Connect() =>
            Api.Connect();

        public static IObservable<T> Subscribe<T>() =>
            Api.Subscribe<T>();

        public static IDisposable Subscribe<T>(Action<T> onNext) =>
            Api.Subscribe(onNext);

        public static void Send<T>(T command) =>
            Api.Send(command);

        // NOTE: only for testing purposes
        public static Playserv.Proxy.Interfaces.ITransportImplementation GetTransportImplementation() =>
            Api.GetTransportImplementation();

        public static void Publish<T>(T @event) =>
            Api.Publish(@event);

        public static void PublishForGroup<T>(string groupName, T @event) =>
            Api.PublishForGroup(groupName, @event);

        public static void PublishForUser<T>(string userId, T @event) =>
            Api.PublishForUser(userId, @event);

        public static void Disconnect() =>
            Api.Disconnect();

        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new() =>
            Api.SelectEntity<TEntity, TDto>(playerId, map);
    }
}