using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Analytics;
using Playserv.Proxy.Logging;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime.Analytics
{
    public sealed class PlayServAnalyticsClientTests
    {
        [UnityTest]
        public IEnumerator TrackAndFlush_SendsTypedContextBatch()
        {
            var provider = new RecordingProvider();
            using (var client = CreateClient(provider))
            {
                client.SetUserProperty("role", "parent");
                client.Track(
                    "login_sso_success",
                    new Dictionary<string, object>
                    {
                        { "attempt", 2 },
                        { "duration", 1.5f },
                        { "provider", "google" },
                        { "new_user", true }
                    });

                Assert.That(client.PendingEventCount, Is.EqualTo(1));
                var flush = client.FlushAsync(CancellationToken.None);
                yield return Await(flush);

                Assert.That(client.PendingEventCount, Is.Zero);
                Assert.That(provider.Batches.Count, Is.EqualTo(1));
                var analyticsEvent = provider.Batches[0].Events[0];
                Assert.That(analyticsEvent.Name, Is.EqualTo("login_sso_success"));
                Assert.That(analyticsEvent.UserId, Is.EqualTo("runtime-user"));
                Assert.That(analyticsEvent.SessionId, Is.Not.Empty);
                Assert.That(analyticsEvent.SdkVersion, Is.EqualTo("test-sdk"));
                Assert.That(analyticsEvent.ApplicationVersion, Is.EqualTo("1.2.3"));
                Assert.That(analyticsEvent.Platform, Is.EqualTo("Editor"));
                Assert.That(analyticsEvent.Parameters, Has.Length.EqualTo(4));
                Assert.That(
                    analyticsEvent.Parameters[0].Type,
                    Is.EqualTo(PlayServAnalyticsParameterType.Integer));
                Assert.That(
                    analyticsEvent.Parameters[1].Type,
                    Is.EqualTo(PlayServAnalyticsParameterType.Number));
                Assert.That(
                    analyticsEvent.Parameters[2].Type,
                    Is.EqualTo(PlayServAnalyticsParameterType.Boolean));
                Assert.That(
                    analyticsEvent.Parameters[3].Type,
                    Is.EqualTo(PlayServAnalyticsParameterType.String));
                Assert.That(analyticsEvent.UserProperties, Has.Length.EqualTo(1));
                Assert.That(analyticsEvent.UserProperties[0].Key, Is.EqualTo("role"));
            }
        }

        [UnityTest]
        public IEnumerator FailedFlush_RetainsQueuedEvents()
        {
            var provider = new RecordingProvider
            {
                SendException = new InvalidOperationException("ingestion unavailable")
            };
            using (var client = CreateClient(provider))
            {
                client.Track("auto_login", parameters: null);

                Exception failure = null;
                var flush = client.FlushAsync(CancellationToken.None);
                yield return AwaitCompletion(flush);
                try
                {
                    flush.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(client.PendingEventCount, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator QueueLimit_DropsOldestEvents()
        {
            var provider = new RecordingProvider
            {
                IsReady = false
            };
            using (var client = CreateClient(
                       provider,
                       batchSize: 3,
                       maxQueueSize: 3))
            {
                client.Track("event_0", parameters: null);
                client.Track("event_1", parameters: null);
                client.Track("event_2", parameters: null);
                client.Track("event_3", parameters: null);

                Assert.That(client.PendingEventCount, Is.EqualTo(3));
                provider.IsReady = true;
                var flush = client.FlushAsync(CancellationToken.None);
                yield return Await(flush);

                Assert.That(provider.Batches.Count, Is.EqualTo(1));
                Assert.That(
                    provider.Batches[0].Events[0].Name,
                    Is.EqualTo("event_1"));
            }
        }

        [Test]
        public void DisablingCollection_ClearsAndIgnoresEvents()
        {
            using (var client = CreateClient(new RecordingProvider()))
            {
                client.Track("queued_event", parameters: null);
                client.SetCollectionEnabled(false);
                client.Track("ignored_event", parameters: null);

                Assert.That(client.CollectionEnabled, Is.False);
                Assert.That(client.PendingEventCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator SetProvider_RoutesPendingEventsToReplacement()
        {
            var original = new RecordingProvider
            {
                IsReady = false
            };
            var replacement = new RecordingProvider();
            using (var client = CreateClient(original))
            {
                client.SetUserId("explicit-user");
                client.Track("provider_swap", parameters: null);
                client.SetProvider(replacement);
                var flush = client.FlushAsync(CancellationToken.None);
                yield return Await(flush);

                Assert.That(original.Batches, Is.Empty);
                Assert.That(replacement.Batches.Count, Is.EqualTo(1));
                Assert.That(
                    replacement.Batches[0].Events[0].UserId,
                    Is.EqualTo("explicit-user"));
            }
        }

        [Test]
        public void UnsupportedParameterType_IsRejected()
        {
            using (var client = CreateClient(new RecordingProvider()))
            {
                Assert.Throws<ArgumentException>(
                    () => client.Track(
                        "invalid_parameter",
                        new Dictionary<string, object>
                        {
                            { "payload", new object() }
                        }));
            }
        }

        private static PlayServAnalyticsClient CreateClient(
            IPlayServAnalyticsProvider provider,
            int batchSize = 20,
            int maxQueueSize = 100)
        {
            return new PlayServAnalyticsClient(
                provider,
                () => "runtime-user",
                new TestLogger(),
                "test-sdk",
                "1.2.3",
                "Editor",
                batchSize,
                maxQueueSize,
                TimeSpan.Zero);
        }

        private static IEnumerator Await(Task task)
        {
            yield return AwaitCompletion(task);
            if (task.IsFaulted)
                throw task.Exception?.InnerException ?? task.Exception;
        }

        private static IEnumerator AwaitCompletion(Task task)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                yield return null;

            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for the analytics test task.");
        }

        private sealed class RecordingProvider : IPlayServAnalyticsProvider
        {
            public readonly List<PlayServAnalyticsBatch> Batches =
                new List<PlayServAnalyticsBatch>();

            public bool IsReady { get; set; } = true;

            public Exception SendException { get; set; }

            public Task SendAsync(
                PlayServAnalyticsBatch batch,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SendException != null)
                    throw SendException;

                Batches.Add(batch);
                return Task.CompletedTask;
            }
        }

        private sealed class TestLogger : ILogger
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
