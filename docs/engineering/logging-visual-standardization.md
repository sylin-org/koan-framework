# Logging visual standardization

## Contract

- **Scope**: Framework-emitted logs across Koan core, adapters, service orchestration, and hosting.
- **Inputs**: `AddKoanCore` logging defaults (Koan console formatter, Koan category filters), adapter lifecycle events, environment snapshots, and health probes.
- **Outputs**: Stage-tagged log lines, structured decision summaries, and consistent block formats for rich snapshots.
- **Failure modes**: Verbose debug chatter at default levels, mismatched formatting between modules, ASCII banners that break parsing, and missing cues for lifecycle transitions.
- **Success criteria**: Operators can follow startup and runtime stages at a glance, structured log sinks receive uniform payloads, and developers have ready-made samples for extending modules.

## Lifecycle map

| Stage tag  | Purpose                                                     | Typical emitters                                      | Default severity                                                   |
| ---------- | ----------------------------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------------------ |
| `[K:BLDG]` | Compiler and build diagnostics surfacing before host start. | `dotnet` build output, Roslyn diagnostics.            | `warning` / `error` as produced.                                   |
| `[K:BOOT]` | Module discovery and registrar execution.                   | `KoanAutoRegistrar`, module auto-loaders.             | `info` (success), `warn` on fallback.                              |
| `[K:CNFG]` | Configuration and orchestration decisions.                  | `ServiceDiscoveryCoordinator`, adapter configurators. | `info` with key/value context, `warn` on fallback.                 |
| `[K:SNAP]` | Rich blocks summarizing environment or version state.       | `KoanEnv`, version inventory.                         | `info` block with ruler.                                           |
| `[K:DATA]` | Data surface readiness and schema guard activity.           | `SqliteRepository`, `EntitySchemaGuard`.              | `info` condensed summaries, promote table/optimization decisions.  |
| `[K:SRVC]` | Background services orchestration lifecycle.                | `KoanBackgroundServiceOrchestrator`.                  | `info`; `warn` when a service fails to start.                      |
| `[K:HLTH]` | Probes and schedulers reporting status.                     | `HealthProbeScheduler`, `StartupProbeService`.        | `info` for aggregated status, `debug` for per-contributor details. |
| `[K:HOST]` | ASP.NET hosting lifecycle messages.                         | `Microsoft.Hosting.Lifetime`, slice banners.          | `info`.                                                            |

## Formatting patterns

Each stage token is a fixed four-character code rendered inside the brackets so every tag consumes the same width. The custom Koan console formatter also moves the logger category to a `src=` suffix and collapses it to the final type name so the lead of each line stays compact.

1. **Single-line decision summaries** – timestamp + severity + stage tag + canonical verb phrase.

   ```text
   21:07:38 info [K:CNFG] sqlite.discovery -> success source=local candidate="Data Source=./data/app.db" latency=132ms src=SqliteDiscoveryAdapter
   21:07:37 warn [K:CNFG] sqlite.discovery -> fallback reason="health check timeout" fallback="Data Source=Data/Koan.sqlite" src=SqliteDiscoveryAdapter
   21:07:38 info [K:BOOT] registrar.init -> loaded module=Koan.Data.Connector.Sqlite src=KoanAutoRegistrar
   ```

2. **Aggregated debug counts** – emit once per stage to replace repetitive debug spam.

   ```text
   21:07:38 debug [K:CNFG] sqlite.discovery.attempts -> summary tried=2 succeeded=1 failed=1 last_error="A task was canceled."
   ```

3. **Structured probe output** – key/value aligned for quick scanning and machine parsing.

   ```text
   21:07:38 info [K:HLTH] probe.startup name="Koan-background-services" status=Healthy running=9 pending=0 uptime="00:00:05"
   21:07:38 debug [K:HLTH] probe.detail name="SchedulingOrchestrator" status=Healthy uptime="00:00:05"
   ```

4. **Snapshot blocks** – only for environment or version inventories; wrap in ruler, align columns.

   ```text
   ┌─ [K:SNAP] Koan Environment ────────────────────────────────────────────
   │ EnvironmentName: Production        InContainer: False          Session: f508a2db
   │ OrchestrationMode: Standalone      KoanAssemblies: 11 loaded   ProcessStart: 2025-09-30T01:07:37Z
   └────────────────────────────────────────────────────────────────────────────
   ```

