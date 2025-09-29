# Sylin.Koan.AI.Contracts

Contracts for Koan AI adapters and routing (models, options, adapters, router).

- Target framework: net9.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.AI.Contracts
```

## Model management metadata

Adapters can surface automated model provisioning capabilities through the new `AiCapabilities.ModelManagement` property. When
present, it advertises which provisioning operations the adapter supports (`SupportsInstall`, `SupportsRemove`, `SupportsRefresh`)
along with whether provenance metadata is honored. Use the companion `IAiModelManager` interface to expose actionable
`EnsureInstalledAsync`, `RefreshAsync`, and `FlushAsync` methods so Koan services or operators can trigger model lifecycle events
programmatically.
