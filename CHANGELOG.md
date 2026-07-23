# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning.

## [Unreleased]

### Added

- Added `PlayServExternalIdentityProof` as an explicitly unverified Apple/Google
  credential handoff for the future PlayServ authentication backend.
- Added assembly-owned runtime module, legacy facade, and local execution
  registration discovered without generated package registries.
- Added the project-owned `Playserv.Project.Generated` assembly under
  `Assets/PlayServ/Generated/Runtime` for the selected runtime module set.
- Added project-scoped module configuration in
  `ProjectSettings/PlayServModules.json`, including SDK profile, runtime modules,
  editor tools, and per-build-target overrides.
- Added automatic module graph synchronization when the project settings file
  changes or Unity switches build target.

### Changed

- Runtime module and editor-tool selection now migrates from `EditorPrefs` to
  version-controlled project settings. Module selection no longer depends on
  machine-local preferences.
- Optional module asmdefs are excluded by their disable define constraints,
  while `Playserv.Runtime.asmdef` and all other package source remain immutable.
- Declared the built-in IMGUI and Unity Web Request modules required by editor,
  sample, and runtime HTTP code.
- Included the `Examples` assembly in registry package contents so bundled
  samples do not reference an omitted assembly.
- Included `package.json.meta` in registry package contents so Unity can import
  read-only package installations without attempting to modify the package.
- The stable `Playserv.Wrapper.PlayServ` surface now acts only as a legacy facade
  over compatibility providers owned by enabled module assemblies.
- Module validation and repair now include the project module settings schema,
  profiles, module IDs, platform overrides, and assembly registrations.

### Removed

- Removed project-specific compatibility, module registry, and manifest
  generation from the SDK package directory.

### Security

- Removed Apple Team ID, Services ID, Key ID, redirect URI, and private key from
  client-side `PlayServAppleSignInSettings`.
- Added an editor migration that re-serializes legacy Apple settings assets so
  removed server credential fields do not remain in project YAML.
- Documented that provider profile data and tokens must not create a trusted
  PlayServ session before backend verification.

## [0.1.0] - 2026-03-04

### Added

- Initial `com.playserv.sdk` package structure with Runtime, Editor, and Samples content.
- Deployment tooling, RPC analyzer integration, and version synchronization editor workflows.
