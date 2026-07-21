---
type: REF
domain: storage
title: "Storage — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: verified
  scope: compiled Storage routing and Local provider journey
---

# Storage — pillar map

Storage maps Entity-readable profile intent to provider-backed bytes. References make providers available;
`AddKoan()` compiles provider identity, placement, priority, capabilities, and profile routes once.

## Canonical expression

```csharp
[StorageBinding("main", "documents")]
public sealed class Document : StorageEntity<Document> { }

var document = await Document.Onboard("contract.pdf", stream, "application/pdf");
await using var content = await document.OpenRead();
```

```json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "main": { "Container": "documents" }
      },
      "Providers": {
        "Local": { "BasePath": ".koan/storage" }
      }
    }
  }
}
```

Install `Sylin.Koan.Storage.Connector.Local` for this path. No Storage-specific registration call is supported or
needed.

## Decisions you can express

| Intent | Expression |
|---|---|
| Bind a model to logical placement | `[StorageBinding(Profile, Container)]` |
| Select a default among several profiles | `StorageOptions.DefaultProfile` / `Koan:Storage:DefaultProfile` |
| Require one topology | profile `Mode: Local`, `Remote`, or `Replicated` |
| Pin one provider | profile `Provider: "local"` / `"s3"` |
| Tune replicated cache | profile `LocalCache` |

One profile needs no extra default knob. Several profiles can boot without a default, but unqualified operations then
fail and ask for `DefaultProfile` or an explicit profile.

## Election and guarantees

- Exact provider pins win.
- Automatic selection filters by `StorageProviderPlacement`, then uses `[ProviderPriority]`, then stable identity.
- No mode + one placement uses that provider; no mode + Local and Remote composes replication.
- Explicit Replicated mode requires both placements and never degrades silently.
- Provider optional interfaces and `StorageCaps` declarations must agree at composition.
- Startup facts report available providers, compiled profile elections, capability tokens, containers, and default
  posture without credentials.

## Escape hatch

Use `IStorageService` for infrastructure workflows that are not naturally one Entity:

```csharp
var storage = services.GetRequiredService<IStorageService>();
var stored = await storage.Put("main", "documents", key, stream, "application/pdf");
var (chunk, length) = await storage.ReadRange("main", "documents", key, 0, 4095);
await foreach (var item in storage.ListObjects("main", "documents", "2026/"))
    Console.WriteLine(item.Key);
```

Provider pages define actual streaming, durability, consistency, presign, and resource limits. See the
[Storage reference](../storage/index.md).
