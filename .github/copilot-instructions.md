# Copilot Instructions for Jaravi

## Build, test, and lint commands

- Build full solution:
  - `dotnet build jaravi.sln`
- Run full test suite:
  - `dotnet test`
- Run project-level tests:
  - `dotnet test Jaravi.Engine.Tests\Jaravi.Engine.Tests.csproj`
  - `dotnet test Jaravi.McpServer.Tests\Jaravi.McpServer.Tests.csproj`
- Run a single test class:
  - `dotnet test Jaravi.Engine.Tests\Jaravi.Engine.Tests.csproj --filter "FullyQualifiedName~SessionManagerTests"`
- Run a single test method:
  - `dotnet test Jaravi.Engine.Tests\Jaravi.Engine.Tests.csproj --filter "FullyQualifiedName~SessionManagerTests.Spawn_await_summary_happy_path"`
- Run local hosts:
  - `dotnet run --project Jaravi.McpServer`
  - `dotnet run --project Jaravi.Dashboard`
- Package global tool:
  - `dotnet pack Jaravi.McpServer -c Release -o nupkg`

There is no dedicated lint command in this repo; use `dotnet build` and tests as the quality gate.

## High-level architecture

Jaravi follows a layered architecture with strict dependency direction:

- `Jaravi.Core`: pure shared contracts (models, events, exceptions, JSON options). No runtime dependencies on higher layers.
- `Jaravi.Engine`: deterministic orchestration runtime (session lifecycle, process execution, queueing, claim collision handling, scope gate, bounded log storage, watchdog/stuck detection, event bus).
- `Jaravi.McpServer`: host surface for MCP + REST + WebSocket telemetry. It composes the engine, exposes MCP tools/resources/prompts, and serves the embedded Control Center UI.
- `Jaravi.Dashboard`: WPF observer/remote client. It does not reference engine internals; it consumes `/api/*` and `/ws/events`.

Runtime flow:

1. Client calls MCP tool/REST endpoint (`Jaravi.McpServer`).
2. `SessionManager` (`Jaravi.Engine`) admits/queues/spawns subprocess sessions and supervises them.
3. Logs are captured into `RingBufferLogStore`; telemetry is published through `ChannelEventBus`.
4. MCP responses stay compact/bounded; full live telemetry goes to observers over WebSocket.

## Key repository conventions

- **Keep `Jaravi.Core` framework-agnostic**: new domain models/events/contracts belong in Core; transport- or host-specific code belongs in McpServer/Dashboard.
- **Single JSON dialect everywhere**: use `JaraviJson.Options` for serialization/deserialization across REST, WebSocket events, resources, and clients.
- **Map domain errors explicitly**:
  - MCP surface uses `McpGuard` to translate `JaraviException` into `McpException`.
  - REST surface maps domain exceptions to HTTP problem responses in middleware.
- **Preserve context-collapse guarantees**: output returned to agents/users must remain bounded (`MaxReadLines`, ring buffer, capped tails). Avoid new unbounded log/result paths.
- **Stdout/stderr contract is strict**: in stdio mode, stdout is reserved for JSON-RPC frames; logs belong on stderr.
- **Agent integrations are declarative**: add/update profiles in `Jaravi.McpServer\agents.json` and reload with `reload_agents`; do not hardcode new CLI behavior in engine code unless required by all profiles.
- **One-shot CLI profiles should close stdin**: for CLIs that block waiting for EOF, use `closeStdin: true` and prefer direct binary/node entrypoint over `.cmd` shim wrappers that can corrupt multiline prompts.
- **Prefer structured delegation input**: `SpawnRequest.Brief`/`TaskBrief` is the primary contract; free-text `task` is fallback.
- **Claims and queueing are opt-in safety rails**: for potentially overlapping write scopes, pass `claims` and explicit `onConflict` (`queue` or `reject`) instead of ad-hoc collision handling.

## Existing AI-assistant config in this repo

- Claude skill: `.claude\skills\jaravi-orchestrator\SKILL.md`
  - Encodes the “boss agent” orchestration workflow and tool-usage doctrine.
- MCP client configs:
  - `.mcp.json` (Claude Code) and `opencode.jsonc` (OpenCode) both register `jaravi-mcp --stdio`.
  - Keep these aligned when changing server startup expectations.
