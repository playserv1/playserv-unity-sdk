# PlayServ Schema Tool

External schema analysis and generation companion for `com.playserv.sdk`.
Unity downloads the tool through UPM, but runs it as a separate process on the
Unity-bundled .NET runtime.

The shipped tool supports Unity 2021.3 through Unity 6.6 on macOS, Windows, and
Linux. It targets .NET 5, used by Unity 2021.3 on Linux, and permits runtime
roll-forward to the .NET 6 or .NET 8 hosts bundled with newer editors. No system-wide .NET
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
Roslyn runtime dependencies that are newer than the Unity 2021.3 Linux base
framework.
