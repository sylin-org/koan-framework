using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Configuration for a training job. Immutable — use <c>with</c> expressions to customize.
/// </summary>
public sealed record TrainOptions
{
    /// <summary>Base model to fine-tune.</summary>
    public required ModelRef Base { get; init; }

    /// <summary>Training dataset.</summary>
    public required DatasetRef Data { get; init; }

    /// <summary>Training method. Defaults to LoRA.</summary>
    public TrainMethod Method { get; init; } = TrainMethod.LoRA;

    /// <summary>LoRA rank (r). Higher = more parameters = more capacity.</summary>
    public int Rank { get; init; } = 16;

    /// <summary>LoRA alpha scaling factor.</summary>
    public int LoraAlpha { get; init; } = 32;

    /// <summary>Target modules for LoRA injection (null = auto-detect).</summary>
    public string[]? TargetModules { get; init; }

    /// <summary>Number of training epochs.</summary>
    public int Epochs { get; init; } = 3;

    /// <summary>Initial learning rate.</summary>
    public double LearningRate { get; init; } = 2e-4;

    /// <summary>Micro-batch size per device.</summary>
    public int BatchSize { get; init; } = 4;

    /// <summary>Gradient accumulation steps (effective batch = BatchSize * GradientAccumulation).</summary>
    public int GradientAccumulation { get; init; } = 4;

    /// <summary>Maximum sequence length in tokens.</summary>
    public int MaxSequenceLength { get; init; } = 2048;

    /// <summary>Output model name (null = auto-generated).</summary>
    public string? OutputName { get; init; }

    /// <summary>Compute requirements for the training job.</summary>
    public ComputeRequirement? Compute { get; init; }

    /// <summary>Level 2 override: custom evaluation script path.</summary>
    public string? EvalScript { get; init; }

    /// <summary>Level 2 override: custom data collator class name.</summary>
    public string? DataCollator { get; init; }

    /// <summary>Additional pip package dependencies.</summary>
    public string[]? Dependencies { get; init; }
}
