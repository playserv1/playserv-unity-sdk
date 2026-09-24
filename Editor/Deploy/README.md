# PlayServ Editor Deployment, Analyzer, and Versioning

This document describes the Unity Editor tooling under `Editor/Deploy`.

## Scope

- Deployment upload (`Deploy Now`)
- RPC code analyzer used before deployment
- Version synchronization (`Sync Version`)
- Platform Functions deployment (`cloud_function` and `game_server`) through the v1 API
- Server Images: build and publish a Docker image to the project registry

## Server Images (0.6.5)

Open **Tools → PlayServ → Settings → Deployment → Server Images**. This Editor-only
workflow builds an existing server Dockerfile and publishes an image using the
current server-image REST contract. Studios need no checkout of the platform repository.

1. Set the **Platform API** origin and server `sk_` key, then **Connect / refresh
   servers**. Credentials use the same project-local Editor preferences and
   `PLAYSERV_API_KEY` precedence as Platform Functions. The operator session comes
   from `POST /api/v1/auth/cli`; review the displayed project and environment.
2. Select an existing **game_server**. All pages of the server listing are loaded.
   Registration happens in PlayServ; this tool does not create a server or pool.
3. Choose the **build context** folder and a **Dockerfile inside it**. Keep the
   server in your game's repository, outside Unity `Assets`, with its own SDK
   dependencies and `.dockerignore`. The Dockerfile is responsible for compiling
   the server. Use an exec-form `ENTRYPOINT` so platform launch arguments reach it.
4. Enter a unique **image tag**, preferably the reviewed commit SHA. Click
   **Check Docker**, then **Build image**. Install Docker separately and use a
   local Linux daemon (Docker Desktop on macOS/Windows or Docker Engine on Linux).
   The CLI must be on the Editor's PATH; restart the Editor after changing PATH.
   BuildKit with `--load` / `--provenance=false` is required. On Apple Silicon,
   Docker must provide amd64 emulation when the Dockerfile executes amd64 code.
5. Review the project, environment, server slug, tag, `linux/amd64` architecture
   and immutable local image ID. Click **Publish image** to publish that exact
   image. Editing any build/target field invalidates the prepared build.
6. Success means `GET /api/v1/server-images` reports that tag with the push's
   **manifest digest** and architecture `amd64` or `multi`. Local image IDs are
   config digests; they are not compared with registry manifest digests.

The Editor refreshes the operator session before publishing and refuses a change
of project/environment. It checks for an existing tag and refuses to overwrite
one; this is a preflight, not an atomic lock against another publisher. Images
are project-wide even though requests also require an environment selector.

After building, `POST /api/v1/server-images:credentials` supplies the registry,
repository and a one-hour credential. Docker pushes directly to that repository:
there is no Docker-archive upload endpoint. The password goes through stdin,
with an isolated temporary Docker config deleted on success, failure or Cancel.
The user's Docker authentication is preserved. PlayServ environment credentials
are removed from child processes and known secrets are redacted from the bounded
Editor log. No credential is added to Unity assets or player builds. Docker may
retain ordinary build cache and local image tags.

Budgets are 30 seconds per HTTP request, 30 minutes each for build and push, and
60 seconds for publication verification. Polling respects `Retry-After`.
**Cancel**, window disposal and domain reload stop local work, including Docker
child processes. They cannot undo a push already accepted by the registry.
A push is never automatically repeated after a lost response. **Check publication**
uses the saved local publication reference after cancellation or reopening the
Editor. If the manifest digest was lost, finding the tag is reported as unverified;
inspect the registry/build evidence before deciding to publish another tag.

Common failures: unavailable Docker or Linux daemon, an unsupported build platform,
existing tag, changed scope, registry not configured (`503 image_registry_not_configured`),
expired credentials, incompatible architecture, or a digest mismatch. Correct the
reported condition and use **Check publication** for an uncertain push.

This feature does not move legacy sample code into a game repository, create a
Dockerfile, configure a pool, change `image_version`, restart machines, or replace
Cloud Run deployments. Selecting a pool version and two-browser/WSS acceptance
remain explicit release steps. C# server SDK and game runtime APIs are unchanged.

## Platform Functions

Open `Tools/PlayServ/Settings`, expand **Deployment**, and select **Platform Functions**.
The **RPC** mode retains the ZIP, analyzer and version-sync workflow described below.

1. Set **Platform API** to the platform API origin used by your CI workflow. This is
   separate from the legacy RPC deployment endpoint.
2. Supply a server API key locally in the Editor or through `PLAYSERV_API_KEY`.
   The environment variable takes precedence. Local storage is project-scoped
   Editor preferences, not an encrypted secret vault; keys never belong in
   `PlayServConfig`, Assets, source control or a player build.
3. Connect to resolve the **project** and **environment** from `/api/v1/auth/cli`.
   Changing the API or key invalidates the previous connection. A public client
   key cannot create this operator session.
4. Select the function's source folder, including folders outside `Assets`.
   The starting location is `<UnityProject>/functions`. The folder name supplies
   the initial editable slug. C# declarations implementing `IPlatformFunction`
   suggest `cloud_function`; those inheriting `PlatformGameServer` suggest
   `game_server`. Choose the type explicitly when inference is ambiguous.
