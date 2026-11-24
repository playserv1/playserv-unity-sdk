# Stable records in live collections

`Records<T>().SubscribeManagedAsync(query, ct)` returns an
`IPlayServManagedCollection<T>`. Each item is a saveable `PlayServRecord<T>`.
While an ID stays in this subscription, both its handle and its root `Value`
instance stay the same through pushes, saves, reloads and reconciliation.
Nested objects and arrays may be replaced. Two subscriptions have independent
objects even when they share one transport subscription.

Retained records support [field observers](managed-record-observers.md) through
`record.ObserveField(...)`. They run before record and collection notifications,
including when that record is explicitly reloaded while the collection is open.

```csharp
using var live = await PlayServData.Records<InventoryItem>()
    .SubscribeManagedAsync(new PlayServRecordQuery<InventoryItem>()
        .Where(item => item.Durability < 25).WithLimit(50), ct);

if (live.Items.Count > 0)
{
    var record = live.Items[0];
    var value = record.Value; // A UI can retain this same root instance.
    live.Conflict += conflict => ShowUnsavedEditWarning(conflict.LocalChangedFields);
    await live.RefreshAsync(ct);
    value.Durability = 100;
    await record.SaveAsync(ct);
}
```

Use mutable reference DTOs with writable persisted fields/properties. Ignored
members are left alone, and missing persisted fields are reset from the complete
deserialized server record. Unsupported immutable/value types and DTOs with
`JsonExtensionData` fail before I/O.
The managed query accepts the existing realtime filters and limit; projections
and expansions are rejected because items must be complete editable records.
The existing `SubscribeAsync` / `ISharedCollection<T>` remains a snapshot API.

## State and conflicts

The collection transport determines membership. New or changed rows are read
through the existing canonical REST path, with at most four reads in flight per
collection. Initial opening and explicit `RefreshAsync` wait for synchronization.
Changed rows use each handle's operation queue alongside Save/Reload/Delete.
The SDK rejects incomplete/unversioned reads, ignores older per-record versions,
and coalesces pending snapshots while active synchronization makes progress.
Reset/reconnect invalidate earlier reads even within the same epoch, and disposal
prevents late results from being applied. Governed frame
epoch/sequence validation stays in the shared transport registry. Legacy
`updated_at` has the platform's documented clock/millisecond ordering limits;
it is not an authority revision and is never compared across records.

Realtime uses the same **backend-wins** policy as an individual record subscription.
A changed server record replaces persisted local edits in place; `Conflict`
carries the pre-overwrite local snapshot and changed field names. Unchanged and
stale refreshes preserve edits. `RecordChanged` follows conflict notification;
`Changed` observes the synchronized collection. Accepted per-record changes and
conflicts are reported even if another row's read fails. This is a coalesced current-state
view, not a lossless event history or an atomic multi-record read. Read failures
raise `SynchronizationError` / `Failure` and retain the last usable membership.
Writes retain normal ACL, acting-player attribution and concurrency checks.
See [split saves](split-saves.md) for partial-write recovery and nested-write limits.

## Membership and lifetime

`Added` announces a new member. `Removed` carries a record and a reason:

- `LeftCollection`: the ID disappeared from the set. This includes filter exits;
  the collection wire does not provide the reason. The SDK does not set `IsDeleted`.
- `Deleted`: that handle has separate confirmation of deletion, such as its
  successful `DeleteAsync` or a directly addressed record subscription's deletion
  notification. An absent/invisible REST row is not treated as confirmation by
  the collection. A collection termination is a subscription-level event.

Membership releases its reference when a row leaves. Re-entry creates a new
member; previously retained handles do not silently reattach. `Dispose` and
`CloseAsync` stop collection work, detach callbacks and release its transport
lease and members. A non-deleted handle retained by the caller remains usable
under its existing authorization context. Disposing the collection does not
cancel an independently dispatched record write. Explicit logout/reconfiguration
closes the subscription; natural reconnect uses the registry's recovery path.

The Game Server companion exposes the same API through
`PlayServGameServer.Records<T>()` after its Records realtime connection is open.
