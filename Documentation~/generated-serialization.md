# Generated serialization metadata

`[PlayServGeneratedSerialization]` opts a DTO into Editor-generated registration,
field names and typed constructor/getter/setter delegates. `NewtonsoftJsonCodec`
uses registered contracts automatically. Newtonsoft still performs JSON parsing,
value conversion, collection handling and custom conversion; this is not a
reflection-free serializer.

```csharp
using Playserv.Serialization;

[PlayServGeneratedSerialization]
public sealed class Inventory
{
    [PlayServJsonName("display_name")]
    public string Name;
    public int Count { get; set; }
}
```

Run **Tools > PlayServ > Generate Serialization Metadata**, wait for compilation,
then enter Play Mode or build. The generated sources and linker descriptions live
under `Assets/PlayServGenerated/Serialization/`; first-pass registrations live
under `Assets/Plugins/PlayServGenerated/Serialization/`. Assembly references place
registrations in their DTOs' assemblies. Commit these generated files and their
Unity metadata with the contracts. Generation is deterministic and unchanged files
are not rewritten, including LF/CRLF-only checkout differences. Only files owned by this generator are removed when contracts
are removed or renamed.

For CI, run a separate Unity process with
`-executeMethod Playserv.Editor.PlayServSerializationGenerator.Generate`, then run
the build/test process after compilation. Both processes must select the same
build target. A custom build script can call `GenerateForBuild(BuildPlayerOptions)`
with the exact build options, including `extraScriptingDefines`, as its preparation
step. Generation requires no platform credentials, network calls or Schema Tool.

Editor and Player descriptors are generated from their respective compilation
contexts and separated with `#if UNITY_EDITOR`. Player-only fields/types do not
produce invalid Editor references. Build and Play Mode checks reject missing or
stale output before it can silently omit a newly added field. Regenerate when
changing types, build target, conditional defines or SDK generator version.
Existing contract build validation and schema workflows continue independently.
Projects without opt-in attributes skip semantic compilation for this generator;
the empty expected output still detects stale files left after removing an opt-in.

## Supported contracts

- Non-derived, non-generic mutable DTO classes accessible from their own assembly,
  with a public parameterless constructor; accessible nested classes work too.
- Public writable fields and public get/set properties, scalar values, nullable
  values, nested objects, arrays, lists, dictionaries and record references.
  Mark each nested DTO separately to generate its contract too; unmarked nested
  types use the existing resolver.
- `PlayServJsonName`, `PlayServField` names and `JsonProperty` names, in that order
  of precedence, matching the existing codec. Generated field order is ordinal by
  wire name; JSON object member order is not part of the contract.
- `PlayServIgnore`, `JsonIgnore`, `NonSerialized` and `IgnoreDataMember`; ignored
  incoming fields stay ignored even with `IgnoreMissingMembers = false`.
- Existing `JsonCodecOptions`, including null handling and custom converters.
  Typed Records still supplies its context-bound `PlayServRecordRef<T>` converter:
  references write IDs and read IDs, nulls or expanded previews as before.

## Fallback and lifecycle

Unsupported opted-in types produce a generation diagnostic and use the complete
existing cached reflection contract. The generator never emits a supported subset
of a type. Examples include inherited DTOs, private attributed members,
constructor-based immutable types, read-only properties, callbacks, `ShouldSerialize`
or `Specified` conventions, extension data, collection/dynamic contract interfaces,
type converters, `DefaultValue`, explicit compiler-generated members,
case-insensitive name collisions and advanced
Newtonsoft attributes or property options. Unregistered types also retain the
default reflection behavior.

Generated `link.xml` preserves opted-in DTOs and their reachable application data
types, including fallback members. Types reached only dynamically through `object`
or external converters need the same explicit preservation as before. This
mechanism does not discover arbitrary runtime types.

Registration runs on Editor reload and player subsystem initialization. Repeating
registration replaces the complete descriptor atomically, including when Domain
Reload is disabled. Resolving a type through fallback before registration does not
prevent later registration from taking effect. `IsRegistered(typeof(T))` reports
whether metadata is available; runtime tests additionally exercise the generated
accessors to detect accidental fallback.

For a controlled comparison or troubleshooting, `new NewtonsoftJsonCodec(false)`
uses the existing shared, cached reflection resolver. The parameterless constructor
enables registered descriptors. Both paths retain strict response validation.

## Verification and measurement

The source repository's `unity/tools/webgl-smoke/` fixture builds and runs an IL2CPP
High-stripping player. It checks generated registration, round trips, strict
validation, private-member fallback and the existing Records reference HTTP flow.
The browser test requires a successful serialization report.

`SerializationSmoke.VerifyAndMeasure()` reports the first serialize/deserialize
of its fixture type and 3,000 warmed serialize/deserialize pairs for each resolver,
with identical payloads and default options. The generated registration has already
run at startup, so first-use timing does not include registration. The warmed test
includes Newtonsoft parsing, conversion and string allocation. Runtime/platform
and Unity version accompany the report. A runtime without a working allocation
counter reports `allocatedBytes: null`, never a misleading zero.

The repository's `unity/tools/serialization-benchmark/` harness runs the actual
compiled SDK and generated smoke descriptors on .NET, whose current-thread
allocation counter is checked. Run both execution orders in fresh processes;
first-use timing includes shared serializer/JIT startup and is order-sensitive.
The harness README records the environment and observed comparison. .NET
allocation results are not IL2CPP allocation claims. Timing is observational,
not a pass/fail threshold or promised speedup.