5. Preview the files and verify the API, project, environment, slug and type, then
   deploy. Each operation publishes one function.

The Editor builds a managed `tar.gz`, retaining relative paths and supporting
files such as `platform.json`. It excludes `bin`, `obj`, `.git` and Unity `.meta`
files, rejects symbolic links, and requires a fresh preview when source files
change. No PlayServ CLI or system `tar` installation is required. This path does
not apply the RPC dependency-closure analyzer to platform source files.
Select a self-contained source folder: the Editor packages that folder exactly,
as a folder-based CI tar step does. It does not vendor `ProjectReference` sources
located outside the selected folder as the standalone CLI can.

The upload is a multipart `POST /api/v1/platform-functions/{slug}/deployments`
with `source`, `kind`, `language=csharp`, and `target_env`, authenticated with
the exchanged session and its `X-Project-Slug` / `X-Env` selectors. The server
remains responsible for validating and compiling the source bundle.

An accepted upload is not a completed deployment. The Editor polls
`GET /api/v1/deployments/{id}` every six seconds for up to twenty minutes, and
reports success only for `deployed`. Failed deployments show the server's
message. An expired session can be renewed while polling only if its project
and environment remain unchanged.

Cancel stops local work, not a deployment already accepted by the server.
The accepted deployment ID is saved locally so status polling can be resumed
after cancellation, timeout or an Editor restart. An uncertain upload is never
automatically retried: check platform deployment history before sending another
package if the upload response was lost.

This mode publishes source and monitors deployment status. Existing game CI
remains usable; game-specific post-deployment steps (for example Eggie's
room-config cleanup) are not executed by the generic SDK tool.

Main files:

- `Editor/PlayServWindow.cs`
- `Editor/PlayServWindowTheme.cs`
- `Editor/PlayServClientProjectConfigBridge.cs`
- `Editor/Deploy/DeploymentApiClient.cs`
- `Editor/Deploy/DeploymentService.cs`
- `Editor/Deploy/DeploymentClosureFilter.cs`
- `Editor/Deploy/DeploymentZipBuilder.cs`
- `Editor/Deploy/DeploymentUploadAction.cs`
- `Editor/Deploy/VersionSyncAction.cs`
- `Editor/Deploy/Analysis/RpcCodeAnalyzer.cs`

## RPC Deployment Flow

1. Open `Tools/PlayServ/Settings`.
2. Expand `Deployment`.
3. Select folder + pattern.
4. Click `Deploy Now`.

Runtime flow:

1. `DeploymentClosureFilter.CollectDeployFiles(...)` collects `*.cs` files.
2. Files are analyzed by `FunctionAnalyzerService` (Roslyn-based analyzer).
3. Analyzer returns `FilesToCompile` (RPC classes + dependency closure).
4. `DeploymentZipBuilder` creates ZIP archive.
5. `DeploymentUploadAction` uploads ZIP with `X-Game-Id` header.

Upload endpoint behavior:

- Upload always targets `/api/deployments`.
- `DeployApiEndpoint` can be:
  - host only, for example `http://host`
  - API base, for example `http://host/api`
  - full deploy path, for example `http://host/api/deployments`

## Analyzer Flow

Analyzer entry point:

- `FunctionAnalyzerService.AnalyzeFiles(...)` in `RpcCodeAnalyzer.cs`

What analyzer does:

1. Parses source files with Roslyn.
2. Finds classes marked with `[Rpc]`.
3. Validates RPC classes (constructors, modifiers, methods).
4. Resolves dependent user types.
5. Validates dependency types/references.
6. Returns:
  - `Success`
  - `Errors`
  - `FilesToCompile`
  - RPC metadata

If analyzer fails, deployment/version sync is blocked and first errors are shown in the Console.

## Version Sync Flow

UI action:

- Click `Sync Version` in Deployment section.

Goal:

- Check if local analyzed RPC code matches backend RPC code hash.
- If it matches, update local `gameVersion` from backend latest version.

Steps:

1. Reuse analyzer-selected file list from `DeploymentClosureFilter`.
2. Call `GET /api/schemas/{gameId}/latest`.
3. `DeploymentZipBuilder` creates in-memory ZIP from local analyzed files.
4. `DeploymentZipBuilder` computes SHA-256 hash of ZIP bytes.
5. Call `GET /api/games/{gameId}/rpc-code/hash`.
6. Compare local hash with remote hash.
7. On match:
  - call `GET /api/games/{gameId}/version/latest`
  - write returned value into `PlayServConfig.gameVersion`
8. On mismatch:
  - call `GET /api/games/{gameId}/code-archive`
  - save archive to temp folder (`.../playserv-sync`)

Execution entry:

- `VersionSyncAction.ExecuteAsync(...)`

Important:

- Version sync does not upload deployment ZIP.
- It validates code parity and aligns version metadata only.

## Quick Analyzer Test

Use sample folders:

- `<imported PlayServ sample>/RPC/TestCode` (valid)
- `<imported PlayServ sample>/RPC/TestCodeWrong` (invalid)

To test:

1. In Deployment UI, choose one of these folders.
2. Click `Preview Files` or `Deploy Now`.
3. For `TestCodeWrong`, analyzer should report validation errors.
