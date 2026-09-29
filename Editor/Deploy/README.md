# PlayServ Editor Deployment

This document describes the Unity Editor tooling under `Editor/Deploy`.

## Scope

- Platform Functions deployment (`cloud_function` and `game_server`) through the v1 API
- Server Images: build and publish a Docker image to the project registry

## Server Images

Deployment captures log lines and result panels at the start of each IMGUI layout
cycle, so background output cannot change the controls during repaint. Changing
the target still invalidates operations immediately; a displayed older result
cannot authorize a new publish or deploy.

Open **Tools → PlayServ → Settings → Deployment → Server Images**. This Editor-only
workflow builds an existing server Dockerfile and publishes an image using the
current server-image REST contract. Studios need no checkout of the platform repository.

1. In **PlayServ Config**, select Dev or Prod, set **Dashboard Address** and enter
   **Server Token (Dev/Prod)** beneath Client Token. Then **Connect / refresh
   servers**. Both Server Images and Platform Functions use these same settings.
   The operator session comes from `POST /api/v1/auth/cli`; review its project and
   environment. A token for a different selected environment is rejected.
2. Select an existing **game_server**, or click **Create game server** and enter
   its display **Name** and **Slug**. Slugs have 3–50 lowercase letters, digits
   or single hyphens, starting with a letter and ending with a letter or digit
   so the slug also works as an image repository name. Review the project/environment,
   then **Create**. The Editor refreshes the operator session and requires the
   same scope before `POST /api/v1/functions` with `kind=game_server`,
   `runtime=dotnet10`, `hosting_mode=multi-room` (the C# CLI declaration defaults).
   This only registers the server; it does not deploy code or start a machine.
   All pages of the listing are loaded; the created server is selected and any
   previously prepared image is invalidated. An existing slug is never modified.
3. Choose the **build context** folder and a **Dockerfile inside it**. Keep the
   server in your game's repository, outside Unity `Assets`, with its own SDK
   dependencies and `.dockerignore`. The Dockerfile is responsible for compiling
   the server. Use an exec-form `ENTRYPOINT` so platform launch arguments reach it.
4. Enter a unique **image tag**, preferably the reviewed commit SHA. Click
   **Check Docker**, then **Build image**. Install Docker separately and use a
   local Linux daemon (Docker Desktop on macOS/Windows or Docker Engine on Linux).
   The CLI is found on the Editor's PATH or in standard macOS Docker locations.
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

Credential expiration preserves the API's UTC or explicit time-zone offset and
is checked against UTC, independently of the Editor's regional date format.
Malformed expiration data and expired credentials are reported separately before
Docker login or push. If the credential has expired, **Publish image** requests a
fresh one for the already built image; rebuilding is unnecessary. An uncertain
push still requires **Check publication**, not another publication attempt.

Budgets are 30 seconds per HTTP request, 30 minutes each for build and push, and
60 seconds for publication verification. Polling respects `Retry-After`.
Creation is never automatically retried. On a conflict the server list is refreshed;
on a timeout or lost response, use **Connect / refresh servers** before trying again.
If creation succeeded but listing failed, refresh the list instead of creating again.
Cancelling creation locally cannot delete a registration already accepted by PlayServ.
**Cancel**, window disposal and domain reload stop local work, including Docker
child processes. They cannot undo a push already accepted by the registry.
A push is never automatically repeated after a lost response. **Check publication**
uses the saved local publication reference after cancellation or reopening the
Editor. If the manifest digest was lost, finding the tag is reported as unverified;
inspect the registry/build evidence before deciding to publish another tag.

Docker is resolved from the Editor's PATH and, on macOS, standard Docker Desktop,
Homebrew, `/usr/local/bin` and `~/.docker/bin` locations. The resolved absolute
executable is used without changing the Editor's PATH or launching a shell. Its
directory is added only to the child process's PATH so credential helpers also work.
Diagnostics distinguish
a missing CLI, an executable that cannot start, an unavailable daemon and a non-Linux daemon.

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
Tabs are ordered **Platform Functions → Server Images**, with Platform Functions selected initially.

1. Use **Dashboard Address** in the current PlayServ Config. Dev defaults to
   `https://dashboard.dev.playserv.com`; Prod to `https://dashboard.playserv.com`.
   Known old `.io` dashboard defaults migrate automatically; custom URLs are preserved.
2. Enter **Server Token (Dev/Prod)** below Client Token, in Settings or the Config
   Inspector. Like Client Token it is visible text, with automatic local saving
   on edit; empty the field to clear it. There are no Save/Clear buttons.
   Local Editor preferences isolate it by Unity project, config GUID
   and environment. `PLAYSERV_API_KEY` is shown read-only, takes precedence and is never copied
   into local storage. Server tokens are never serialized into assets or player builds.
   Editor preferences are local storage, not an encrypted secret vault.
3. Connect to resolve the **project** and **environment** from `/api/v1/auth/cli`.
   The returned environment must match the selected Dev/Prod environment.
   Changing the config, address, environment or saved key cancels local work,
   invalidates the connection and prepared image, and requires another Connect.
   Late responses cannot restore the old session. An old Deployment key migrates
   once according to its saved standard API address; unknown addresses preserve
   the old record and show a request to re-enter the appropriate token.
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

## Retired V1 workflows

The RPC deployment tab, ZIP upload, Sync Version, remote code hash/archive and
by-sdk-key schema download are retired in 0.6.10 (PSV-2887). Use Platform Functions
for platform source and Server Images for an existing Dockerfile. Gameplay RPC is
unchanged. Deploy API and Schema API values from older config assets are ignored.

Runtime Connect uses the configured `GameVersion`. Public `GetLatestVersionAsync`
entry points remain `[Obsolete]` for source compatibility and fail with
`NotSupportedException` before any HTTP request. The debug terminal has no
`sdk latest` command. Import local schema JSON through Schema Workflows, or use
Schema Tool to generate local output and push the current code-first API.
