# Prepare the first binary runtime upgrade

The binary package keeps the SDK's public API and runtime assembly names. Editor
tools and companion packages remain source. The first upgrade from a source
package must run while the old SDK still compiles. Clean installations and later
binary-to-binary updates do not need this preparation.

Use Unity **2022.3.62f3 or newer**. If the project still uses Unity 2021, first
upgrade its Editor while retaining the existing SDK, resolve compilation errors,
and then prepare the package upgrade below.

1. Save your project and commit or back up its current state. Finish any active
   Package Manager operation and leave Play Mode.
2. Extract the validated binary package into a permanent directory outside
   `Assets`, `Packages`, `Library` and `Temp`. The tool installs local UPM paths
   pointing there, so keep that directory after the upgrade.
3. In the old project, import the extracted package's
   `Upgrade~/PrepareBinaryUpgrade.unitypackage` using **Assets → Import Package →
   Custom Package**. This imports only the independent preparation scripts.
4. Choose **Tools → PlayServ → Prepare binary upgrade** and select the extracted
   package directory. The tool validates the DLL checksums, backs up project
   files, migrates their references and installs core plus already-installed
   companions. Wait for Package Manager and script recompilation to finish.
5. Open your existing configuration, scenes and prefabs. Their values and
   components should remain intact. Commit the migrated serialized files,
   assembly definitions and package manifest/lock file together. Remove the
   temporary `Assets/PlayServBinaryUpgrade` preparation scripts when finished.

The tool covers configuration assets, scenes, prefabs, animations/controllers
and assembly definitions in `Assets` and writable embedded/local packages. It
preserves unrelated references and captures existing automatic third-party DLL
references when converting test assemblies. It converts binary serialized assets
to text with Unity before replacing their script references.

Embedded PlayServ core or companion packages must first be moved to source local,
Git or registry installations: an embedded copy takes precedence over a new
manifest dependency. All registered PlayServ companions, including indirect
dependencies, are replaced together.

A read-only registry/Git package that still references source SDK assemblies must
first be updated by its author or embedded. An `.asmref` extending an SDK runtime
assembly must first become a studio-owned assembly definition; a compiled SDK
assembly cannot be extended with project source. These cases stop preparation
before any package or asset is changed. Legacy SDK copies under `Assets` must
first be moved to a source UPM installation; installing a second copy beside them
would produce duplicate types.

## Recovery

Original file bytes and a `restore.json` journal are saved in
`Library/PlayServ/BinaryUpgrade/<timestamp-id>/`, including files in local packages
outside the project. Package Manager failures restore those original files. Do
not delete `Library` while you still need this backup.

Restoration leaves unchanged files untouched and attempts the remaining files if
one cannot be written. An incomplete restore reports the retained backup path;
restore those files before continuing. The tool releases its Editor reload lock
even when restoration fails, and reports success only after saving the completion
marker.

If the Editor is interrupted, keep the package directory and backup. Reopen the
project and use **Tools → PlayServ → Restore binary upgrade backup** with the
transaction's `restore.json`. If compilation prevents that menu from loading,
close Unity and restore the recorded original files (or your version-control
backup), including `Packages/manifest.json` and `packages-lock.json`, before
reopening. A successful transaction has a `completed.txt` marker.

Once the binary release is available in your chosen registry or Git tag, you may
switch the local paths to that **same** release. Keep core and installed companion
versions aligned. Do not replace existing release tags with different contents.
