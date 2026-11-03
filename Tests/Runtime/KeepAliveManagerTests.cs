using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Proxy.Common;
using Playserv.Proxy.Interfaces;
using UnityEngine;
using UnityEngine.TestTools;
using ISdkLogger = Playserv.Proxy.Logging.ILogger;

namespace Playserv.Tests.Runtime
{
    public sealed class KeepAliveManagerTests
    {
        [UnityTest]
        public IEnumerator Stop_DrainsLegacyReplyAndLoopTasks()
        {
            var transport = new BlockingLegacyReplyTransport();
            using var manager = new KeepAliveManager(transport, new TestLogger())
            {
                PingIntervalMs = 60000,
                PongTimeoutMs = 10000
            };

            manager.Start();
            transport.Publish(new KeepAliveRequest());
            yield return AwaitWithTimeout(transport.LegacyReplyStarted.Task);

            manager.Stop();
            transport.CompleteLegacyReply();
            yield return AwaitWithTimeout(manager.WaitForBackgroundTasksAsync());

            Assert.That(manager.BackgroundTaskCount, Is.Zero);
        }

        [Test]
        public void Stop_PreventsCallbacksFromLaterKeepAliveEvents()
        {
            var transport = new BlockingLegacyReplyTransport();
            using var manager = new KeepAliveManager(transport, new TestLogger());
            var pongCount = 0;
            manager.PongReceived += () => pongCount++;

            manager.Start();
            manager.Stop();
            transport.Publish(new EventMessage("KeepAlive", "{}"));

            Assert.That(pongCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator StopImmediately_DrainsScheduledLoops()
        {
            var transport = new BlockingLegacyReplyTransport();
            using var manager = new KeepAliveManager(transport, new TestLogger());

            manager.Start();
            manager.Stop();
            yield return AwaitWithTimeout(manager.WaitForBackgroundTasksAsync());

            Assert.That(manager.BackgroundTaskCount, Is.Zero);
        }

        private static IEnumerator AwaitWithTimeout(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 3f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for managed background tasks.");
            if (task.IsFaulted)
                throw task.Exception;
        }

        private sealed class BlockingLegacyReplyTransport : ITransport
        {
            private readonly TestObservable<KeepAliveRequest> _legacyRequests =
                new TestObservable<KeepAliveRequest>();
            private readonly TestObservable<EventMessage> _events =
                new TestObservable<EventMessage>();
            private readonly TaskCompletionSource<bool> _legacyReplyRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public event EventHandler ConnectionLost;

            public TaskCompletionSource<bool> LegacyReplyStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<bool> Connect()
            {
                return Task.FromResult(true);
            }

            public Task Send<T>(T command, string moduleName = null)
            {
                if (command is KeepAliveResponse)
                {
                    LegacyReplyStarted.TrySetResult(true);
                    return _legacyReplyRelease.Task;
                }

                return Task.CompletedTask;
            }

            public void ResetConnection()
            {
            }

            public IObservable<T> OnReceive<T>()
            {
                if (typeof(T) == typeof(KeepAliveRequest))
                    return (IObservable<T>)(object)_legacyRequests;
                if (typeof(T) == typeof(EventMessage))
                    return (IObservable<T>)(object)_events;

                return new TestObservable<T>();
            }

            public IObservable<object> OnReceive(string commandName)
            {
                return new TestObservable<object>();
            }

            public void Publish(KeepAliveRequest request)
            {
                _legacyRequests.Publish(request);
            }

            public void Publish(EventMessage message)
            {
                _events.Publish(message);
            }

            public void CompleteLegacyReply()
            {
                _legacyReplyRelease.TrySetResult(true);
            }

            public void Dispose()
            {
            }
        }

        private sealed class TestObservable<T> : IObservable<T>
        {
            private readonly object _gate = new object();
            private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();

            public IDisposable Subscribe(IObserver<T> observer)
            {
                lock (_gate)
                    _observers.Add(observer);

                return new Subscription(this, observer);
            }

            public void Publish(T value)
            {
                IObserver<T>[] observers;
                lock (_gate)
                    observers = _observers.ToArray();

                for (var i = 0; i < observers.Length; i++)
                    observers[i].OnNext(value);
            }

            private void Unsubscribe(IObserver<T> observer)
            {
                lock (_gate)
                    _observers.Remove(observer);
            }

            private sealed class Subscription : IDisposable
            {
                private TestObservable<T> _owner;
                private IObserver<T> _observer;

                public Subscription(TestObservable<T> owner, IObserver<T> observer)
                {
                    _owner = owner;
                    _observer = observer;
                }

                public void Dispose()
                {
                    var owner = _owner;
                    var observer = _observer;
                    _owner = null;
                    _observer = null;
                    owner?.Unsubscribe(observer);
                }
            }
        }

        private sealed class TestLogger : ISdkLogger
        {
            public void Log(string message)
            {
            }

            public void LogWarning(string message)
            {
            }

            public void LogError(string message)
            {
            }
        }
    }
}
