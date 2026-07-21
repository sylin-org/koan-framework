# Sylin.Koan.ZenGarden.Contracts

Inert client, discovery, connection-intent, and capability contracts for layered Zen Garden integrations.

Most applications receive this package through `Sylin.Koan.ZenGarden` or a connector that can optionally use Zen
Garden. Reference it directly only when implementing an integration boundary without activating the runtime client.

## Install

```powershell
dotnet add package Sylin.Koan.ZenGarden.Contracts
```

## Meaningful use

A connector can recognize explicit Zen Garden intent while remaining usable with its native configuration:

```csharp
if (ZenGardenConnectionIntent.TryParse(
        "zen-garden://ollama:dev?cap=llama3.2,nomic-embed-text",
        out var intent))
{
    var offering = intent.ToOfferingSelector(); // ollama:dev
}
```

The runtime package supplies `IZenGardenInitializationProvider`; connectors ask for it optionally. When the runtime is
absent, automatic layered capability remains dormant. An explicit `zen-garden://` choice must fail with the connector's
correction rather than silently selecting another backend.

`IZenGardenClient` is the portable client boundary for catalog, subscription, and capability wishes. `ToolFqid` owns
offering identity parsing so connectors and the runtime do not invent competing grammars.

## Boundaries and failures

- Referencing Contracts registers no services, opens no connection, performs no discovery, and activates no module.
- Contracts describe intent and results; `Sylin.Koan.ZenGarden` owns the client, discovery, subscriptions, and runtime
  implementation.
- A syntactically valid connection intent does not prove that an offering exists, is ready, or satisfies a capability.
- Capability wishes are requests, not completion or availability guarantees. Observe the returned receipt/progress.
- Integrations retain ownership of their native configuration, health probe, fallback policy, and corrective error.

## Technical reference

See the [technical contract](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.ZenGarden.Contracts/TECHNICAL.md)
for assembly ownership and layered activation rules.
