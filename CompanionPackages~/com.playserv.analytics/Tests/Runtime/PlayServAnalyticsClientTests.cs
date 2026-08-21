using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Analytics;
using Playserv.Http.Interfaces;
using Playserv.Modules;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime.Analytics
{
    public sealed class PlayServAnalyticsClientTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayServAnalyticsProviderRegistry.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            PlayServAnalyticsProviderRegistry.Reset();
        }

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
        public IEnumerator PerEventUserId_DoesNotMutateSharedUserContext()
        {
            var provider = new RecordingProvider();
            using (var client = CreateClient(provider))
            {
                client.Track("server_event", parameters: null, eventUserId: "plr_1");
                client.Track("runtime_event", parameters: null);

                var flush = client.FlushAsync(CancellationToken.None);
                yield return Await(flush);

                Assert.That(provider.Batches[0].Events[0].UserId, Is.EqualTo("plr_1"));
                Assert.That(provider.Batches[0].Events[1].UserId, Is.EqualTo("runtime-user"));
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

        [UnityTest]
        public IEnumerator ProviderConfiguredBeforeClientCreation_IsUsed()
        {
            var defaultProvider = new RecordingProvider();
            var customProvider = new RecordingProvider();
            PlayServAnalyticsProviderRegistry.Set(customProvider);

            using (var client = CreateClient(
                       new PlayServAnalyticsProviderSelector(defaultProvider)))
            {
                client.Track("custom_provider", parameters: null);
                var flush = client.FlushAsync(CancellationToken.None);
                yield return Await(flush);

                Assert.That(defaultProvider.Batches, Is.Empty);
                Assert.That(customProvider.Batches.Count, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator ResetProvider_RestoresDefaultProvider()
        {
            var defaultProvider = new RecordingProvider();
            var customProvider = new RecordingProvider();
            var selector = new PlayServAnalyticsProviderSelector(defaultProvider);
            PlayServAnalyticsProviderRegistry.Set(customProvider);
            PlayServAnalyticsProviderRegistry.Reset();

            using (var client = CreateClient(selector))
            {
                client.Track("default_provider", parameters: null);
                var flush = client.FlushAsync(CancellationToken.None);
                yield return Await(flush);

                Assert.That(customProvider.Batches, Is.Empty);
                Assert.That(defaultProvider.Batches.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void PublicProviderConfiguration_DoesNotRequireConnection()
        {
            var provider = new RecordingProvider();
            var facade = new PlayServApiAnalyticsFacade(
                new DisconnectedRuntimeAccess());

            Assert.DoesNotThrow(() => facade.SetProvider(provider));
            Assert.That(facade.HasCustomProvider, Is.True);
            Assert.DoesNotThrow(facade.ResetProvider);
            Assert.That(facade.HasCustomProvider, Is.False);
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

        [UnityTest]
        public IEnumerator HttpProvider_PostsRuntimeIngestShapeWithCurrentPlayerToken()
        {
            var http = new RecordingHttpClient();
            var json = new NewtonsoftJsonCodec();
            var settings = new PlayServSettings
            {
                BackendServerAddress = "https://platform.example",
                ClientToken = "pk_test",
                RuntimeTokenProvider = new StaticTokenProvider("player-jwt")
            };
            var provider = new PlayServHttpAnalyticsProvider(settings, http, json);
            var batch = new PlayServAnalyticsBatch
            {
                BatchId = "batch-1",
                SentAtUnixMilliseconds = 1_700_000_000_500,
                Events = new[]
                {
                    new PlayServAnalyticsEvent
                    {
                        EventId = "event-1",
                        Name = "match_finished",
                        TimestampUnixMilliseconds = 1_700_000_000_000,
                        Sequence = 4,
                        SessionId = "session-1",
                        UserId = "plr_1",
                        SdkVersion = "0.4.0",
                        ApplicationVersion = "1.2.3",
                        Platform = "Android",
                        Parameters = new[]
                        {
                            PlayServAnalyticsParameter.Integer("score", 1250),
                            PlayServAnalyticsParameter.Boolean("won", true)
                        },
                        UserProperties = new[]
                        {
                            new PlayServAnalyticsUserProperty("tier", "premium")
                        }
                    }
                }
            };

            var send = provider.SendAsync(batch, CancellationToken.None);
            yield return Await(send);

            Assert.That(provider.IsReady, Is.True);
            Assert.That(http.LastDataRequest, Is.Not.Null);
            Assert.That(http.LastDataRequest.Method, Is.EqualTo("POST"));
            Assert.That(http.LastDataRequest.RelativePath, Is.EqualTo("analytics/events"));
            Assert.That(http.LastDataRequest.ClientToken, Is.EqualTo("pk_test"));
            Assert.That(http.LastDataRequest.BearerToken, Is.EqualTo("player-jwt"));

            var root = (Dictionary<string, object>)json.ParseToPlainValue(
                http.LastDataRequest.JsonBody);
            var events = (List<object>)root["events"];
            var wireEvent = (Dictionary<string, object>)events[0];
            Assert.That(wireEvent["type"], Is.EqualTo("match_finished"));
            Assert.That(wireEvent["channel"], Is.EqualTo("unity"));
            Assert.That(
                Convert.ToDateTime(wireEvent["event_time"]).ToUniversalTime(),
                Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime));

            var payload = (Dictionary<string, object>)wireEvent["payload"];
            Assert.That(payload["event_id"], Is.EqualTo("event-1"));
            Assert.That(payload["user_id"], Is.EqualTo("plr_1"));
            var parameters = (Dictionary<string, object>)payload["parameters"];
            Assert.That(Convert.ToInt64(parameters["score"]), Is.EqualTo(1250));
            Assert.That(parameters["won"], Is.EqualTo(true));
            var properties = (Dictionary<string, object>)payload["user_properties"];
            Assert.That(properties["tier"], Is.EqualTo("premium"));
        }

        [Test]
        public void HttpProvider_RequiresConfiguredRuntimeEndpointAndClientToken()
        {
            var provider = new PlayServHttpAnalyticsProvider(
                new PlayServSettings(),
                new RecordingHttpClient(),
                new NewtonsoftJsonCodec());

            Assert.That(provider.IsReady, Is.False);
            Assert.Throws<InvalidOperationException>(
                () => provider.SendAsync(
                        new PlayServAnalyticsBatch { Events = Array.Empty<PlayServAnalyticsEvent>() })
                    .GetAwaiter()
                    .GetResult());
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

        private sealed class StaticTokenProvider : IPlayServRuntimeTokenProvider
        {
            private readonly string _token;

            public StaticTokenProvider(string token)
            {
                _token = token;
            }

            public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_token);
            }
        }

        private sealed class RecordingHttpClient : IPlayServRuntimeHttpClient
        {
            public PlayServRuntimeDataRequest LastDataRequest { get; private set; }

            public Task<string> GetLatestVersionAsync(
                string gameId,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> SignInAnonAsync(
                string clientToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerRefreshResponseDto> RefreshAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayerTokenBundleDto> LoginExternalAsync(
                string clientToken,
                PlayerExternalLoginRequestDto request,
                string playerAccessToken = null,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task SignOutAsync(
                string clientToken,
                string refreshToken,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<PlayServRuntimeDataResponse> SendDataAsync(
                PlayServRuntimeDataRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                LastDataRequest = request;
                return Task.FromResult(
                    new PlayServRuntimeDataResponse(202, "{\"accepted\":1}", null, null));
            }
        }

        private sealed class DisconnectedRuntimeAccess :
            IPlayServAnalyticsRuntimeAccess
        {
            public IPlayServModuleServiceProvider CurrentServices => null;

            public IPlayServModuleServiceProvider RequiredServices =>
                throw new InvalidOperationException("No active PlayServ connection.");

            public IPlayServModuleServiceProvider GetServicesForFireAndForget(
                string operationName)
            {
                return null;
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
