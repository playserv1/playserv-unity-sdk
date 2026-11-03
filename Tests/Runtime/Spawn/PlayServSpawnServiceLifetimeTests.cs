using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Events;
using Playserv.Spawn;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime.Spawn
{
    public sealed class PlayServSpawnServiceLifetimeTests
    {
        [UnityTest]
        public IEnumerator Dispose_CancelsTrackedSpawnTimeout()
        {
            var delay = new CancellableDelay();
            var prefab = new GameObject("SpawnLifetimePrefab");
            prefab.AddComponent<NetworkObject>();
            GameObject instance = null;
            var service = new SpawnService(1000, delay.WaitAsync);

            try
            {
                service.Initialize(new TestEventsAdapter(), () => "local-user");
                service.SetPrefabRegistry(new TestPrefabRegistry(prefab));

                var spawnTask = service.SpawnAsync("test-prefab", Vector3.zero, Quaternion.identity);
                yield return AwaitWithTimeout(spawnTask);
                instance = spawnTask.Result;
                yield return AwaitWithTimeout(delay.Started.Task);
                Assert.That(instance, Is.Not.Null);
                Assert.That(service.BackgroundTaskCount, Is.EqualTo(1));

                service.Dispose();
                yield return AwaitWithTimeout(delay.Cancelled.Task);
                yield return AwaitWithTimeout(service.WaitForBackgroundTasksAsync());

                Assert.That(service.BackgroundTaskCount, Is.Zero);
            }
            finally
            {
                service.Dispose();
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        private static IEnumerator AwaitWithTimeout(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 3f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for spawn lifetime task.");
            if (task.IsFaulted)
                throw task.Exception;
        }

        private sealed class CancellableDelay
        {
            public TaskCompletionSource<bool> Started { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Cancelled { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task WaitAsync(int delayMs, CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() =>
                {
                    Cancelled.TrySetResult(true);
                    completion.TrySetCanceled();
                });
                return completion.Task;
            }
        }

        private sealed class TestPrefabRegistry : INetworkPrefabRegistry
        {
            private readonly GameObject _prefab;

            public TestPrefabRegistry(GameObject prefab)
            {
                _prefab = prefab;
            }

            public bool TryLoadPrefab(string prefabId, out GameObject prefab)
            {
                prefab = _prefab;
                return true;
            }
        }

        private sealed class TestEventsAdapter : IEventsAdapter
        {
            public IObservable<T> Subscribe<T>()
            {
                return new EmptyObservable<T>();
            }

            public IObservable<string> SubscribeRaw<T>()
            {
                return new EmptyObservable<string>();
            }

            public IDisposable Subscribe<T>(Action<T> onNext)
            {
                return NoOpDisposable.Instance;
            }

            public IDisposable SubscribeRaw<T>(Action<string> onNext)
            {
                return NoOpDisposable.Instance;
            }

            public void Publish<T>(T @event)
            {
            }

            public void PublishForGroup<T>(string groupName, T @event)
            {
            }

            public void PublishForUser<T>(string userId, T @event)
            {
            }

            public Task<bool> SubscribeGroupAsync(string groupName, CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }

            public Task<bool> UnsubscribeGroupAsync(string groupName, CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }
        }

        private sealed class EmptyObservable<T> : IObservable<T>
        {
            public IDisposable Subscribe(IObserver<T> observer)
            {
                return NoOpDisposable.Instance;
            }
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new NoOpDisposable();

            public void Dispose()
            {
            }
        }
    }
}
