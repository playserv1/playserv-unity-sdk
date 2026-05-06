using System;
using Playserv.Events;

namespace Playserv.Spawn
{
    internal static class PlayServSpawnRuntime
    {
        private static readonly IDisposable EmptySubscription = new EmptyDisposable();
        private static IEventsAdapter _eventsAdapter;

        public static int TransformSyncIntervalMs { get; set; } = 100;

        public static bool HasEventsAdapter => _eventsAdapter != null;

        public static void Initialize(IEventsAdapter eventsAdapter)
        {
            _eventsAdapter = eventsAdapter ?? throw new ArgumentNullException(nameof(eventsAdapter));
        }

        public static void Clear()
        {
            _eventsAdapter = null;
        }

        public static IDisposable Subscribe<T>(Action<T> onNext)
        {
            return _eventsAdapter == null ? EmptySubscription : _eventsAdapter.Subscribe(onNext);
        }

        public static void Publish<T>(T @event)
        {
            _eventsAdapter?.Publish(@event);
        }

        public static void PublishForGroup<T>(string groupName, T @event)
        {
            _eventsAdapter?.PublishForGroup(groupName, @event);
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
