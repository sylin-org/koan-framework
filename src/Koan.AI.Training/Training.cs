using Koan.AI.Contracts.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Training;

/// <summary>
/// Static facade for training orchestration. Platform-agnostic verbs —
/// compute, runtime, and framework are resolved, not specified.
///
/// <code>
/// await Training.Train("meta-llama/Llama-3.1-8B", dataset);
/// await Training.Align(new AlignOptions { Base = model, Data = prefs });
/// await Training.Run(new RunOptions { Script = "./train.py" });
/// </code>
/// </summary>
public static class Training
{
    // ── Convenience overload ──

    /// <summary>Start a LoRA training job with minimal configuration.</summary>
    public static async Task<TrainingJob> Train(
        ModelRef @base,
        DatasetRef data,
        TrainMethod method = TrainMethod.LoRA,
        CancellationToken ct = default)
    {
        var options = new TrainOptions { Base = @base, Data = data, Method = method };
        return await ResolveService().TrainAsync(options, progress: null, ct);
    }

    /// <summary>Start a training job with full options and optional progress reporting.</summary>
    public static async Task<TrainingJob> Train(
        TrainOptions options,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await ResolveService().TrainAsync(options, progress, ct);
    }

    // ── Script escape hatch ──

    /// <summary>Run an arbitrary training script with Koan compute orchestration.</summary>
    public static async Task<TrainingJob> Run(
        RunOptions options,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await ResolveService().RunAsync(options, progress, ct);
    }

    // ── Alignment ──

    /// <summary>Run preference-based alignment (DPO, RLHF, KTO, ORPO).</summary>
    public static async Task<TrainingJob> Align(
        AlignOptions options,
        CancellationToken ct = default)
    {
        return await ResolveService().AlignAsync(options, ct);
    }

    // ── Comparison ──

    /// <summary>
    /// Train multiple variations and compare results. Returns jobs for each variation.
    /// </summary>
    public static async Task<IReadOnlyList<TrainingJob>> Compare(
        ModelRef @base,
        DatasetRef data,
        TrainOptions[] variations,
        string? eval = null,
        string[]? metrics = null,
        CancellationToken ct = default)
    {
        var service = ResolveService();
        var jobs = new List<TrainingJob>();

        foreach (var variation in variations)
        {
            var opts = variation with { Base = @base, Data = data };
            var job = await service.TrainAsync(opts, progress: null, ct);
            jobs.Add(job);
        }

        return jobs.AsReadOnly();
    }

    // ── Estimation ──

    /// <summary>Get a pre-flight estimate for a training job (tokens, GPU hours, cost).</summary>
    public static async Task<TrainingEstimate> Estimate(
        TrainOptions options,
        CancellationToken ct = default)
    {
        return await ResolveService().EstimateAsync(options, ct);
    }

    // ── Job lifecycle ──

    /// <summary>Get the current status of a training job.</summary>
    public static async Task<TrainingJob> Status(
        string jobId,
        CancellationToken ct = default)
    {
        return await ResolveService().StatusAsync(jobId, ct);
    }

    /// <summary>Cancel a running training job.</summary>
    public static async Task Cancel(
        string jobId,
        CancellationToken ct = default)
    {
        await ResolveService().CancelAsync(jobId, ct);
    }

    /// <summary>Resume a training job from a checkpoint.</summary>
    public static async Task<TrainingJob> Resume(
        string jobId,
        string? checkpoint = null,
        CancellationToken ct = default)
    {
        return await ResolveService().ResumeAsync(jobId, checkpoint, ct);
    }

    /// <summary>List all training jobs.</summary>
    public static async Task<IReadOnlyList<TrainingJob>> List(
        CancellationToken ct = default)
    {
        return await ResolveService().ListAsync(ct);
    }

    // ── Internal ──

    private static ITrainingService ResolveService()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Training not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Training.*");

        return provider.GetRequiredService<ITrainingService>();
    }
}
