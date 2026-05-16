# Koan.AI.Models

Centralized model lifecycle management for Koan: search, pull, inspect, convert, quantize, deploy, version, and audit AI models across providers.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Models
```

## Quick Start

```csharp
// Search for a model
var entries = await Model.Search(new ModelQuery
{
    Keywords    = ["llama", "instruct"],
    Quantization = Quantization.Q4_K_M,
    Format       = ModelFormat.Gguf,
});

// Pull a model (download with progress)
await foreach (var progress in Model.Pull("llama3:8b-instruct-q4_K_M"))
    Console.WriteLine($"{progress.Stage} {progress.Percent:P0}");

// Deploy and list viable routes
var routes = await Model.Routes("llama3:8b-instruct-q4_K_M");
foreach (var route in routes)
    Console.WriteLine($"{route.Runtime} via {route.Compute?.DeviceName}");
```

## Core API

```csharp
Model.Search(ModelQuery query)              // → IReadOnlyList<ModelEntry>
Model.Pull(string modelId)                  // → IAsyncEnumerable<ModelPullProgress>
Model.Inspect(string modelId)               // → ModelEntry
Model.Convert(string modelId, ModelFormat)  // → ConversionJob
Model.Quantize(string modelId, Quantization)// → QuantizationJob
Model.Merge(string[] modelIds, MergeOptions)// → MergeJob
Model.Deploy(string modelId)                // → DeployResult
Model.Routes(string modelId)                // → IReadOnlyList<ModelRoute>
Model.History(string modelId)               // → IReadOnlyList<ModelHistoryEntry>
Model.Rollback(string modelId, string version) // → void
Model.Audit(string modelId)                 // → AuditReport
Model.Register(ModelEntry entry)            // → void  — add to catalog
Model.List()                                // → IReadOnlyList<ModelEntry>
Model.Remove(string modelId)                // → void
Model.Prune()                               // → PruneReport  — remove unused/old
Model.Health(string modelId)                // → ModelHealthReport
```

## `ModelStatus` Lifecycle

```
Cached → Loaded → Deployed → Standby
```

## Supported Formats

| `ModelFormat` | Description |
|--------------|-------------|
| `Gguf` | llama.cpp format (recommended for local) |
| `Safetensors` | HuggingFace format |
| `Onnx` | ONNX Runtime format |
| `Mlx` | Apple MLX format |

## Quantizations

`Q4_K_M`, `Q5_K_M`, `Q8_0`, `F16`, `F32` — standard GGUF quantization levels.

## Reference

- **ADR**: `docs/decisions/AI-0023-model-catalog-and-lifecycle.md`
- **Related**: `Koan.AI.Compute` (deployment routing), `Koan.AI.Training` (fine-tuning), `Koan.AI.Eval` (quality gates)
