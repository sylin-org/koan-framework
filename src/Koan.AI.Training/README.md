# Koan.AI.Training

Training orchestration and dataset management for Koan. Supports LoRA fine-tuning, preference alignment (DPO/RLHF/KTO/ORPO), dataset analysis, and job lifecycle management with 4-level escape hatches.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Training
```

## Quick Start

```csharp
// Fine-tune a model with LoRA
var job = await Training.Train("base-model:7b", dataset: myDataset);
Console.WriteLine($"Job: {job.Id} — {job.Status}");

// Stream progress
await foreach (var progress in Training.Status(job.Id, ct))
    Console.WriteLine($"Step {progress.Step}/{progress.TotalSteps}  loss={progress.Loss:F4}");
```

## Core API

```csharp
Training.Train(string modelId, Dataset dataset)         // → TrainingJob
Training.Train(string modelId, Dataset dataset, TrainOptions options) // with options
Training.Run(string scriptPath, RunOptions options)     // Escape hatch: run script directly
Training.Align(string modelId, Dataset prefs, AlignOptions) // DPO/RLHF/KTO/ORPO
Training.Compare(TrainingJob a, TrainingJob b)          // → ComparisonReport
Training.Estimate(string modelId, Dataset dataset)      // → TrainingEstimate (cost preview)
Training.Status(string jobId, CancellationToken ct)     // → IAsyncEnumerable<TrainingProgress>
Training.Cancel(string jobId)                           // → void
Training.Resume(string jobId)                           // Resume from checkpoint
Training.List()                                         // → IReadOnlyList<TrainingJob>
```

## Training Methods

| `TrainMethod` | Description |
|--------------|-------------|
| `LoRA` | Low-Rank Adaptation (default, recommended) |
| `FullFinetuning` | Full parameter fine-tuning |

## Alignment Methods (Preference Tuning)

| Method | Description |
|--------|-------------|
| DPO | Direct Preference Optimization |
| RLHF | Reinforcement Learning from Human Feedback |
| KTO | Kahneman-Tversky Optimization |
| ORPO | Odds Ratio Preference Optimization |

## `TrainOptions`

```csharp
var job = await Training.Train("base-model:7b", dataset, new TrainOptions
{
    Method      = TrainMethod.LoRA,
    Epochs      = 3,
    LearningRate = 2e-4,
    BatchSize   = 4,
    OutputModel  = "my-custom-model:v1"
});
```

## 4-Level Escape Hatches

| Level | API | Use when |
|-------|-----|---------|
| 1 | `Training.Train(model, dataset)` | Default LoRA — works in most cases |
| 2 | `Training.Train(model, dataset, TrainOptions)` | Custom hyperparameters |
| 3 | `Training.Run(scriptPath, RunOptions)` | Existing training scripts |
| 4 | Implement `ITrainingRuntime` | Fully custom runtime |

## `TrainingEstimate` (pre-flight cost preview)

```csharp
var estimate = await Training.Estimate("llama3:8b", myDataset);
Console.WriteLine($"Tokens: {estimate.Tokens:N0}");
Console.WriteLine($"GPU Hours: {estimate.GpuHours:F1}");
Console.WriteLine($"Est. Cost: ${estimate.EstimatedCostUsd:F2}");
```

## Reference

- **ADR**: `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md`
- **Related**: `Koan.AI.Review` (feedback loop → training datasets), `Koan.AI.Eval` (post-training quality gates), `Koan.AI.Models` (deploy trained models)
