using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Data;
using Playserv.Http.Interfaces;
using Playserv.Schema;
using UnityEngine.TestTools;

namespace Playserv.Tests.Runtime
{
    public sealed partial class PlayServTypedRecordsTests
    {
        [PlayServSchema(Kind = PlayServSchemaKind.Part)]
        private sealed class SplitPart { public int Score; }
        private sealed class SplitModel
        {
            public string Name;
            [PlayServField("parts")] public SplitPart[] Parts;
            [PlayServField("friend", Type = PlayServSchemaFieldType.Relation, Target = "Friend")]
            public string Friend;
        }

        private sealed class SplitServer
        {
            internal readonly FakeDataHttpClient Http = new FakeDataHttpClient();
            internal readonly JObject Row = JObject.Parse("{\"id\":\"rec_split\",\"Name\":\"old\",\"parts\":[],\"friend\":null}");
            internal string FailField;
            internal int Version = 1;
            internal bool FailRead;
            internal string Tag => "\"v" + Version + "\"";
            internal SplitServer(bool singleton = false)
            {
                Http.ConfigureDefaultCatalogue("ent_split", nameof(SplitModel), singleton);
                Http.DataHandler = (request, ct) =>
                {
                    if (request.Method == "GET")
                    {
                        if (FailRead) throw new InvalidOperationException("network unavailable");
                        return Reply(Row);
                    }
                    if (request.IfMatch != null && request.IfMatch != Tag)
                        throw HttpError(412, "precondition_failed", "{\"code\":\"precondition_failed\"}");
                    var body = JToken.Parse(request.JsonBody);
                    if (request.Method == "PUT")
                    {
                        var field = Uri.UnescapeDataString(request.RelativePath.Split('/').Last());
                        if (FailField == field) throw HttpError(422, "validation_failed", "{\"code\":\"validation_failed\"}");
                        Version++;
                        if (request.RelativePath.Contains("/includes/")) { Row[field] = body; return Reply(body); }
                        Row[field] = body["child_ids"].First?.DeepClone();
                        return Reply(body, false);
                    }
                    foreach (var property in ((JObject)body).Properties()) Row[property.Name] = property.Value.DeepClone();
                    Version++;
                    return Reply(Row);
                };
            }
            private Task<PlayServRuntimeDataResponse> Reply(JToken body, bool tag = true) => Task.FromResult(
                new PlayServRuntimeDataResponse(200, body.ToString(), tag ? Tag : null, null));
            internal PlayServRecordSet<SplitModel> Set => CreateSet<SplitModel>(Http, "ent_split");
        }

