# Saving parts and relations

Typed `CreateAsync`, record `SaveAsync`, singleton `SaveAsync`, and saves invoked
by `BulkSaveAsync` separate declared nested fields from ordinary inline fields.
The same implementation serves player clients and the Game Server companion.
Inline-only saves keep their single-request behavior. `BulkCreateAsync` remains
the separate atomic server operation; it is not expanded into client-side writes.

## Field mapping and requests

The runtime uses the contract attributes and serialized member names:

- A part contract (`Kind = Part`, or an attributed struct with `Kind = Auto`) is
  an inclusion. An explicit `PlayServField(Type = Inclusion)` also declares one.
- `PlayServRecordRef<T>` and explicitly declared relation ID fields are relations.
  Related records must already exist. Saving a parent never saves its children.
- Collections imply cardinality many; a dictionary part remains one object.
  `PlayServField.Cardinality` can declare one or many. `Ordered` relations send
  a position for each ID.
- Ordinary JSON objects, and fields explicitly declared `Json`, stay inline.
  Ignored members are not written. `PlayServJsonName` and `PlayServField` names
  follow the existing serializer.

Before writing, the SDK captures and validates the submitted field values. It
creates or patches the parent with inline fields, then replaces changed nested
fields in serialized-name order. An empty inline patch is skipped. An inclusion
uses `PUT …/includes/{field}` with an array (one object for cardinality one);
a relation uses `PUT …/links/{field}` with `{"child_ids":[…]}`. Singleton paths
use `…/singleton/…`. Clearing sends `[]` or `{"child_ids":[]}`; empty fields of
a new record need no nested request.

A split create first creates a parent with no parts or links. Its schema must
permit that initial state: declare nested fields optional when they are populated
after creation (for code-first contracts, `Required = PlayServRequiredMode.Optional`).
The backend can reject the inline create with `missing_required_field` when a
required nested field is absent. The SDK preserves that error and does not retry
or change the schema. Required-field validation and clearing rules remain the
backend's authority.

## Partial completion and retry

These requests are **not one transaction**. A failure stops subsequent writes;
accepted steps are retained. The SDK updates acknowledged snapshots and metadata
as steps succeed, and retains local edits made while requests are running.
`PlayServSplitSaveException<T>` carries the failed step/field, completed and
pending fields, original error, and a recovery `Record` or `Singleton` handle.
Bulk results retain this exception in the item's `Exception` property.

```csharp
try
{
    var created = await PlayServData.Records<Loadout>().CreateAsync(value, ct);
}
catch (PlayServSplitSaveException<Loadout> error)
{
    var record = error.Record;
    if (record == null)
        throw; // Parent creation did not return a usable identity; inspect IdempotencyKey.

    // Inspect the original failure and correct invalid values/access first.
    if (record.RequiresReconciliation)
        await record.ReconcileAsync(ct);
    await record.SaveAsync(ct); // Retains the ID; no second parent POST.
}
```

`ReconcileAsync` reads current server state while preserving local edits for an
explicit retry. `ReloadAsync` keeps its existing backend-wins behavior and drops
local edits. A network error does not trigger an automatic write retry. If parent
creation has an ambiguous result and no handle, the exception exposes its
`IdempotencyKey`; an explicit repeat must use the identical input and key in the
same authorization context and within the server's retention window.

## Concurrency, readback and cancellation

Parent patches retain `If-Match` and surface version conflicts. A nested-only
save checks a known parent version before writing. **Nested PUT routes do not
enforce `If-Match`**, so another writer can race after that check or between steps.
Use application-level coordination when multiple writers change nested fields.

Inclusion responses supply the parent's ETag. A final parent read reconciles the
complete state and version, including relation writes. If this read fails or
supplies no usable version, the handle requires reconciliation before another save
or delete. Recovery must return a version before the guard is removed. Read
permission is therefore needed for a fully reconciled split save; a read failure
can follow accepted writes.

The entire record save stays inside its existing operation queue. Canceling a
record-save caller stops its wait; an already dispatched save continues and updates
the handle. Creation and singleton saves use their existing cancellation tokens:
canceling cannot roll back accepted steps, and a partial failure can require
reconciliation. Inspect the returned failure and handle before starting another
write.
