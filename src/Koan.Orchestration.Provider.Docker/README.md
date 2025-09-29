# Koan.Orchestration.Provider.Docker

> ✅ Validated against availability probes, compose lifecycle orchestration, and port parsing on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for full flows, component map, and edge-case coverage.

Docker implementation of `IHostingProvider`, used by the Koan CLI and orchestration tooling to manage Compose-based environments.

## Capabilities

- Detect Docker availability (`docker version --format '{{.Server.Version}}'`).
- Start stacks with `docker compose up`, waiting for all services to reach a healthy state within the configured timeout.
- Stop and optionally prune volumes via `docker compose down`.
- Stream logs, capture service status, and enumerate live port bindings for downstream endpoint formatting.
- Surface engine metadata (server version, current context) for diagnostics and provider election.

## Quick verification

```pwsh
# From repo root, run the Koan CLI doctor command against the Docker provider
dotnet run --project src/Koan.Orchestration.Cli -- doctor --engine docker --json

# Bring up a compose plan (non-prod) and watch readiness handling
dotnet run --project src/Koan.Orchestration.Cli -- up --engine docker --dry-run --explain
```

If Docker is reachable, `doctor` reports `available=true` and the `engine` block lists the detected server version and context.

## Programmatic usage

```csharp
var provider = new DockerProvider();
var availability = await provider.IsAvailableAsync();

if (availability.Ok)
{
    await provider.Up(".Koan/compose.yml", Profile.Local, new RunOptions(Detach: true, ReadinessTimeout: TimeSpan.FromSeconds(60)));
    var status = await provider.Status(new StatusOptions(Service: null));
    var ports = await provider.LivePorts();
    await provider.Down(".Koan/compose.yml", new StopOptions(RemoveVolumes: false));
}
```

- `RunOptions.Detach` controls whether `docker compose up` returns immediately or attaches to service output.
- `RunOptions.ReadinessTimeout` ensures the call fails if containers never reach `running/healthy`.
- `StopOptions.RemoveVolumes` maps to `docker compose down -v`.

## Edge cases to watch

- Docker CLI may emit NDJSON; parsers tolerate both NDJSON and array outputs.
- Informational stderr output is expected even on success—only exit codes are treated as failures.
- IPv6 bindings (`:::8080->80/tcp`) are parsed and normalized before returning `PortBinding` records.
- When the CLI is missing, status/logs fall back quietly while `IsAvailableAsync` delivers a helpful reason string.

## Related docs

- [`Koan.Orchestration.Cli`](../Koan.Orchestration.Cli/README.md) – commands using this provider.
- [`Koan.Orchestration.Provider.Podman`](../Koan.Orchestration.Provider.Podman/README.md) – sibling provider for Podman engines.
- `/docs/engineering/index.md`, `/docs/architecture/principles.md` – orchestration design principles.