        [Test]
        public void SplitSave_create_uses_dedicated_routes_and_retains_server_identity()
        {
            var server = new SplitServer();
            var record = server.Set.CreateAsync(new SplitModel { Name = "new", Parts = new[] { new SplitPart { Score = 3 } }, Friend = "rec_friend" }).GetAwaiter().GetResult();
            var writes = server.Http.Requests.Where(r => r.Method != "GET").ToArray();
            Assert.That(writes.Select(r => r.Method), Is.EqualTo(new[] { "POST", "PUT", "PUT" }));
            Assert.That(JObject.Parse(writes[0].JsonBody).Properties().Select(p => p.Name), Is.EqualTo(new[] { "Name" }));
            Assert.That(writes.Any(r => r.RelativePath.EndsWith("/includes/parts") && JArray.Parse(r.JsonBody).Count == 1), Is.True);
            Assert.That(writes.Any(r => r.RelativePath.EndsWith("/links/friend") && (string)JObject.Parse(r.JsonBody)["child_ids"][0] == "rec_friend"), Is.True);
            Assert.That(record.Id, Is.EqualTo("rec_split"));
            Assert.That(record.ETag, Is.EqualTo(server.Tag));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [Test]
        public void SplitSave_failed_nested_create_exposes_handle_and_retry_does_not_post_again()
        {
            var server = new SplitServer { FailField = "parts" };
            var error = Assert.Catch(() => server.Set.CreateAsync(new SplitModel { Name = "new", Parts = new[] { new SplitPart { Score = 3 } }, Friend = "rec_friend" }).GetAwaiter().GetResult());
            Assert.That(error, Is.Not.Null, "Nested failure must be visible even after parent create succeeded.");
            var record = error.GetType().GetProperty("Record")?.GetValue(error) as PlayServRecord<SplitModel>;
            Assert.That(record, Is.Not.Null, "The failure must expose the already-created recovery handle.");
            Assert.That(record.Id, Is.EqualTo("rec_split"));
            Assert.That(record.HasPendingChanges, Is.True);
            Assert.That(((PlayServSplitSaveException<SplitModel>)error).CompletedFields, Does.Contain("Name"));
            server.FailField = null;
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(server.Http.Requests.Count(r => r.Method == "POST"), Is.EqualTo(1));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SplitSave_clears_nested_values_for_record_and_singleton(bool singleton)
        {
            var server = new SplitServer(singleton);
            server.Row["parts"] = JArray.Parse("[{\"Score\":1}]"); server.Row["friend"] = "rec_friend";
            if (singleton)
            {
                var record = server.Set.GetSingletonAsync().GetAwaiter().GetResult();
                record.Value.Parts = null; record.Value.Friend = null;
                record.SaveAsync().GetAwaiter().GetResult();
                Assert.That(record.HasPendingChanges, Is.False);
            }
            else
            {
                var record = server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
                record.Value.Parts = null; record.Value.Friend = null;
                record.SaveAsync().GetAwaiter().GetResult();
                Assert.That(record.HasPendingChanges, Is.False);
            }
            var writes = server.Http.Requests.Where(r => r.Method != "GET").ToArray();
            Assert.That(writes.Select(r => r.Method), Is.EqualTo(new[] { "PUT", "PUT" }));
            Assert.That(writes.Single(r => r.RelativePath.Contains("/includes/")).JsonBody, Is.EqualTo("[]"));
            Assert.That(JObject.Parse(writes.Single(r => r.RelativePath.Contains("/links/")).JsonBody)["child_ids"], Is.Empty);
            Assert.That(server.Row["parts"], Is.Empty);
            Assert.That(writes.All(r => r.RelativePath.Contains(singleton ? "/singleton/" : "/records/rec_split/")), Is.True);
        }

        [Test]
        public void SplitSave_nested_only_detects_stale_parent_before_overwriting()
        {
            var server = new SplitServer();
            var record = server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            record.Value.Friend = "rec_mine";
            server.Row["friend"] = "rec_theirs"; server.Version++;
            var error = Assert.Catch(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(error, Is.InstanceOf<PlayServRecordConflictException>());
            Assert.That(server.Http.Requests.Any(r => r.Method == "PUT"), Is.False);
            Assert.That(record.Value.Friend, Is.EqualTo("rec_mine"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SplitSave_failed_readback_requires_reconciliation_preserving_edits(bool singleton)
        {
            var server = new SplitServer(singleton);
            object handle = singleton ? (object)server.Set.GetSingletonAsync().GetAwaiter().GetResult()
                : server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            // Use reflection so the RED test compiles before the recovery API exists.
            var type = ((object)handle).GetType();
            var value = (SplitModel)type.GetProperty("Value").GetValue(handle);
            value.Name = "accepted"; value.Friend = "rec_pending";
            server.FailField = "friend"; server.FailRead = true;
            Assert.Catch(() => ((Task)type.GetMethod("SaveAsync").Invoke(handle, new object[] { default(System.Threading.CancellationToken) })).GetAwaiter().GetResult());
            Assert.That((bool)type.GetProperty("RequiresReconciliation").GetValue(handle), Is.True);
            server.FailField = null; server.FailRead = false;
            var reconcile = type.GetMethod("ReconcileAsync");
            Assert.That(reconcile, Is.Not.Null, "Recovery must retain pending changes instead of requiring a destructive reload.");
            ((Task)reconcile.Invoke(handle, new object[] { default(System.Threading.CancellationToken) })).GetAwaiter().GetResult();
            Assert.That(((SplitModel)type.GetProperty("Value").GetValue(handle)).Friend, Is.EqualTo("rec_pending"));
            ((Task)type.GetMethod("SaveAsync").Invoke(handle, new object[] { default(System.Threading.CancellationToken) })).GetAwaiter().GetResult();
            Assert.That((bool)type.GetProperty("HasPendingChanges").GetValue(handle), Is.False);
        }

        [Test]
        public void SplitSave_bulk_failure_retains_accepted_fields_and_retries_only_pending()
        {
            var server = new SplitServer { FailField = "parts" };
            var set = server.Set;
            var record = set.LoadAsync("rec_split").GetAwaiter().GetResult();
            record.Value.Name = "accepted"; record.Value.Friend = "rec_friend";
            record.Value.Parts = new[] { new SplitPart { Score = 4 } };
            var result = set.BulkSaveAsync(new[] { record }).GetAwaiter().GetResult();
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Items[0].Exception, Is.InstanceOf<PlayServSplitSaveException<SplitModel>>());
            server.FailField = null;
            var before = server.Http.Requests.Count;
            record.SaveAsync().GetAwaiter().GetResult();
            var writes = server.Http.Requests.Skip(before).Where(r => r.Method != "GET").ToArray();
            Assert.That(writes, Has.Length.EqualTo(1));
            Assert.That(writes[0].RelativePath, Does.EndWith("/includes/parts"));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [UnityTest]
        public IEnumerator SplitSave_pending_steps_do_not_lose_edits_made_during_IO() => QueueRun(async () =>
        {
            var server = new SplitServer();
            var record = await server.Set.LoadAsync("rec_split");
            record.Value.Name = "first"; record.Value.Friend = "rec_friend";
            record.Value.Parts = new[] { new SplitPart { Score = 3 } };
            var handler = server.Http.DataHandler;
            var gate = new TaskCompletionSource<bool>();
            server.Http.DataHandler = async (request, ct) =>
            {
                if (request.RelativePath.EndsWith("/includes/parts")) await gate.Task;
                return await handler(request, ct);
            };
            var save = record.SaveAsync();
            await QueueUntil(() => server.Http.Requests.Any(r => r.RelativePath.EndsWith("/includes/parts")));
            record.Value.Name = "second"; record.Value.Parts[0].Score = 9;
            gate.SetResult(true); await save;
            Assert.That(record.Value.Name, Is.EqualTo("second"));
            Assert.That(record.Value.Parts[0].Score, Is.EqualTo(9));
            Assert.That(record.HasPendingChanges, Is.True);
            await record.SaveAsync();
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [Test]
        public void SplitSave_server_normalization_is_not_mistaken_for_a_local_edit()
        {
            var server = new SplitServer();
            var record = server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            var handler = server.Http.DataHandler;
            server.Http.DataHandler = async (request, ct) =>
            {
                var reply = await handler(request, ct);
                if (request.Method == "PATCH")
                {
                    server.Row["Name"] = "normalized";
                    return new PlayServRuntimeDataResponse(200, server.Row.ToString(), server.Tag, null);
                }
                if (request.Method == "PUT") server.Row["Name"] = "canonical";
                return reply;
            };
            record.Value.Name = "input"; record.Value.Friend = "rec_friend";
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(record.Value.Name, Is.EqualTo("canonical"));
            Assert.That(record.HasPendingChanges, Is.False);
        }

        [Test]
        public void SplitSave_ambiguous_inline_response_blocks_repeated_write_until_reconciliation()
        {
            var server = new SplitServer();
            var record = server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            var handler = server.Http.DataHandler;
            server.Http.DataHandler = async (request, ct) =>
            {
                var reply = await handler(request, ct);
                if (request.Method == "PATCH") { server.FailRead = true; throw new InvalidOperationException("connection lost after acceptance"); }
                return reply;
            };
            record.Value.Name = "accepted"; record.Value.Friend = "rec_friend";
            Assert.Catch(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(record.RequiresReconciliation, Is.True);
            var count = server.Http.Requests.Count;
            Assert.Catch(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(server.Http.Requests.Count, Is.EqualTo(count));
            server.Http.DataHandler = handler; server.FailRead = false;
            record.ReconcileAsync().GetAwaiter().GetResult();
            record.SaveAsync().GetAwaiter().GetResult();
            Assert.That(record.HasPendingChanges, Is.False);
        }

        private sealed class SplitShapes
        {
            [PlayServField("one", Type = PlayServSchemaFieldType.Inclusion)] public SplitPart One;
            [PlayServField("ordered", Type = PlayServSchemaFieldType.Relation, Ordered = true, Target = "Friend")]
            public string[] Ordered;
            [PlayServField(Type = PlayServSchemaFieldType.Json)] public SplitPart Json;
            [PlayServIgnore] public SplitPart Ignored;
        }

        [Test]
        public void SplitSave_single_part_ordered_links_and_raw_JSON_use_distinct_shapes()
        {
            var http = new FakeDataHttpClient();
            var set = CreateSet<SplitShapes>(http, "ent_shapes");
            http.EnqueueJson(201, "{\"id\":\"rec_shapes\",\"one\":null,\"ordered\":[],\"Json\":{\"Score\":8}}", "\"v1\"");
            http.EnqueueJson(200, "[{\"id\":\"prt_part\",\"Score\":2}]", "\"v2\"");
            http.EnqueueJson(200, "{\"child_ids\":[\"rec_b\",\"rec_a\"]}");
            http.EnqueueJson(200, "{\"id\":\"rec_shapes\",\"one\":{\"id\":\"prt_part\",\"Score\":2},\"ordered\":[\"rec_b\",\"rec_a\"],\"Json\":{\"Score\":8}}", "\"v3\"");
            var result = set.CreateAsync(new SplitShapes { One = new SplitPart { Score = 2 }, Ordered = new[] { "rec_b", "rec_a" }, Json = new SplitPart { Score = 8 }, Ignored = new SplitPart() }).GetAwaiter().GetResult();
            Assert.That(JObject.Parse(http.Requests[0].JsonBody).Properties().Select(p => p.Name), Is.EqualTo(new[] { "Json" }));
            Assert.That(http.Requests[1].JsonBody, Is.EqualTo("[{\"Score\":2}]"));
            Assert.That(JObject.Parse(http.Requests[2].JsonBody)["positions"].Values<int>(), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(result.Value.One.Score, Is.EqualTo(2));
            Assert.That(result.HasPendingChanges, Is.False, "Server-only part IDs must not create phantom dirty fields.");
        }

        [Test]
        public void SplitSave_invalid_nested_payload_is_rejected_before_parent_write()
        {
            var server = new SplitServer();
            Assert.Throws<ArgumentException>(() => server.Set.CreateAsync(new SplitModel { Name = "new", Friend = " " }).GetAwaiter().GetResult());
            Assert.That(server.Http.Requests, Is.Empty);
        }

        [Test]
        public void SplitSave_parent_validation_failure_keeps_backend_code_and_never_retries()
        {
            var server = new SplitServer();
            server.Http.DataHandler = (request, ct) => throw HttpError(422, "missing_required_field", "{\"code\":\"missing_required_field\"}");
            var error = Assert.Throws<PlayServSplitSaveException<SplitModel>>(() => server.Set.CreateAsync(new SplitModel { Parts = new[] { new SplitPart() } }).GetAwaiter().GetResult());
            Assert.That(error.Record, Is.Null);
            Assert.That(error.BackendCode, Is.EqualTo("missing_required_field"));
            Assert.That(server.Http.Requests.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SplitSave_canceled_waiter_does_not_cancel_an_accepted_record_save() => QueueRun(async () =>
        {
            var server = new SplitServer();
            var record = await server.Set.LoadAsync("rec_split");
            record.Value.Name = "accepted"; record.Value.Friend = "rec_friend";
            var gate = new TaskCompletionSource<bool>();
            var handler = server.Http.DataHandler;
            server.Http.DataHandler = async (request, ct) =>
            {
                if (request.Method == "PUT") await gate.Task;
                return await handler(request, ct);
            };
            using var cancel = new System.Threading.CancellationTokenSource();
            var save = record.SaveAsync(cancel.Token);
            cancel.Cancel();
            await CaptureExceptionAsync<OperationCanceledException>(() => save);
            var following = record.SaveAsync();
            gate.SetResult(true); await following;
            Assert.That(server.Http.Requests.Count(request => request.Method == "PUT"), Is.EqualTo(1));
            Assert.That(record.HasPendingChanges, Is.False);
        });

        [TestCase(false, "ReconcileAsync")]
        [TestCase(true, "ReconcileAsync")]
        [TestCase(false, "ReloadAsync")]
        [TestCase(true, "ReloadAsync")]
        [TestCase(false, "SynchronizeRealtimeAsync")]
        public void SplitSave_unversioned_recovery_does_not_remove_the_write_guard(bool singleton, string method)
        {
            var server = new SplitServer(singleton);
            object handle = singleton ? (object)server.Set.GetSingletonAsync().GetAwaiter().GetResult()
                : server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            var type = handle.GetType();
            var value = (SplitModel)type.GetProperty("Value").GetValue(handle);
            value.Name = "accepted"; value.Friend = "rec_pending";
            server.FailField = "friend"; server.FailRead = true;
            Assert.Catch(() => ((Task)type.GetMethod("SaveAsync").Invoke(handle, new object[] { default(System.Threading.CancellationToken) })).GetAwaiter().GetResult());
            server.Http.DataHandler = (request, ct) => Task.FromResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), null, null));
            var recovery = type.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.Catch(() => ((Task)recovery.Invoke(handle, new object[] { default(System.Threading.CancellationToken) })).GetAwaiter().GetResult());
            Assert.That((bool)type.GetProperty("RequiresReconciliation").GetValue(handle), Is.True);
            Assert.That(((SplitModel)type.GetProperty("Value").GetValue(handle)).Friend, Is.EqualTo("rec_pending"));
        }

        [Test]
        public void SplitSave_unversioned_readback_retains_known_tag_and_blocks_delete()
        {
            var server = new SplitServer();
            var record = server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            record.Value.Name = "accepted"; record.Value.Parts = new[] { new SplitPart { Score = 3 } };
            var handler = server.Http.DataHandler;
            server.Http.DataHandler = (request, ct) => request.Method == "GET"
                ? Task.FromResult(new PlayServRuntimeDataResponse(200, server.Row.ToString(), null, null)) : handler(request, ct);
            Assert.Throws<PlayServSplitSaveException<SplitModel>>(() => record.SaveAsync().GetAwaiter().GetResult());
            Assert.That(record.ETag, Is.EqualTo(server.Tag), "An invalid readback cannot replace the last acknowledged version.");
            Assert.That(record.RequiresReconciliation, Is.True);
            var requests = server.Http.Requests.Count;
            Assert.Catch(() => record.DeleteAsync().GetAwaiter().GetResult());
            Assert.That(server.Http.Requests.Count, Is.EqualTo(requests), "Deletion must not bypass a required reconciliation.");
        }

        [Test]
        public void SplitSave_failed_readback_blocks_delete_before_HTTP()
        {
            var server = new SplitServer();
            var record = server.Set.LoadAsync("rec_split").GetAwaiter().GetResult();
            record.Value.Name = "accepted"; record.Value.Friend = "rec_friend";
            server.FailRead = true;
            Assert.Catch(() => record.SaveAsync().GetAwaiter().GetResult());
            var count = server.Http.Requests.Count;
            Assert.Catch(() => record.DeleteAsync().GetAwaiter().GetResult());
            Assert.That(server.Http.Requests.Count, Is.EqualTo(count));
        }

        private sealed class SplitDictionaryPart
        {
            [PlayServField(Type = PlayServSchemaFieldType.Inclusion, Target = "Part")]
            public Dictionary<string, object> Part;
        }

        [Test]
        public void SplitSave_dictionary_part_is_one_object_not_a_collection()
        {
            var http = new FakeDataHttpClient();
            var set = CreateSet<SplitDictionaryPart>(http, "ent_dictionary");
            http.EnqueueJson(201, "{\"id\":\"rec_dict\",\"Part\":null}", "\"v1\"");
            http.EnqueueJson(200, "[{\"id\":\"prt_part\",\"Score\":3}]", "\"v2\"");
            http.EnqueueJson(200, "{\"id\":\"rec_dict\",\"Part\":{\"id\":\"prt_part\",\"Score\":3}}", "\"v2\"");
            var record = set.CreateAsync(new SplitDictionaryPart { Part = new Dictionary<string, object> { ["Score"] = 3 } }).GetAwaiter().GetResult();
            Assert.That(http.Requests[1].JsonBody, Is.EqualTo("[{\"Score\":3}]"));
            Assert.That(record.HasPendingChanges, Is.False);
        }
    }
}
