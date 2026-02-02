using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
using UnityEngine;

#nullable enable

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

        public static void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string? sdkVersion = null) =>
        Api.Config(gameAccessToken, gameId, userId, gameVersion, sdkVersion);

        public static Task<bool> Connect() =>
            Api.Connect();

        public static IObservable<T> Subscribe<T>() =>
            Api.Subscribe<T>();

        public static IDisposable Subscribe<T>(Action<T> onNext) =>
            Api.Subscribe(onNext);

        public static void Send<T>(T command) =>
            Api.Send(command);
        
        public static void Send<T>(T command, string moduleName) =>
            Api.Send(command,  moduleName);

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
        
        public static Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation) =>
            Api.Spawn(assetName, position, rotation);

        public static Task<GameObject> Spawn(string assetName, Vector3 position) =>
            Api.Spawn(assetName, position);

        public static Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new() =>
            Api.SelectEntity<TEntity, TDto>(playerId, map);
    }
}

#nullable restore