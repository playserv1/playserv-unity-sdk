using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Requests;
using Playserv.DataSubscription.Responses;
using Playserv.Http.Interfaces;
using Playserv.Modules;
using Playserv.Proxy.Common;
using Playserv.Proxy.Logging;
using Playserv.Runtime.Abstractions;
using Playserv.Serialization;
using Playserv.Wrapper;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed class PlayServPlayerOwnedRecordsTests
    {
        [Test]
        public void Public_facade_exposes_current_player_only_helper()
        {
            Assert.That(typeof(PlayServData).GetMethods().Any(method => method.Name == "LoadPlayerOwnedAsync"), Is.True);
            Assert.That(typeof(PlayServRecordSet<Profile>).GetMethods().Any(method => method.Name == "LoadPlayerOwnedAsync"), Is.True);
        }

        [Test]
        public void Existing_row_uses_indexed_lookup_without_factory_or_write()
        {
            var http = new Http();
            var factoryCalls = 0;
            var result = Load(Set<Profile>(http), () => { factoryCalls++; return new Profile(); }).GetAwaiter().GetResult();
            Assert.That(result.WasCreated, Is.False);
            Assert.That(result.Record.Owner, Is.EqualTo("plr_own"));
            Assert.That(result.Record.Value.Name, Is.EqualTo("Stored"));
            Assert.That(result.Record.HasPendingChanges, Is.False, "DTOs may intentionally omit the schema key.");
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(http.Rows.Select(request => request.RelativePath), Is.EqualTo(new[] { "data/tables/ent_profiles/records:by-natural-key?field=player_id&value=plr_own" }));
            Assert.That(http.Requests.All(request => request.ClientToken == "pk_fixture" && request.BearerToken == "fixture-bearer"), Is.True);
        }

        [Test]
        public void Missing_row_seeds_once_and_reads_canonical_server_minted_ID()
        {
            var http = new Http { Missing = true };
            var result = Load(Set<Profile>(http), () => new Profile { Name = "Default" }).GetAwaiter().GetResult();
            Assert.That(result.WasCreated, Is.True);
            Assert.That(result.Record.Id, Is.EqualTo("rec_canonical"));
            Assert.That(result.Record.Value.Name, Is.EqualTo("Stored"));
            Assert.That(http.Rows.Select(request => request.Method), Is.EqualTo(new[] { "GET", "POST", "GET" }));
            var seed = http.Rows[1];
            Assert.That(seed.RelativePath, Is.EqualTo("data/tables/ent_profiles/records:upsert"));
            var body = new NewtonsoftJsonCodec().Deserialize<Dictionary<string, object>>(seed.JsonBody);
            Assert.That(body["field"], Is.EqualTo("player_id"));
            Assert.That(body["value"], Is.EqualTo("plr_own"));
            Assert.That(body["mode"], Is.EqualTo("seed"));
            Assert.That(seed.JsonBody, Does.Contain("\"player_id\":\"plr_own\""));
            Assert.That(seed.JsonBody, Does.Not.Contain("owner").And.Not.Contain("fixture-bearer"));
            Assert.That(http.Rows[2].RelativePath, Is.EqualTo("data/tables/ent_profiles/records/rec_canonical"));
        }

        [TestCase("project", "[\"player_id\"]", false)]
        [TestCase("player", "[]", false)]
        [TestCase("player", "[\"PlayerId\"]", false)]
        [TestCase("player", "[\"player_id\"]", true)]
        [TestCase(null, "[\"player_id\"]", false)]
        public void Ineligible_catalogue_refuses_before_row_IO(string ownedBy, string unique, bool singleton)
        {
            var http = new Http { Catalogue = Catalogue(ownedBy, unique, singleton) };
            Assert.Throws<InvalidOperationException>(() => Load(Set<Profile>(http)).GetAwaiter().GetResult());
            Assert.That(http.Rows, Is.Empty);
        }

        [TestCase("plr_other", "plr_own")]
        [TestCase("plr_own", "plr_other")]
        [TestCase(null, "plr_own")]
        [TestCase("plr_own", null)]
        public void Foreign_or_missing_owner_or_key_is_rejected_before_DTO_conversion(string owner, string key)
        {
            var http = new Http { Row = Row(owner, key) };
            CountingProfile.Conversions = 0;
            var error = Assert.Throws<PlayServDataException>(() => Load(Set<CountingProfile>(http)).GetAwaiter().GetResult());
            Assert.That(error.UnifiedError.SourceCode, Is.EqualTo("invalid_response"));
            Assert.That(CountingProfile.Conversions, Is.Zero);
            Assert.That(error.Message, Does.Not.Contain("plr_other").And.Not.Contain("Stored").And.Not.Contain("fixture-bearer"));
        }

        [Test]
        public void Omitted_key_is_clean_and_business_save_does_not_remove_it()
        {
            var http = new Http();
            var record = Load(Set<Profile>(http)).GetAwaiter().GetResult().Record;
            Assert.That(record.HasPendingChanges, Is.False);
            record.Value.Name = "Changed";
            http.Row = Row(name: "Changed");
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(http.Rows.Last().JsonBody, Is.EqualTo("{\"name\":\"Changed\"}"));
            Assert.That(http.Rows.Last().IfMatch, Is.EqualTo("\"v1\""));
            Assert.That(record.HasPendingChanges, Is.False);
            record.ReloadAsync().GetAwaiter().GetResult();
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [TestCase(null)]
        [TestCase("plr_other")]
        public void Caller_cannot_change_fixed_player_key(string changed)
        {
            var http = new Http();
            var record = Load(Set<KeyedProfile>(http)).GetAwaiter().GetResult().Record;
            record.Value.PlayerId = changed;
            var count = http.Requests.Count;
            Assert.Throws<InvalidOperationException>(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(http.Requests.Count, Is.EqualTo(count));
        }

        [Test]
        public void Identity_is_required_and_server_scope_is_not_supported()
        {
            var http = new Http(); var settings = Settings(); settings.PlayerId = null;
            Assert.Throws<InvalidOperationException>(() => Load(Set<Profile>(http, settings)).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => Load(new PlayServRecordSet<Profile>(new PlayServRecordsClient(
                Settings(), http, new NewtonsoftJsonCodec(), accessSubject: PlayServDataAccessSubject.Server), "ent_profiles")).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void Precancellation_does_not_load_catalogue_or_create_a_row()
        {
            var http = new Http();
            Assert.Throws<OperationCanceledException>(() => Load(Set<Profile>(http), ct: new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.That(http.Requests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Identity_change_during_lookup_discards_response_and_invalidates_handle() => Run(async () =>
        {
            var http = new Http(); var settings = Settings();
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Handler = (request, ct) => pending.Task;
            var load = Load(Set<Profile>(http, settings));
            settings.PlayerId = "plr_new";
            pending.SetResult(Response(http.Row));
            await Throws<InvalidOperationException>(async () => await load);
        });

        [UnityTest]
        public IEnumerator Save_preserves_business_edits_made_while_request_is_pending() => Run(async () =>
        {
            var http = new Http(); var record = (await Load(Set<Profile>(http))).Record;
            record.Value.Name = "Sent";
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Handler = (request, ct) => pending.Task;
            var save = record.SaveAsync(); record.Value.Name = "Newer";
            pending.SetResult(Response(Row(name: "Sent")));
            await save;
            Assert.That(record.Value.Name, Is.EqualTo("Newer"));
            Assert.That(record.HasPendingChanges, Is.True);
            http.Handler = null; http.Row = Row(name: "Newer");
            await record.SaveAsync();
            Assert.That(http.Rows.Last().JsonBody, Is.EqualTo("{\"name\":\"Newer\"}"));
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [Test]
        public void Loaded_handle_rejects_identity_change_even_for_noop_save()
        {
            var http = new Http(); var settings = Settings();
            var record = Load(Set<Profile>(http, settings)).GetAwaiter().GetResult().Record;
            settings.PlayerId = "plr_new";
            var count = http.Requests.Count;
            Assert.Throws<InvalidOperationException>(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => record.ReloadAsync().GetAwaiter().GetResult());
            Assert.That(http.Requests.Count, Is.EqualTo(count));
        }

        [UnityTest]
        public IEnumerator Concurrent_missing_loads_use_seed_and_converge_without_overwrite() => Run(async () =>
        {
            var http = new Http(); var set = Set<Profile>(http);
            var gates = new Queue<TaskCompletionSource<PlayServRuntimeDataResponse>>(); var seeds = 0;
            http.Handler = (request, ct) =>
            {
                if (request.RelativePath.Contains("by-natural-key"))
                {
                    var gate = new TaskCompletionSource<PlayServRuntimeDataResponse>(); gates.Enqueue(gate); return gate.Task;
                }
                if (request.Method == "POST") return Task.FromResult(Response("{\"id\":\"rec_canonical\",\"created\":" + (++seeds == 1 ? "true" : "false") + "}"));
                return Task.FromResult(Response(http.Row));
            };
            var first = Load(set, () => new Profile { Name = "First" });
            var second = Load(set, () => new Profile { Name = "Second" });
            foreach (var gate in gates) gate.SetException(new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false));
            var results = await Task.WhenAll(first, second);
            Assert.That(results.Count(result => result.WasCreated), Is.EqualTo(1));
            Assert.That(results.Select(result => result.Record.Id).Distinct().Count(), Is.EqualTo(1));
            Assert.That(results.All(result => result.Record.Value.Name == "Stored"), Is.True);
            Assert.That(http.Rows.Count(request => request.Method == "POST"), Is.EqualTo(2));
        });

        [TestCase(false, true, false, true)]
        [TestCase(true, false, true, true)]
        [TestCase(true, false, false, false)]
        public void ACL_is_checked_for_read_and_only_checks_write_when_missing(bool read, bool write, bool missing, bool refused)
        {
            var http = new Http { Missing = missing, Catalogue = Catalogue().Replace("\"read\":\"owner\"", "\"read\":\"owner\",\"acl\":{\"client\":{\"read\":" + read.ToString().ToLowerInvariant() + ",\"write\":" + write.ToString().ToLowerInvariant() + "}}") };
            var factoryCalls = 0;
            if (refused) Assert.Throws<PlayServDataAccessDeniedException>(() => Load(Set<Profile>(http), () => { factoryCalls++; return new Profile(); }).GetAwaiter().GetResult());
            else Assert.That(Load(Set<Profile>(http)).GetAwaiter().GetResult().Record, Is.Not.Null);
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(http.Rows.Any(request => request.Method != "GET"), Is.False);
            if (!read) Assert.That(http.Rows, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Identity_change_during_catalogue_or_token_prevents_row_request() => Run(async () =>
        {
            var http = new Http(); var settings = Settings();
            var token = new TaskCompletionSource<string>();
            settings.RuntimeTokenProvider = new PlayServDelegateRuntimeTokenProvider(ct => token.Task);
            var loading = Load(Set<Profile>(http, settings)); settings.PlayerId = "plr_new"; token.SetResult("new-bearer");
            await Throws<InvalidOperationException>(async () => await loading);
            Assert.That(http.Requests, Is.Empty);
            http = new Http(); settings = Settings(); var catalogue = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.CatalogueHandler = ct => catalogue.Task;
            loading = Load(Set<Profile>(http, settings)); settings.PlayerId = "plr_new"; catalogue.SetResult(Response(http.Catalogue));
            await Throws<InvalidOperationException>(async () => await loading);
            Assert.That(http.Rows, Is.Empty);
        });

        [UnityTest]
        public IEnumerator Identity_change_during_seed_prevents_canonical_read() => Run(async () =>
        {
            var http = new Http { Missing = true }; var settings = Settings();
            var seed = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Handler = (request, ct) => request.Method == "POST" ? seed.Task : throw new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false);
            var load = Load(Set<Profile>(http, settings)); settings.PlayerId = "plr_new";
            seed.SetResult(Response("{\"id\":\"rec_canonical\",\"created\":true}"));
            await Throws<InvalidOperationException>(async () => await load);
            Assert.That(http.Rows, Has.Count.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Pending_seed_cancellation_does_not_hydrate_or_retry() => Run(async () =>
        {
            var http = new Http { Missing = true }; using var cancellation = new CancellationTokenSource();
            var seed = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Handler = (request, ct) => request.Method == "POST" ? seed.Task : throw new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false);
            var load = Load(Set<Profile>(http), ct: cancellation.Token); cancellation.Cancel();
            seed.SetResult(Response("{\"id\":\"rec_canonical\",\"created\":true}"));
            await Throws<OperationCanceledException>(async () => await load);
            Assert.That(http.Rows, Has.Count.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Late_save_and_reload_never_install_a_different_identity_response() => Run(async () =>
        {
            foreach (var save in new[] { false, true })
            {
                var http = new Http(); var settings = Settings(); var record = (await Load(Set<Profile>(http, settings))).Record;
                if (save) record.Value.Name = "Sent";
                var response = new TaskCompletionSource<PlayServRuntimeDataResponse>(); http.Handler = (request, ct) => response.Task;
                var operation = save ? record.SaveAsync() : record.ReloadAsync();
                settings.PlayerId = "plr_new"; response.SetResult(Response(Row(name: "Foreign")));
                await Throws<InvalidOperationException>(async () => await operation);
                Assert.Throws<InvalidOperationException>(() => { var ignored = record.Value; });
                settings.PlayerId = "plr_own";
                Assert.That(record.Value.Name, Is.EqualTo(save ? "Sent" : "Stored"));
            }
        });

        [Test]
        public void Public_read_policy_does_not_make_foreign_row_an_own_record()
        {
            var http = new Http { Catalogue = Catalogue().Replace("\"owner\"", "\"public\""), Row = Row("plr_other", "plr_own") };
            var set = Set<Profile>(http);
            Assert.Throws<PlayServDataException>(() => Load(set).GetAwaiter().GetResult());
            Assert.That(set.LoadAsync("rec_canonical").GetAwaiter().GetResult().Owner, Is.EqualTo("plr_other"), "General public Records retain public reads.");
        }

        [UnityTest]
        public IEnumerator Managed_session_refreshes_same_player_without_anonymous_replacement() => Run(async () =>
        {
            foreach (var fail in new[] { false, true })
            {
                var http = new Http(); var settings = Settings(); settings.EnableAutomaticPlayerFingerprint = false;
                using var session = new PlayServPlayerSession(settings, http, new Store(), SynchronizationContext.Current);
                await session.GetTokenAsync(); settings.RuntimeTokenProvider = session;
                var record = (await Load(Set<Profile>(http, settings))).Record;
                typeof(PlayServPlayerSession).GetField("_accessTokenExpiresAtUtc", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(session, DateTimeOffset.MinValue);
                http.FailRefresh = fail;
                var count = http.Requests.Count;
                if (fail)
                {
                    await Throws<PlayServSessionRejectedException>(() => record.ReloadAsync());
                    Assert.That(http.Requests.Count, Is.EqualTo(count));
                }
                else { await record.ReloadAsync(); Assert.That(http.Requests.Last().BearerToken, Is.EqualTo("refreshed-bearer")); }
                Assert.That(http.AnonymousCalls, Is.EqualTo(1)); Assert.That(http.RefreshCalls, Is.EqualTo(1));
            }
        });

        [Test]
        public void Subscription_retains_real_adapter_and_omitted_key_never_becomes_a_false_local_edit()
        {
            var http = new Http(); var settings = Settings(); var bus = new Bus();
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new Logger(), new NewtonsoftJsonCodec());
            var set = new PlayServRecordSet<Profile>(new PlayServRecordsClient(settings, http, new NewtonsoftJsonCodec()), "ent_profiles", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = Load(set).GetAwaiter().GetResult().Record;
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            Assert.That(http.Rows, Has.Count.EqualTo(1), "Matching baseline needs no metadata reload.");
            PlayServRecordChange<Profile> change = null; subscription.Changed += update => change = update;
            http.Row = Row(name: "Operator"); bus.Update("Operator");
            Assert.That(record.Value.Name, Is.EqualTo("Operator")); Assert.That(record.HasPendingChanges, Is.False);
            Assert.That(change.OverwrotePendingChanges, Is.False);
            Assert.That(change.ChangedFields, Is.EqualTo(new[] { "name" }));
            var count = http.Requests.Count; change = null; settings.PlayerId = "plr_new"; bus.Update("Late");
            Assert.That(http.Requests.Count, Is.EqualTo(count)); Assert.That(change, Is.Null);
        }

        [Test]
        public void Identity_change_during_matching_subscription_baseline_releases_lease()
        {
            var http = new Http(); var settings = Settings(); var bus = new Bus { BeforeBaseline = () => settings.PlayerId = "plr_new" };
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new Logger(), new NewtonsoftJsonCodec());
            var set = new PlayServRecordSet<Profile>(new PlayServRecordsClient(settings, http, new NewtonsoftJsonCodec()), "ent_profiles", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = Load(set).GetAwaiter().GetResult().Record;
            Assert.Throws<InvalidOperationException>(() => record.SubscribeAsync().GetAwaiter().GetResult());
            Assert.That(bus.Closes, Is.EqualTo(1)); Assert.That(http.Rows, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Conflict_callback_invalidation_stops_following_changed_delivery(bool dispose)
        {
            var http = new Http(); var settings = Settings(); var bus = new Bus();
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new Logger(), new NewtonsoftJsonCodec());
            var set = new PlayServRecordSet<Profile>(new PlayServRecordsClient(settings, http, new NewtonsoftJsonCodec()), "ent_profiles", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = Load(set).GetAwaiter().GetResult().Record;
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            var conflicts = 0; var changes = 0;
            subscription.Conflict += conflict =>
            {
                conflicts++;
                if (dispose) subscription.Dispose();
                else settings.PlayerId = "plr_new";
            };
            subscription.Changed += change => changes++;
            record.Value.Name = "Unsaved";
            http.Row = Row(name: "Operator"); bus.Update("Operator");
            Assert.That(conflicts, Is.EqualTo(1), "The real canonical refresh must first deliver its conflict.");
            Assert.That(changes, Is.Zero, "A conflict callback may invalidate this handle before Changed is delivered.");
        }

        [Test]
        public void Changed_subscriber_identity_switch_stops_later_subscriber_delivery()
        {
            var http = new Http(); var settings = Settings(); var bus = new Bus();
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new Logger(), new NewtonsoftJsonCodec());
            var set = new PlayServRecordSet<Profile>(new PlayServRecordsClient(settings, http, new NewtonsoftJsonCodec()), "ent_profiles", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = Load(set).GetAwaiter().GetResult().Record;
            using var subscription = record.SubscribeAsync().GetAwaiter().GetResult();
            var first = 0; var second = 0;
            subscription.Changed += change => { first++; settings.PlayerId = "plr_new"; };
            subscription.Changed += change => second++;
            http.Row = Row(name: "Operator"); bus.Update("Operator");
            Assert.That(first, Is.EqualTo(1), "The first subscriber receives the current identity's update.");
            Assert.That(second, Is.Zero, "Delivery must revalidate identity between subscribers.");
        }

        [UnityTest]
        public IEnumerator Identity_change_during_realtime_refresh_releases_lease_without_another_push() => Run(async () =>
        {
            var http = new Http(); var settings = Settings(); var bus = new Bus();
            using var adapter = new PlayServDataSubscriptionAdapter(bus, new Logger(), new NewtonsoftJsonCodec());
            var set = new PlayServRecordSet<Profile>(new PlayServRecordsClient(settings, http, new NewtonsoftJsonCodec()), "ent_profiles", subscribeRecordAsync: adapter.SelectTypedRecordAsync);
            var record = (await Load(set)).Record;
            using var subscription = await record.SubscribeAsync();
            var observedFailure = new TaskCompletionSource<bool>();
            subscription.Failure += failure => observedFailure.TrySetResult(true);
            var pending = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Handler = (request, ct) => pending.Task;
            bus.Update("Pending");
            settings.PlayerId = "plr_new";
            pending.SetResult(Response(Row(name: "Pending")));
            await observedFailure.Task;
            Assert.That(bus.Closes, Is.EqualTo(1), "The old identity's transport lease must be released without another server update.");
            Assert.That(subscription.State, Is.EqualTo(PlayServSubscriptionState.Terminated).Or.EqualTo(PlayServSubscriptionState.Closed));
        });

        [UnityTest]
        public IEnumerator Identity_change_during_final_seed_read_discards_hydration() => Run(async () =>
        {
            var http = new Http(); var settings = Settings(); var final = new TaskCompletionSource<PlayServRuntimeDataResponse>();
            http.Handler = (request, ct) => request.Method == "POST"
                ? Task.FromResult(Response("{\"id\":\"rec_canonical\",\"created\":true}"))
                : request.RelativePath.Contains("by-natural-key")
                    ? throw new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false)
                    : final.Task;
            var load = Load(Set<Profile>(http, settings)); settings.PlayerId = "plr_new"; final.SetResult(Response(http.Row));
            await Throws<InvalidOperationException>(async () => await load);
            Assert.That(http.Rows, Has.Count.EqualTo(3));
        });

        [TestCase("owner", "12")]
        [TestCase("owner", "[]")]
        [TestCase("player_id", "12")]
        [TestCase("player_id", "{}")]
        public void Non_string_ownership_metadata_never_undergoes_permissive_conversion(string field, string raw)
        {
            var http = new Http { Row = Row().Replace("\"" + field + "\":\"plr_own\"", "\"" + field + "\":" + raw) };
            Assert.Throws<PlayServDataException>(() => Load(Set<Profile>(http)).GetAwaiter().GetResult());
        }

        [Test]
        public void Seed_defaults_cannot_assign_another_owner_or_another_player_key()
        {
            var http = new Http { Missing = true };
            Assert.Throws<InvalidOperationException>(() => Load(Set<KeyedProfile>(http), () => new KeyedProfile { PlayerId = "other", Name = "secret" }).GetAwaiter().GetResult());
            Assert.That(http.Rows, Has.Count.EqualTo(1));
            http = new Http { Missing = true };
            var result = Load(Set<Dictionary<string, object>>(http), () => new Dictionary<string, object> { ["name"] = "Default", ["owner"] = "forged" }).GetAwaiter().GetResult();
            Assert.That(http.Rows[1].JsonBody, Does.Not.Contain("forged").And.Not.Contain("owner"));
            Assert.That(result.Record.Owner, Is.EqualTo("plr_own"));
        }

        [Test]
        public void Missing_row_without_factory_seeds_only_key_and_null_factory_result_does_not_write()
        {
            var http = new Http { Missing = true };
            Load(Set<Profile>(http)).GetAwaiter().GetResult();
            Assert.That(http.Rows[1].JsonBody, Does.Contain("\"record\":{\"player_id\":\"plr_own\"}"));
            http = new Http { Missing = true };
            Assert.Throws<InvalidOperationException>(() => Load(Set<Profile>(http), () => null).GetAwaiter().GetResult());
            Assert.That(http.Rows, Has.Count.EqualTo(1));
        }

        [Test]
        public void Reload_and_save_reject_foreign_canonical_result_without_overwriting_value()
        {
            foreach (var save in new[] { false, true })
            {
                var http = new Http(); var record = Load(Set<Profile>(http)).GetAwaiter().GetResult().Record;
                if (save) record.Value.Name = "Pending";
                http.Row = Row(owner: "plr_other", name: "Foreign");
                Assert.Throws<PlayServDataException>(() => (save ? record.SaveAsync() : record.ReloadAsync()).GetAwaiter().GetResult());
                Assert.That(record.Value.Name, Is.EqualTo(save ? "Pending" : "Stored"));
                Assert.That(record.ETag, Is.EqualTo("\"v1\""));
            }
        }

        [UnityTest]
        public IEnumerator Cancelled_save_preserves_unsent_changes_and_never_retries() => Run(async () =>
        {
            var http = new Http(); var record = (await Load(Set<Profile>(http))).Record; record.Value.Name = "Pending";
            var response = new TaskCompletionSource<PlayServRuntimeDataResponse>(); http.Handler = (request, ct) => response.Task;
            using var cancellation = new CancellationTokenSource(); var save = record.SaveAsync(cancellation.Token);
            cancellation.Cancel(); response.SetResult(Response(Row(name: "Server")));
            await Throws<OperationCanceledException>(async () => await save);
            Assert.That(record.Value.Name, Is.EqualTo("Pending")); Assert.That(record.HasPendingChanges, Is.True);
            Assert.That(http.Rows.Count(request => request.Method == "PATCH"), Is.EqualTo(1));
        });

        [TestCase(409, "record_unique_conflict", typeof(PlayServRecordConflictException))]
        [TestCase(403, "table_write_forbidden", typeof(PlayServDataAccessDeniedException))]
        [TestCase(412, "precondition_failed", typeof(PlayServRecordConflictException))]
        public void Write_refusals_keep_typed_backend_code_and_do_not_retry(int status, string code, Type errorType)
        {
            var http = new Http(); var record = Load(Set<Profile>(http)).GetAwaiter().GetResult().Record;
            record.Value.Name = "Pending";
            http.Handler = (request, ct) => throw new PlayServRuntimeHttpException("refused", status, "{}", code, false);
            var error = Assert.Throws(errorType, () => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(((PlayServDataException)error).UnifiedError.SourceCode, Is.EqualTo(code));
            Assert.That(http.Rows.Count(request => request.Method == "PATCH"), Is.EqualTo(1));
            Assert.That(record.HasPendingChanges, Is.True);
        }

        internal sealed class Profile { [JsonProperty("name")] public string Name; }
        internal sealed class KeyedProfile { [JsonProperty("name")] public string Name; [JsonProperty("player_id")] public string PlayerId; }
        internal sealed class CountingProfile
        {
            internal static int Conversions;
            [JsonProperty("name")] public string Name { get; set; }
            public CountingProfile() { Conversions++; }
        }

        private static PlayServSettings Settings() => new PlayServSettings
        {
            BackendServerAddress = "https://owned-" + Guid.NewGuid().ToString("N") + ".test",
            ClientToken = "pk_fixture", PlayerId = "plr_own", PlayerAccessToken = "fixture-bearer"
        };
        private static PlayServRecordSet<T> Set<T>(Http http, PlayServSettings settings = null) =>
            new PlayServRecordSet<T>(new PlayServRecordsClient(settings ?? Settings(), http, new NewtonsoftJsonCodec()), "ent_profiles");
        private static Task<PlayServLoadOrCreateResult<T>> Load<T>(PlayServRecordSet<T> set, Func<T> factory = null, CancellationToken ct = default)
            => set.LoadPlayerOwnedAsync(factory, ct);
        private static string Catalogue(string ownedBy = "player", string unique = "[\"player_id\"]", bool singleton = false) =>
            "{\"data\":[{\"entity_id\":\"ent_profiles\",\"name\":\"Profile\",\"read\":\"owner\",\"owned_by\":" + JsonConvert.SerializeObject(ownedBy) +
            ",\"unique_fields\":" + unique + ",\"singleton\":" + singleton.ToString().ToLowerInvariant() + "}]}";
        private static string Row(string owner = "plr_own", string key = "plr_own", string name = "Stored") =>
            "{\"id\":\"rec_canonical\",\"created_at\":\"2026-10-22T00:00:00Z\",\"updated_at\":\"2026-10-22T00:00:00Z\",\"owner\":" +
            JsonConvert.SerializeObject(owner) + ",\"player_id\":" + JsonConvert.SerializeObject(key) + ",\"name\":" + JsonConvert.SerializeObject(name) + "}";
        private static PlayServRuntimeDataResponse Response(string body) => new PlayServRuntimeDataResponse(200, body, "\"v1\"", null);
        private static IEnumerator Run(Func<Task> action)
        {
            var task = action(); while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }
        private static async Task Throws<T>(Func<Task> action) where T : Exception
        {
            try { await action(); } catch (T) { return; }
            Assert.Fail("Expected " + typeof(T).Name);
        }
        private sealed class Http : IPlayServRuntimeHttpClient
        {
            internal string Catalogue = PlayServPlayerOwnedRecordsTests.Catalogue();
            internal string Row = PlayServPlayerOwnedRecordsTests.Row();
            internal bool Missing;
            internal Func<PlayServRuntimeDataRequest, CancellationToken, Task<PlayServRuntimeDataResponse>> Handler;
            internal Func<CancellationToken, Task<PlayServRuntimeDataResponse>> CatalogueHandler;
            internal int AnonymousCalls, RefreshCalls;
            internal bool FailRefresh;
            internal readonly List<PlayServRuntimeDataRequest> Requests = new List<PlayServRuntimeDataRequest>();
            internal List<PlayServRuntimeDataRequest> Rows => Requests.Where(request => request.RelativePath != "data/tables").ToList();
            public Task<PlayServRuntimeDataResponse> SendDataAsync(PlayServRuntimeDataRequest request, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Requests.Add(request);
                if (request.RelativePath == "data/tables") return CatalogueHandler == null ? Task.FromResult(Response(Catalogue)) : CatalogueHandler(ct);
                if (Handler != null) return Handler(request, ct);
                if (request.RelativePath.Contains("records:by-natural-key") && Missing)
                    throw new PlayServRuntimeHttpException("missing", 404, "{}", "record_not_found", false);
                if (request.RelativePath.EndsWith("records:upsert"))
                    return Task.FromResult(Response("{\"id\":\"rec_canonical\",\"created\":true}"));
                return Task.FromResult(Response(Row));
            }
            public Task<string> GetLatestVersionAsync(string gameId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<PlayerTokenBundleDto> SignInAnonAsync(string clientToken, CancellationToken ct = default)
            {
                AnonymousCalls++;
                return Task.FromResult(new PlayerTokenBundleDto { player_id = "plr_own", access_token = "fixture-bearer", refresh_token = "refresh", expires_in = 3600, refresh_expires_in = 86400, issued_at = DateTimeOffset.UtcNow.ToString("O") });
            }
            public Task<PlayerRefreshResponseDto> RefreshAsync(string clientToken, string refreshToken, CancellationToken ct = default)
            {
                RefreshCalls++;
                if (FailRefresh) throw new PlayServRuntimeHttpException("denied", 401, "{}", "unauthorized", false);
                return Task.FromResult(new PlayerRefreshResponseDto { access_token = "refreshed-bearer", refresh_token = "refresh2", expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"), refresh_expires_in = 86400 });
            }
            public Task<PlayerTokenBundleDto> LoginExternalAsync(string clientToken, PlayerExternalLoginRequestDto request, string playerAccessToken = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task SignOutAsync(string clientToken, string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private sealed class Store : IPlayServPlayerSessionStore
        {
            public Task<PlayServPlayerSessionData> LoadAsync(string scopeKey, CancellationToken cancellationToken = default) => Task.FromResult<PlayServPlayerSessionData>(null);
            public Task SaveAsync(string scopeKey, PlayServPlayerSessionData session, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ClearAsync(string scopeKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
        private sealed class Logger : ILogger
        {
            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
        private sealed class Bus : IPlayServCommandBus
        {
            private readonly Dictionary<Type, List<Delegate>> _listeners = new Dictionary<Type, List<Delegate>>();
            internal Action BeforeBaseline;
            internal int Closes;
            public PlayServState State => PlayServState.Online;
            public IDisposable On<T>(Action<T> action)
            {
                if (!_listeners.TryGetValue(typeof(T), out var callbacks)) _listeners[typeof(T)] = callbacks = new List<Delegate>();
                callbacks.Add(action); return new Callback(() => callbacks.Remove(action));
            }
            public IDisposable OnCommand(string name, Action<object> action) => new Callback(() => { });
            public Task SendAsync<T>(T command, string moduleName = null)
            {
                if (command is DataAuthorityContextRequest context) Emit(new DataAuthorityContextResponse { RequestId = context.RequestId, Error = new DataSubscriptionError { ErrorCode = 32001 } });
                if (command is DataSubscriptionRequest open)
                {
                    Emit(new DataSubscriptionResponse { RequestId = open.RequestId, Result = new DataSubscriptionResult { SubscriptionId = 91 } });
                    BeforeBaseline?.Invoke(); Update("Stored");
                }
                if (command is DataSubscriptionCloseRequest close)
                {
                    Closes++; Emit(new DataSubscriptionCloseResponse { RequestId = close.RequestId, Result = new DataSubscriptionCloseResult { Success = true } });
                }
                return Task.CompletedTask;
            }
            internal void Update(string name) => Emit(new DataSubscriptionUpdate { DataSubscriptionId = 91, UpdateType = "Overwrite", IsCollection = false, Data = new Dictionary<string, object> { ["player_id"] = "plr_own", ["name"] = name } });
            private void Emit<T>(T value)
            {
                if (_listeners.TryGetValue(typeof(T), out var callbacks)) foreach (var callback in callbacks.ToArray()) ((Action<T>)callback)(value);
            }
        }
        private sealed class Callback : IDisposable
        {
            private Action _action;
            internal Callback(Action action) { _action = action; }
            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}
