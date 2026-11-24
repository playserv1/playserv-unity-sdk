# Field observers on managed records

`PlayServRecord<T>.ObserveField` observes an exact, case-sensitive top-level
persisted wire field. It uses the existing `PlayServFieldChange<T>` payload and
returns an `IDisposable`; registration does not open a network subscription or
replay the current value. This works on ordinary typed records, records retained
from managed collections, and canonically loaded reference records.

```csharp
using Playserv.DataSubscription;

var record = await PlayServData.Records<InventoryItem>().LoadAsync(itemId, ct: ct);
using var watch = record.ObserveField("durability", change =>
    ShowDurabilityChange(change.Previous, change.Current));
record.Changed += change => RefreshInventoryUi(change.Record.Value);
await record.ReloadAsync(ct);
```

Remote subscription refreshes, explicit `ReloadAsync` and a loaded reference's
`ReloadAsync` use the same notification path. The record has its complete new
value, snapshot, timestamps and ETag before callbacks run. The order is:

1. Field observers, in registration order.
2. Existing subscription/managed-collection conflict callbacks when applicable.
3. The record's `Changed` event.
4. Active record-subscription `Changed` and managed-collection `RecordChanged`.
5. Successful operation completion, followed by the next queued operation.

`record.Changed.ChangedFields` describes effective persisted value changes from
the live value immediately before overwrite. Subscription/collection change
payloads retain their existing meaning: changes from the previous canonical
snapshot, which can differ when local edits existed. A metadata-only refresh is
silent for fields and `record.Changed`; subscription events keep their existing
metadata-refresh behavior. Explicit reload now reaches active record wrappers too.

Previous values are detached copies of the live persisted state just before
overwrite, including unsaved local edits. `Current` and `Value` are detached too;
mutating a payload does not change the record or another callback's payload.
Comparison uses Records serialization, so equivalent reference IDs are unchanged
even when deserialization constructs new reference objects. Ignored fields are
not reported. An object/array change produces one callback per registration on
its top-level field, not one per nested member. Array order matters; object key
order does not. Managed observation describes mapped persisted DTO values;
missing server fields reset to DTO defaults rather than exposing the shared
view's raw-wire presence semantics.

Initial loading, first reference hydration, subscription initialization, local
assignment and save responses are silent. An unchanged reload is silent. A
reload that discards a local edit does change the live value and reports it.
Realtime retains its existing backend-wins policy. Refresh paths ignore reads
whose non-default `updated_at` predates the handle's accepted timestamp, with
the same clock/precision limits as existing realtime comparisons.
`ReconcileAsync` retains its explicit save-recovery behavior; it is not a reload
notification. This is a coalesced current-state view, not an every-write history.

For a single-record subscription, complete versioned pushes use this same
notification path without a REST GET (PSV-3734). Missing/invalid metadata,
patch-only payloads, ambiguous timestamps and reconnect/reset recovery still use
canonical reads. Callbacks see the installed snapshot and conditional-write token
on either path; duplicate or obsolete frames are silent. REST-only metadata absent
from a push keeps its last loaded value until a canonical read. Managed collection
member refreshes continue to use their existing canonical reads.

Create record handles and register observers on the Unity thread. Delivery uses
the record queue's captured context, outside SDK locks. Callbacks may dispose
observers or enqueue another record operation; do not synchronously block on an
operation queued by a callback. Exceptions from one callback do not prevent the
remaining callbacks or fail the refresh. Disposing a watch suppresses later
delivery, including remaining delivery in the same batch. Authorization-context
changes suppress remaining notifications on context-bound references and
player-owned records, including changes made by an earlier callback.
Public cancellation can end a caller's
wait before an already dispatched operation completes; it does not retract an
accepted update or its notifications.

A record watch belongs to that handle. Leaving or closing a managed collection
detaches its collection events; a separately retained record/watch can still
observe explicit reloads. Dispose watches when the consumer no longer needs them.
A reference keeps its canonical handle for subsequent successful rehydration,
including recovery after a failed read; missing/faulted references retain their
existing public state and error behavior and do not fabricate field removals.

Shared entities and snapshot collections keep their
[original observer contract](playserv-sdk.md#field-observation-psv-3207): initial
delivery and whole-view `Changed` before fields. Managed record notification
ordering does not change that API.
