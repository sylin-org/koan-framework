# Sora.Orchestration.Abstractions

Contracts for Sora DevHost orchestration (see docs/engineering/orchestration-spi.md):
- IDevServiceDescriptor: declares intended services.
- IHostingProvider: runs/inspects stacks.
- IArtifactExporter: generates artifacts like docker-compose.

Includes small model types (ServiceSpec, Plan, HealthSpec) and utilities (redaction, event ids).
