---
type: RECIPE
recipe: ship-a-single-binary
title: "Ship it as one self-contained executable"
domain: platform
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: source-verified
  scope: snippets copied from samples/journeys/GardenCoop/01-GardenJournal, which compiles and publishes AOT
gets_you: "A native executable that starts fast, needs no .NET installed, and has no service to run beside it."
works_if: "Every capability the application uses can run in-process — embedded store, in-process AI, local files."
costs: "Longer publish times, per-platform builds, and reflection-dependent code needs trim roots declared."
ingredients:
  - "one | any Koan application | Sylin.Koan.App"
  - "one | an embedded store | Sylin.Koan.Data.Connector.Sqlite"
  - "optional | in-process embeddings, no model server | Sylin.Koan.AI.Connector.Onnx"
  - "optional | durable local vector index | Sylin.Koan.Data.Vector.Connector.SqliteVec"
---

# Ship it as one self-contained executable

Koan composes from references, and references are what a trimmer can see. If every capability resolves
to something in-process, the whole application publishes as a native binary.

## When this is the answer

"I want to hand someone a file and have it work." Edge and on-premise deployments, CLI-shaped tools,
demos that must survive a conference network, and anything where "install .NET first" is a real
obstacle.

**The constraint is the composition, not the flag.** AOT is not something you turn on at the end — it
is something an application stays eligible for. Every referenced capability must have an in-process
option: an embedded store rather than a database server, in-process embeddings rather than a model
server, a local vector index rather than a vector service. A single reference to a networked provider
does not break the build, but it does mean the binary still needs that service, which usually defeats
the point.

Ask before promising it:

- **Which capabilities are in play, and does each have an in-process form?** Answer this first; it
  decides feasibility.
- **How many platforms?** A native binary is per-architecture. Three targets is three builds.
- **Does anything rely on reflection?** Controllers, models, and anything discovered at runtime need to
  be rooted for the trimmer, or they vanish in a way that only shows up at runtime.

## Assembly

Keep AOT opt-in per build rather than always on, so ordinary development stays fast:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <InvariantGlobalization>true</InvariantGlobalization>
  <StripSymbols>true</StripSymbols>
  <IlcOptimizationPreference>Speed</IlcOptimizationPreference>
</PropertyGroup>

<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>

<ItemGroup>
  <TrimmerRootDescriptor Include="NativeAotRoots.xml" />
</ItemGroup>

<PropertyGroup Condition="'$(KoanAot)' == 'true'">
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

Declare what the trimmer must keep:

```xml
<linker>
  <assembly fullname="YourApp">
    <type fullname="YourApp.Controllers.*" preserve="all" />
    <type fullname="YourApp.Models.*" preserve="all" />
  </assembly>
</linker>
```

Koan emits a trim-root descriptor for its own composition when a trimming or AOT publish is configured,
so what you declare here is your application's reflection surface, not the framework's.

Depth: [NativeAOT how-to](../guides/nativeaot-howto.md).

## Prove it

1. **Behavior** — publish with AOT and run the *published binary*, not the debug build. Exercise a real
   journey through it. Trimming failures appear at runtime, not at compile time, which is the whole
   difficulty.
2. **Composition** — assert the published binary elected the in-process providers you intended.
3. **Correction** — assert a controller or model reachable only by reflection still resolves. If it
   404s in the published binary but works in development, a trim root is missing.

Run the published artifact in CI. An AOT configuration nobody published is a setting, not a capability.

## Boundaries

- AOT does not make a networked dependency local. It removes the runtime, not the architecture.
- Not every library is trim-safe; a dependency doing dynamic work may need roots or may not work at all.
- Binaries are per-platform, and publish is materially slower than an ordinary build.

## Interacts with

**Search by meaning.** The in-process ONNX connector plus a durable local vector index is what makes
semantic search possible inside a single binary — GardenCoop ships the embedding model as content
alongside the app, so it works with no Docker, no API key, and no vector server.
