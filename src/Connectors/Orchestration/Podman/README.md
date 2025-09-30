# Koan.Orchestration.Connector.Podman

> ✅ Validated against availability probes, compose lifecycle orchestration, and port parsing on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for detailed flows, component map, and edge-case coverage.

Podman implementation of `IHostingProvider`, allowing Koan CLI and orchestration tooling to manage Compose environments on rootless or Podman-first setups.

## Capabilities

- Detect Podman availability via `podman version --format json`.
- Start Compose plans with `podman compose up`, waiting for services to reach a `running`+`healthy` state within the configured timeout.
- Tear down stacks with `podman compose down` (optionally pruning volumes).
- Stream logs, capture live service status, and enumerate port bindings for downstream endpoint hinting.
- Report Podman engine metadata (client/server version, default system connection) for diagnostics and provider election.

## Quick verification

```pwsh
# From repo root, check Podman availability
dotnet run --project src/Koan.Orchestration.Cli -- doctor --engine podman --json

# Dry-run a compose launch with verbose diagnostics
dotnet run --project src/Koan.Orchestration.Cli -- up --engine podman --dry-run --explain
```

When Podman is reachable, `doctor` returns `available=true` and surfaces the detected engine version and connection name.

## Programmatic usage

```csharp
var provider = new PodmanProvider();
var availability = await provider.IsAvailableAsync();

if (availability.Ok)
{
    await provider.Up(".Koan/compose.yml", Profile.Local,
        new RunOptions(Detach: true, ReadinessTimeout: TimeSpan.FromSeconds(60)));

    var status = await provider.Status(new StatusOptions(Service: null));
    var ports = await provider.LivePorts();

    await provider.Down(".Koan/compose.yml", new StopOptions(RemoveVolumes: false));
}
```

- `RunOptions.Detach` maps to `podman compose up -d`.
- `RunOptions.ReadinessTimeout` controls how long the provider waits for `running`/`healthy` containers before throwing.
- `StopOptions.RemoveVolumes` adds `-v` to `podman compose down`.

## Edge cases to watch

- Podman sockets often require group membership (`podman` group) or custom permissions in rootless scenarios.
- Compose feature gaps (e.g., secrets, build directives) may differ from Docker; inspect `ProviderStatus` output to confirm readiness states.
- `podman compose ps --format json` returns JSON arrays; if future releases switch to NDJSON, Koan will skip malformed entries instead of failing.
- Informational stderr output is expected; exit codes determine failure.

## Related docs

- [`Koan.Orchestration.Cli`](../Koan.Orchestration.Cli/README.md) – caller of this provider.
- [`Koan.Orchestration.Connector.Docker`](../Koan.Orchestration.Connector.Docker/README.md) – sibling provider for Docker parity testing.
- `/docs/engineering/index.md`, `/docs/architecture/principles.md` – orchestration design tenets.

