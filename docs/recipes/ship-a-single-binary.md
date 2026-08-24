---
type: RECIPE
recipe: ship-a-single-binary
title: "Ship a runtime-self-contained artifact"
domain: platform
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: source-verified
  scope: snippets copied from samples/journeys/GardenCoop/01-GardenJournal, which compiles and publishes AOT
gets_you: "A native artifact that starts fast, needs no .NET installed, and has no service to run beside it - one executable when no capability needs content sidecars, otherwise an offline bundle."
works_if: "Every capability the application uses can run in-process — embedded store, in-process AI, local files."
costs: "Longer publish times, per-platform builds, and reflection-dependent code needs trim roots declared."
ingredients:
  - "one | any Koan application | Sylin.Koan.App"
  - "one | an embedded store | Sylin.Koan.Data.Connector.Sqlite"
  - "optional | in-process embeddings, no model server | Sylin.Koan.AI.Connector.Onnx"
  - "optional | durable local vector index | Sylin.Koan.Data.Vector.Connector.SqliteVec"
absent:
  - "a verified Linux or arm64 build | only win-x64 has been published and run here | publish and test your own target; the cross-compilation toolchain differs enough that the win-x64 result does not carry over"
---

# Ship a runtime-self-contained artifact

Koan composes from references, and references are what a trimmer can see. If every capability resolves
to something in-process, the application can publish as a native artifact with no adjacent service.
That is not automatically one file: ONNX models, vocabularies, application content, and runtime data
remain separate unless their owner explicitly supports embedding them.

## When this is the answer

"I want to hand someone an artifact and have it work offline." Edge and on-premise deployments,
CLI-shaped tools, demos that must survive a conference network, and anything where "install .NET
first" is a real obstacle. If the requirement literally says *one file*, audit content sidecars before
promising it.

**The constraint is the composition, not the flag.** AOT is not something you turn on at the end — it
is something an application stays eligible for. Every referenced capability must have an in-process
option: an embedded store rather than a database server, in-process embeddings rather than a model
server, a local vector index rather than a vector service. A single reference to a networked provider
does not break the build, but it does mean the binary still needs that service, which usually defeats
the point.

Ask before promising it:

- **Which capabilities are in play, and does each have an in-process form?** Answer this first; it
  decides feasibility.
- **Does any capability need content beside the executable?** ONNX model and vocabulary files make
  the deliverable an offline bundle even though no model service is required.
- **How many platforms?** A native binary is per-architecture, so three targets is three builds -- and only win-x64 has been published and run in this repository. Treat another target as work you are measuring.
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
so what you declare here is your application's reflection surface, not the framework's. The same build
assets pin MVC's model-metadata switch for the publish — without it, ASP.NET Core's binder throws on
every parameterized controller action inside the artifact (see the NativeAOT how-to). On current
`Sylin.Koan.Core` packages this is automatic; older ones need
`<MvcEnhancedModelMetadataSupport>true</MvcEnhancedModelMetadataSupport>` declared alongside `PublishAot`.

Depth: [NativeAOT how-to](../guides/nativeaot-howto.md).

## Prove it

1. **Behavior** — publish with AOT and run the *published binary*, not the debug build. Exercise a real
   journey through it. Trimming failures appear at runtime, not at compile time, which is the whole
   difficulty.
2. **Composition** — assert the published binary elected the in-process providers you intended.
3. **Correction** — assert a controller or model reachable only by reflection still resolves. If it
   404s in the published binary but works in development, a trim root is missing.

When the composition includes content files, prove the *published directory* from a clean location,
then remove one required sidecar and assert the startup or first-use error names the missing artifact.

Run the published artifact in CI. An AOT configuration nobody published is a setting, not a capability.

## Boundaries

- AOT does not make a networked dependency local. It removes the runtime, not the architecture.
- NativeAOT does not embed arbitrary application content. A no-service bundle and a one-file artifact
  are different claims.
- Not every library is trim-safe; a dependency doing dynamic work may need roots or may not work at all.
- Binaries are per-platform, and publish is materially slower than an ordinary build.

## Interacts with

**Search by meaning.** The in-process ONNX connector plus a durable local vector index is what makes
semantic search possible inside an offline bundle — GardenCoop ships the embedding model as content
alongside the executable, so it works with no Docker, no API key, and no vector server. Because those
model files are sidecars, this variant is not literally one file.
