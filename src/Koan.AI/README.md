# Sylin.Koan.AI

AI router and registry for Koan: plug multiple AI adapters and route prompts/embeddings.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.AI
```

## Host behavior

- `Client.IsAvailable` and `Client.TryResolve()` are optional probes: a missing or disposed host
  returns `false` or `null`.
- Required operations such as `Client.Chat(...)` throw `KoanHostContextException` when the host is
  absent, disposed, or missing the AI pipeline. The exception identifies the operation, required
  service, and corrective startup paths.

## Links
- AI contracts: https://github.com/sylin-org/Koan-framework/blob/dev/src/Koan.AI.Contracts