5. **Timeline footer (optional)** – summarize durations per stage once startup completes; the formatter appends `src=` so the emitting component stays discoverable without front-loading namespaces.

   ```text
   21:07:38 info [K:BOOT] startup.timeline -> summary boot=112ms config=486ms data=214ms services=430ms total=1.24s
   ```

## Stage helper API

`Koan.Core.Logging` now exposes `KoanLogStage` plus `ILogger` extensions so contributors can emit the fixed-format lines without handcrafting tokens.

```csharp
using Koan.Core.Logging;
using Microsoft.Extensions.Logging;

// Action + outcome + extra context
logger.LogKoanStage(
   KoanLogStage.Cnfg,
   LogLevel.Information,
   action: "sqlite.discovery",
   outcome: "success",
   ("source", "local"),
   ("candidate", "Data Source=./data/app.db"),
   ("latency", TimeSpan.FromMilliseconds(132)));

// Shorthand helpers for common severities
logger.LogKoanStageInfo(KoanLogStage.Srvc, "background.start", "started", ("count", 9));
logger.LogKoanStageWarning(KoanLogStage.Cnfg, "sqlite.discovery", "fallback", ("reason", "timeout"));
logger.LogKoanStageDebug(KoanLogStage.Hlth, "probe.detail", null, ("name", "SchedulingOrchestrator"));
```

## Bootstrap step delineation (proposal)

Bootstrap telemetry should read as three clear steps: invite, inspect, and declare ready.

1. **Init header block** – emitted by the host bootstrapper before any registrars execute. This anchors the console with a fixed-width banner that highlights the Koan runtime entering the startup pipeline.

   ```text
   ┌─ [K:BOOT] Koan Bootstrap ──────────────────────────────────────────────
   │ Runtime   : Koan.Core 1.0.0                Host: ASP.NET Core
   │ Assemblies: 38 (Modules: adapters, data, schedulers)
   │ Timestamp : 2025-09-30T01:31:01Z          Session: a99a2900
   └────────────────────────────────────────────────────────────────────────
   ```

   - Stage: `[K:BOOT]`
   - Owner: `StartupProbeService` or a dedicated `KoanBootstrapAnnouncer`
   - Context: Koan version, hosting stack, assembly count, session id.

2. **Inventory report block** – replaces the legacy ASCII list with a structured table that mixes version info and the KoanEnv snapshot. Use aligned columns and clear labels so operators can scan capabilities quickly.

   ```text
   ┌─ [K:SNAP] Koan Inventory ──────────────────────────────────────────────
   │ Core        1.0.0           Scheduler     0.6.3.0
   │ Data.Rel    0.6.3.0         Sqlite        0.6.3.0
   │ Web         0.6.3.0         Web.Ext       0.6.3.0
   │
   │ Environment: Production (Standalone)      InContainer: False
   │ Process    : Started 2025-09-30T01:31:01Z Uptime: 00:00:00.481
   │ Session    : a99a2900                     Machine: GARDEN-COOP
   └────────────────────────────────────────────────────────────────────────
   ```

   - Stage: `[K:SNAP]`
   - Owner: `KoanEnv.DumpSnapshot`
   - Layout: two-column version pairs, followed by KoanEnv properties rendered as `key: value` pairs with alignment.

3. **Ready declaration block** – published once background services, discovery, and health probes complete. This summarizes durations and exposes the base URL so slice operators can immediately click through.

   ```text
   ┌─ [K:HOST] Koan Ready ──────────────────────────────────────────────────
   │ Urls   : http://localhost:5000
   │ Timing : boot=112ms config=486ms data=214ms services=430ms total=1.24s
   │ Health : contributors=11 status=Healthy    Broadcast: initial probe sent
   └────────────────────────────────────────────────────────────────────────
   ```

   - Stage: `[K:HOST]`
   - Owner: combo of `KoanBackgroundServiceOrchestrator` and `Microsoft.Hosting.Lifetime`
   - Context: Base addresses, startup timeline, aggregated health summary.

### Implementation notes

