# PlayServ Schema Tool

External schema analysis and generation companion for `com.playserv.sdk`.
Unity downloads the tool through UPM, but runs it as a separate process on the
Unity-bundled .NET runtime.

The package provides:

- `init`, `status`, `analyze`, `generate`, `validate`, `sync`, `watch`, and
  `doctor` commands;
- Roslyn-based discovery of `[PlayServSchema]` contracts;
- deterministic JSON Schema and lock files;
- optional backend C# contract generation;
- a stable project launcher for Rider and other IDEs.

Open `Tools > PlayServ > Settings` and use the `Schema Tool` section to install,
initialize, generate, or start the watcher.

See the main SDK documentation for configuration and CLI examples.

## Source build

Published runtime files are committed under `Tools~/SchemaTool/runtime` so the
package works without a system-wide .NET installation or network restore.

To rebuild them, install a .NET 6 SDK or newer with the .NET 6 targeting pack,
then run:

```bash
Tools~/SchemaTool/build.sh
```

Set `DOTNET` to an explicit SDK executable when necessary. The resulting
framework-dependent assembly is launched with the .NET runtime bundled in the
active Unity editor.
