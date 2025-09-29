# Koan.Orchestration.Abstractions

> ✅ Validated against orchestration interfaces, attributes, and planning models on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for the full reference.

Contracts for Koan DevHost orchestration (see docs/engineering/orchestration-spi.md):
- IDevServiceDescriptor: declares intended services.
- IHostingProvider: runs/inspects stacks.
- IArtifactExporter: generates artifacts like docker-compose.

Includes small model types (ServiceSpec, Plan, HealthSpec) and utilities (redaction, event ids).

## Documentation
- [`TECHNICAL.md`](./TECHNICAL.md) – service declaration attributes, provider lifecycle, exporter capabilities, and validation notes.
