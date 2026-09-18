# Typed record references

`PlayServRecordRef<T>` is an optional relation value for Records DTOs. Existing
string-ID DTOs, `PopulateAsync`, batch reads and generated models keep working.

```csharp
using Playserv.Data;
using Playserv.Schema;
using Playserv.Wrapper;

[PlayServSchema("inventory.item")]
public sealed class Item
{
    public string Name;
}

[PlayServSchema("inventory.loadout")]
public sealed class Loadout
{
    public PlayServRecordRef<Item> Primary;
    public PlayServRecordRef<Item>[] Items;
}

// Resolve the table by DTO name, or pass its entity ID to Records<T>(entityId).
var reference = PlayServData.Records<Item>().Reference("rec_item");
var record = await reference.LoadAsync(ct);
if (record != null)
{
    record.Value.Name = "Updated";
    await record.SaveAsync(ct); // saves this item, using its own ETag
}

var references = await PlayServData.Records<Item>().LoadReferencesAsync(
    new[] { "rec_first", "rec_second", "rec_first" }, ct: ct);
foreach (var item in references.Items)
{
    // Input order and duplicate entries are preserved; duplicate handles are independent.
    // Inspect IsSuccess/Error/Exception as with LoadManyAsync.
    var state = item.Value.State;
}
```

A constructor such as `new PlayServRecordRef<Item>("rec_item")` creates an
unbound ID-only value for assignment and writes. It cannot load until read back
through a Records context. Prefer `Records<Item>().Reference(id)` for a loadable
value. References embedded in a loaded DTO bind to that DTO's original Records
client and resolve the target table from the target type's name.

## Wire representation and expanded previews

Records serialization accepts a string ID or null for a single reference and an
array of IDs for collections. It always writes IDs, including after a reference
has loaded. Expanded response objects must contain `id`; `Value` exposes their
preview and `State` is `Expanded`. `Record` stays null until `LoadAsync` obtains a
canonical record with its own metadata and ETag. Expanded metadata never becomes
an editable record handle.

Loading a child or changing a child's value does not dirty its parent. Saving the
parent changes only its relation IDs and other edited parent fields; there is no
cascade save. Save an explicitly loaded child record separately. An expanded
preview can be incomplete, so load before editing it.

## Loading, errors and lifetime

States are `Unloaded`, `Expanded`, `Loading`, `Loaded`, `Missing` and `Faulted`.
`LoadAsync` caches a loaded or missing result; `ReloadAsync` fetches again.
Concurrent calls on one reference share I/O. Cancelling a caller's wait does not
cancel the shared request or another caller. HTTP transport timeouts still apply.

A record 404 returns null and `Missing`; ACL and network failures throw and remain
available in `Error` with `Faulted`. A batch uses the existing `LoadManyAsync`
ID-query path. Each item includes its reference, including a `Missing` reference
and the original per-item error when a record is absent or invisible.

References capture project/environment credentials, authorization subject and
player context. They keep the originating client/server/acting-player transport.
Changing configuration, logging out or replacing the managed player session makes
old references unusable, including cached values and responses arriving later.
Ordinary managed access-token refresh preserves them. Context is checked before
and after token resolution as well as after I/O. Obtain a new Records context
after an identity change. Custom token providers must update `PlayerId` or replace
the provider/configuration when changing identity; rotating a token for the same
player does not require a new context.

## Code-first schemas and player builds

Schema Tool recognizes `PlayServRecordRef<Entity>` and its collection forms as
relations with the target entity and one/many cardinality. The JSON schema uses
string IDs. Targets must be discovered non-singleton entity contracts; conflicting
explicit target/type/cardinality declarations are rejected. Existing model
generation continues to emit its established ID-based fields.

The SDK's reference constructor is preserved for IL2CPP. As with other Records
DTOs, retain your game DTO fields/constructors under stripping (for example with
Unity's `[Preserve]` or the game's `link.xml`). The source-repository WebGL fixture
exercises reference reads, canonical hydration, ID-only writes and batch loading
in an IL2CPP player with high stripping.