- Banner rendering should live in a single helper (e.g., `KoanConsoleBlocks`) so every block shares rulers, padding, and column widths.
- Inject the header block from the earliest point in `Program` after logging is configured to guarantee it prints first.
- For the inventory block, hydrate data from `KoanEnvironmentSnapshot` so CLI and web hosts reuse the same code path.
- The ready block should only emit once all health prerequisites pass to avoid double-printing when hosted services restart.

## Developer experience guardrails

- Emit stage tags via the `ILogger` extensions (`logger.LogKoanStageInfo(KoanLogStage.Cnfg, ...)`) so contributors do not handcraft prefixes.
- Promote `info` messages only when a decision changes runtime behavior (e.g., selected connection string). Leave iterative attempts at `debug` or `trace`.
- Ensure fallback paths (`warn`) state the reason, fallback target, and next action to unblock operators.
- For multi-line snapshots, pad columns and keep width under 100 columns to avoid wrapping in standard consoles.
- Lean on structured logging (`logger.LogInformation("{Stage} {Action}...", stage, action, ...)`) so sinks can query `Stage` even when rendered with textual tags.

## Edge cases

1. **High-frequency schedulers** – throttle to one aggregated line per interval instead of per invocation.
2. **Multi-provider discovery** – summarize attempts with `tried`, `succeeded`, and `failed` counts; surface provider-specific errors at `debug`.
3. **Hosted services with long warmups** – emit a `debug` progress heartbeat every 30 seconds and escalate to `warn` if startup exceeds configured SLA.
4. **CI environments** – retain `[K:BLDG]` emissions so pipeline logs clearly separate compiler output from runtime telemetry.
5. **Operator overrides** – expose `KoanLoggingOptions.StageFilter` so slices can suppress or elevate stages without rewriting formatters.

## Implementation roadmap

1. Introduce `KoanLogStage` enum + extension helpers for tags.
2. Ship Koan console formatter to inject `[K:STAGE]`, align spacing, and suffix the trimmed source name.
3. Refactor core components (discovery, health probes, background services) to emit aggregated summaries.
4. Replace ASCII version banner with structured `[K:SNAP]` block and timeline footer.
5. Document customization entry points (options and configuration keys) under `docs/reference/observability/logging.md` (follow-up).

## Sample end-to-end startup excerpt

```text
21:07:36 info [K:BOOT] registrar.init -> loaded module=Koan.Core.Adapters src=KoanAutoRegistrar
21:07:36 info [K:BOOT] registrar.init -> loaded module=Koan.Data.Connector.Sqlite src=KoanAutoRegistrar
21:07:36 info [K:CNFG] sqlite.discovery -> delegating adapter="SqliteDiscoveryAdapter" src=ServiceDiscoveryCoordinator
21:07:37 warn [K:CNFG] sqlite.discovery -> fallback reason="autonomous discovery failed" fallback="Data Source=Data/Koan.sqlite" src=SqliteDiscoveryAdapter
21:07:37 info [K:DATA] schema.ensure -> create entity="g1c1.GardenCoop.Models.Plot" provider="sqlite" src=EntitySchemaGuard
┌─ [K:SNAP] Koan Environment ──────────────────────────────────────────────
│ EnvironmentName: Production        InContainer: False          Session: f508a2db
│ OrchestrationMode: Standalone      KoanAssemblies: 11 loaded   ProcessStart: 2025-09-30T01:07:37Z
└──────────────────────────────────────────────────────────────────────────────
21:07:38 info [K:SRVC] services.start -> ready count=9 src=KoanBackgroundServiceOrchestrator
21:07:38 info [K:HOST] Microsoft.Hosting.Lifetime -> listening url="http://localhost:5000" src=Microsoft.Hosting.Lifetime
21:07:43 info [K:SRVC] services.summary -> started="DatabaseMigrationService,HealthProbeScheduler,..." src=KoanBackgroundServiceOrchestrator
21:07:43 info [K:HLTH] probe.startup name="DatabaseMigrationService" status=Healthy duration=4.8s src=HealthProbeScheduler
21:07:43 info [K:BOOT] startup.timeline -> summary boot=112ms config=486ms data=214ms services=430ms total=1.24s src=StartupProbeService
```

