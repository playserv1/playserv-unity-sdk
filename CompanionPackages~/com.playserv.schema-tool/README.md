# PlayServ Schema Tool

Normal Unity Player/Server builds now run offline contract validation automatically
when the target contains `[PlayServSchema]` contracts (PSV-3729). Install this
companion at the same revision as core. Validation needs neither initialization
nor generated schemas, credentials or network access; it never writes schemas or
lock files. The existing `validate` command continues checking generated output
freshness; the build pipeline uses `validate-contracts --build-inputs <json>` with
the target's sources, references and scripting symbols instead.

See core `Documentation~/playserv-sdk.md`, **Automatic build validation**, for the
supported rules, source diagnostics, headless entry point and troubleshooting.
The CI matrix runs the shipped tool and integration on Unity 2022.3.62f3 and 6000.6.0f1.

External schema analysis and generation companion for `com.playserv.sdk`.
Unity downloads the tool through UPM, but runs it as a separate process on the
Unity-bundled .NET runtime.

The shipped tool supports Unity 2022.3.62f3 and Unity 6 on macOS, Windows, and
Linux. Its .NET 5 target permits runtime roll-forward to the .NET hosts bundled
with supported editors. No system-wide .NET
installation is required to use the published tool.

The package provides:

- `init`, `status`, `analyze`, `generate`, `validate`, `sync`, `push`, `watch`,
  and `doctor` commands;
- Roslyn-based discovery of `[PlayServSchema]` contracts;
- deterministic JSON Schema and lock files;
- optional backend C# contract generation;
- a stable project launcher for Rider and other IDEs.

Open `Tools > PlayServ > Settings` and use the `Schema Tool` section to install,
initialize, generate, or start the watcher.

See the main SDK documentation for configuration and CLI examples.

## Push code-first schemas

`push` analyzes all attributed C# contracts, exchanges an `sk_*` server key
for a short-lived CLI session, reads the current whole-schema revision, then
sends one atomic `schema:push-from-code` bundle. A stale revision or invalid
element leaves the remote schema unchanged.

Configure `service.endpoint`, `service.environment`, and `service.projectId`
in `playserv.schema.json`, then supply the credential only through the process
environment:

```bash
export PLAYSERV_SERVER_KEY='sk_...'
.playserv/bin/playserv-schema push
```

Use `push --dry-run` to validate the exact bundle without reading a credential
or making a network request. `--endpoint` can override the configured endpoint;
`PLAYSERV_API_URL` is the final fallback. A custom secret variable name can be
set with `service.serverKeyEnvironmentVariable`. Server keys are never accepted
in command-line arguments, config files, generated output, or logs.

## Source build

Published runtime files are committed under `Tools~/SchemaTool/runtime` so the
package works without a system-wide .NET installation or network restore.

To rebuild them, install a .NET SDK with the .NET 5 targeting pack,
then run:

```bash
Tools~/SchemaTool/build.sh
```

Set `DOTNET` to an explicit SDK executable when necessary. The build restores
the pinned portable Roslyn packages and replaces the published runtime folder
as a complete set, including its dependency manifest. Consuming projects use
these committed files and do not perform a package restore.

The resulting framework-dependent assembly is launched with the .NET runtime
bundled in the active Unity editor. The published folder also carries the
required Roslyn runtime dependencies, independent of the bundled framework.

### Typed record relations (0.6.3)

Code-first fields can use `PlayServRecordRef<Entity>` and arrays/lists of that type,
including nullable collections. Schema push emits a relation with the discovered
entity target and one/many cardinality. JSON schema describes ID strings; the
optional C# contracts target qualifies reference types. Existing ID DTO generation
is unchanged. See [typed record references](../../Documentation~/record-references.md).
