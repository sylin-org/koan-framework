---
type: REFERENCE
domain: operations
title: "Runtime-self-contained publish"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/operations/single-binary.md - win-x64 publish-and-run of a template web
    application (trim roots + model-metadata pin): full Entity journey over HTTP, sqlite election in
    banner and facts; console exemplar remains the certified cross-check
---

# Runtime-self-contained publish

Publish a native artifact that starts without an installed .NET runtime and needs no service beside
it for the capabilities the application promises to run locally. It is one executable only when the
composition has no required content files; in-process ONNX adds model and vocabulary sidecars, making
the result an offline self-contained bundle instead.

## You need

| Piece | Package | Note |
|---|---|---|
| Koan web application | `Sylin.Koan.App` | references remain visible to composition and trimming |
| Embedded Entity store | `Sylin.Koan.Data.Connector.Sqlite` | durable local state with no database service |
| In-process embeddings (optional) | `Sylin.Koan.AI.Connector.Onnx` | model artifacts travel with the application |
| Durable local vector index (optional) | `Sylin.Koan.Data.Vector.Connector.SqliteVec` | semantic retrieval without a vector service |

## The constraint box

> **The constraint:** Artifact eligibility belongs to the entire composition, not a publish flag.
> Every required capability needs an in-process form, every required content file must travel with
> the artifact, reflection-discovered application types need trim roots, and each operating-system
> and architecture target needs its own published-and-run proof. Do not call a directory containing
> ONNX model files “one file.”
>
> The web path is held open by a build-time pin: NativeAOT compiles ASP.NET Core's model-metadata
> switch off, and in that mode every parameterized controller action throws on first request.
> `Sylin.Koan.Core` build assets pin `MvcEnhancedModelMetadataSupport=true` for consuming apps;
> packages predating that pin need it declared manually (see [ship a runtime-self-contained artifact](../../recipes/ship-a-single-binary.md)
> and the [NativeAOT how-to](../../guides/nativeaot-howto.md)). Each OS/architecture target still owes
> its own published-and-run proof; win-x64 is the one this repository has run.

## Audit the composition before publishing

| Capability shape | Effect on the promise |
|---|---|
| Embedded store and no required content files | can preserve a one-executable claim |
| In-process ONNX model and vocabulary files | preserves offline/no-service operation as a bundle, not one file |
| Application-owned local data or uploaded files | runtime state lives outside the executable and needs its own lifecycle |
| Networked database, model server, broker, or vector service | binary may publish, but it still needs that service |
| Runtime-only reflection with no trim root | may compile and disappear only in the published artifact |
| New OS or architecture target | a separate native build and runtime journey |

## Leaves

- **Build and decision guide:** [ship a runtime-self-contained artifact](../../recipes/ship-a-single-binary.md)
- **Publish contract:** [NativeAOT guide](../../guides/nativeaot-howto.md)
- **Runnable exemplar:**
  [AOT relational sample](https://github.com/sylin-org/koan-framework/blob/main/samples/fundamentals/AotRelational/AotRelational.csproj)
- **Offline bundle variant:** [portable embeddings](../ai/embedding/portable.md)

Run the published executable in CI and exercise a real Entity journey. A project property nobody
published is not a capability.
