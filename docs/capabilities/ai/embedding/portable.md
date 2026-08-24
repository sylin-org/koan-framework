---
type: REFERENCE
domain: ai
title: "Embedding variant: portable offline bundle"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/ai/embedding/portable.md
---

# Embedding variant: portable offline bundle

In-process ONNX: no model server, no network call, no account. The model artifacts travel with
the application, so the published bundle works offline, including with a NativeAOT executable. The
ONNX graph and vocabulary remain content sidecars; this is not literally one file. The inherited
constraint from [semantic search](../semantic-search.md) applies unchanged: one model, one
dimensionality, everywhere.

## Variant gotchas

1. **Artifacts are side-loaded, never downloaded.** The connector downloads nothing at runtime -
   that is the air-gap guarantee. Fetch the two files (ONNX graph + wordpiece vocabulary) once and
   commit them to the project. Full instructions and a pasteable download block:
   [Onnx connector README - Get the model artifacts](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/AI/Onnx/README.md#get-the-model-artifacts).
   After `dotnet add package`, the same README is restored beside the package on disk
   (`~/.nuget/packages/sylin.koan.ai.connector.onnx/<version>/README.md`) - version-exact and offline.
2. **Relative artifact paths resolve against the build output**, not the content root. Copy the
   artifacts into the project and let the SDK carry them:
   `<Content Include="models\**" CopyToOutputDirectory="PreserveNewest" />`.
3. **Size and file count are the trade.** The quantized reference model is ~22 MB plus vocabulary -
   fine for an offline tool bundle, worth reconsidering if the complete directory cannot ship.

## Do not, at this variant

- Do not expect runtime downloads - absent artifacts mean an inactive embedder, stated at
  startup, not a fallback.
- Do not reference model paths outside the copied content folder; relative resolution lands in
  the build output.

## Mechanics

Configuration keys, startup behavior, and health reporting are owned by the connector README -
read it online:
[Onnx connector README](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/AI/Onnx/README.md),
or from disk once the package is restored
(`~/.nuget/packages/sylin.koan.ai.connector.onnx/<version>/README.md`).

When the scale outgrows the portable posture, the next rungs are the
[Ollama connector](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/AI/Ollama/README.md) (local model server) and -
when it exists - a hosted path. Re-indexing is part of that move; the constraint box travels with
you.
