# Sora.Storage

Storage orchestrator for Sora apps. Simple, profile-based routing with first-class DX to create, read, probe, copy, move, and delete objects across providers.

## What it does

- Routing: Resolves profile → provider+container using options or rules with safe defaults.
- Orchestration: Streams data and computes SHA-256 for seekable uploads; supports range reads.
- Capabilities: Leverages provider features like server-side copy; presign when available.
- DX: Small, async helpers without Async suffix and a model-centric API for clean calls.

## Setup

- Registration: Packages auto-register via the framework pipeline. Call `SoraInitialization.InitializeModules(services)` once. Providers (e.g., Local) self-register.
- Core options:
  - `Sora:Storage:Profiles:<name>` → `{ Provider, Container }`
  - `Sora:Storage:DefaultProfile` → string (optional)
  - `Sora:Storage:FallbackMode` → `Disabled | SingleProfileOnly | NamedDefault` (default `SingleProfileOnly`)
  - `Sora:Storage:ValidateOnStart` → `bool` (default `true`)

Notes
- Ambient DI: APIs resolve `IStorageService` from `SoraApp.Current`. In app host, this is set during startup (built into Sora templates).

## Model‑centric API (recommended)

Bind a model type to a profile (and optionally a default container) and use concise statics/instance ops.

```csharp
// Bind to profile "hot" (container optional; provider+container resolve via options)
[Sora.Storage.Infrastructure.StorageBinding("hot")] 
public sealed class FileA : Sora.Storage.Model.StorageEntity<FileA> { }

[Sora.Storage.Infrastructure.StorageBinding("cold")] 
public sealed class FileB : Sora.Storage.Model.StorageEntity<FileB> { }

// Create → Read → Copy/Move
var rec = await FileA.CreateTextFile("name.txt", "hello");
var text = await FileA.Get(rec.Key).ReadAllText();
await FileA.Get(rec.Key).CopyTo<FileB>();
await FileA.Get(rec.Key).MoveTo<FileB>();

// Other instance ops
var bytes = await FileA.Get(rec.Key).ReadAllBytes();
var head  = await FileA.Get(rec.Key).Head(); // ObjectStat
```

Contract
- Inputs: `key`, optional `name`, content (`string` / `byte[]` / `Stream` / object for JSON), optional `contentType`.
- Outputs: `IStorageObject` with metadata (`Id`, `Key`, `Name`, `ContentType`, `Size`, `ContentHash`, `CreatedAt`, `UpdatedAt`, `Provider`, `Container`, `Tags`).
- Errors: Unknown profile/container → throws; missing key → `null` for `Head`, `false` for `Delete`; invalid range → throws. Hash computed only on seekable streams.

## Service helpers (alternative)

The same primitives are available directly on `IStorageService`.

```csharp
await storage.CreateTextFile("doc.txt", "hello", profile: "main");
var text = await storage.ReadAllText("main", "", "doc.txt");
var stat = await storage.HeadAsync("main", "", "doc.txt");
await storage.CopyTo("hot", "", "doc.txt", "cold");
```

## Behavioral notes and edge cases

- Defaults and fallbacks: If profile is omitted, `DefaultProfile` is used when set; with exactly one profile, `SingleProfileOnly` fallback applies.
- Validation: With `ValidateOnStart=true`, misconfigurations fail fast during app start.
- Large payloads: Use `ReadRangeAsString` / `ReadRangeAsync` to avoid loading entire content.
- Capabilities: Local provider supports lightweight stat and server-side copy; presign is unsupported.

## References

- Reference: `docs/reference/storage.md`
- Decisions: `STOR-0001`, `STOR-0006`, `STOR-0007`, `DATA-0061`, `ARCH-0040`
