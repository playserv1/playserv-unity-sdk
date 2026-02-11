using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.Proxy.Common;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

#nullable enable

namespace Playserv.Wrapper
{
    public interface IPlayServApi
    {
        string SdkVersion { get; }
        PlayServState State { get; }

        event Action<TransportError>? OnTransportError;
        event Action? OnKeepAlivePingSent;
        event Action? OnKeepAlivePongReceived;

        void Config(PlayServSettings settings);
        void Config(string gameAccessToken, string gameId, string userId, string gameVersion, string? sdkVersion = null);
        Task<bool> Connect();

        IObservable<T> Subscribe<T>();
        IDisposable Subscribe<T>(Action<T> onNext);

        void Send<T>(T command);
        void Send<T>(T command, string moduleName);

        // NOTE: only for testing purposes
        Proxy.Interfaces.ITransportImplementation GetTransportImplementation();

        void Publish<T>(T @event);
        void PublishForGroup<T>(string groupName, T @event);
        void PublishForUser<T>(string userId, T @event);

#if UNITY_5_3_OR_NEWER
        Task<GameObject> Spawn(string assetName, Vector3 position, Quaternion rotation);
        Task<GameObject> Spawn(string assetName, Vector3 position);
#endif

        void Disconnect();

        Task<ISharedEntity<TDto>> SelectEntity<TEntity, TDto>(string playerId, Func<TEntity, TDto> map)
            where TEntity : class
            where TDto : class, new();
    }
}

#nullable restore
