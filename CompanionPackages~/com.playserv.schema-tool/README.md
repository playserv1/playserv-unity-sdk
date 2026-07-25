# PlayServ Schema Tool

External schema analysis and generation companion for `com.playserv.sdk`.
Unity downloads the tool through UPM, but runs it as a separate process on the
Unity-bundled .NET runtime.

The shipped tool supports Unity 2021.3 through Unity 6.6 on macOS, Windows, and
Linux. It targets .NET 6, used by older supported editors, and permits runtime
roll-forward to the .NET 8 host bundled with Unity 6.x. No system-wide .NET
installation is required to use the published tool.

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

Set `DOTNET` to an explicit SDK executable when necessary. When building with a
newer SDK, set `PLAYSERV_ROSLYN_PATH` to a Roslyn directory compatible with the
.NET 6 target, for example Unity 2021.3's `DotNetSdkRoslyn` directory. This is a
maintainer-only build input; consuming projects use the Roslyn assemblies and
tool binary already published in the package.

The resulting framework-dependent assembly is launched with the .NET runtime
bundled in the active Unity editor.
