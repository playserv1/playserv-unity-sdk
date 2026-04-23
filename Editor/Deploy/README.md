# PlayServ Editor Deployment, Analyzer, and Versioning

This document describes the Unity Editor tooling under `Assets/playserv-unity-sdk/Editor/Deploy`.

## Scope

- Deployment upload (`Deploy Now`)
- RPC code analyzer used before deployment
- Version synchronization (`Sync Version`)

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

## Deployment Flow

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

- `Assets/playserv-unity-sdk/Samples/RPC/TestCode` (valid)
- `Assets/playserv-unity-sdk/Samples/RPC/TestCodeWrong` (invalid)

To test:

1. In Deployment UI, choose one of these folders.
2. Click `Preview Files` or `Deploy Now`.
3. For `TestCodeWrong`, analyzer should report validation errors.
