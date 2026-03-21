using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Training service that delegates to registered <see cref="ITrainingRuntime"/> instances.
/// Runtime implementations are discovered via Reference = Intent.
/// </summary>
internal sealed class TrainingService : ITrainingService
{
    private readonly IReadOnlyList<ITrainingRuntime> _runtimes;

    public TrainingService(IEnumerable<ITrainingRuntime> runtimes)
    {
        _runtimes = runtimes.ToList().AsReadOnly();
    }

    public async Task<TrainingJob> TrainAsync(
        TrainOptions options, IProgress<TrainingProgress>? progress, CancellationToken ct)
    {
        var runtime = await ResolveRuntime(options.Method, ct);
        return await runtime.LaunchAsync(options, progress, ct);
    }

    public async Task<TrainingJob> RunAsync(
        RunOptions options, IProgress<TrainingProgress>? progress, CancellationToken ct)
    {
        var runtime = await ResolveAnyRuntime(ct);
        return await runtime.LaunchScriptAsync(options, progress, ct);
    }

    public async Task<TrainingJob> AlignAsync(AlignOptions options, CancellationToken ct)
    {
        // Alignment is a specialized training run
        var trainOptions = new TrainOptions
        {
            Base = options.Base,
            Data = options.Data,
            Method = options.Method switch
            {
                AlignMethod.DPO => TrainMethod.LoRA,
                AlignMethod.RLHF => TrainMethod.LoRA,
                AlignMethod.KTO => TrainMethod.LoRA,
                AlignMethod.ORPO => TrainMethod.LoRA,
                _ => TrainMethod.LoRA
            },
            Compute = options.Compute
        };

        var runtime = await ResolveRuntime(trainOptions.Method, ct);
        return await runtime.LaunchAsync(trainOptions, null, ct);
    }

    public Task<TrainingEstimate> EstimateAsync(TrainOptions options, CancellationToken ct)
    {
        // Estimation doesn't require a runtime — it's computation based on dataset size and model
        var estimate = new TrainingEstimate
        {
            Tokens = 0, // Would be computed from Dataset.Analyze()
            EstimatedGpuHours = 0,
            EstimatedCost = null,
            RecommendedCompute = _runtimes.Count > 0
                ? _runtimes[0].Id
                : "No training runtime available",
            FitsLocalGpu = false,
            Reason = _runtimes.Count == 0
                ? "No training runtimes registered."
                : $"Available runtimes: {string.Join(", ", _runtimes.Select(r => r.Id))}"
        };

        return Task.FromResult(estimate);
    }

    public async Task<TrainingJob> StatusAsync(string jobId, CancellationToken ct)
    {
        foreach (var runtime in _runtimes)
        {
            try
            {
                return await runtime.StatusAsync(jobId, ct);
            }
            catch
            {
                // This runtime doesn't know about this job — try next
            }
        }

        throw new InvalidOperationException(
            $"Job '{jobId}' not found in any registered training runtime.");
    }

    public async Task CancelAsync(string jobId, CancellationToken ct)
    {
        foreach (var runtime in _runtimes)
        {
            try
            {
                await runtime.CancelAsync(jobId, ct);
                return;
            }
            catch
            {
                // Try next runtime
            }
        }

        throw new InvalidOperationException(
            $"Job '{jobId}' not found in any registered training runtime.");
    }

    public async Task<TrainingJob> ResumeAsync(string jobId, string? checkpoint, CancellationToken ct)
    {
        foreach (var runtime in _runtimes)
        {
            try
            {
                return await runtime.ResumeAsync(jobId, checkpoint, ct);
            }
            catch
            {
                // Try next runtime
            }
        }

        throw new InvalidOperationException(
            $"Job '{jobId}' not found in any registered training runtime.");
    }

    public Task<IReadOnlyList<TrainingJob>> ListAsync(CancellationToken ct)
    {
        // Aggregate jobs from all runtimes — for now return empty
        // Real implementation would query each runtime
        IReadOnlyList<TrainingJob> empty = [];
        return Task.FromResult(empty);
    }

    private async Task<ITrainingRuntime> ResolveRuntime(TrainMethod method, CancellationToken ct)
    {
        if (_runtimes.Count == 0)
            throw new InvalidOperationException(
                "No training runtime registered. Add a training runtime reference to enable training.");

        // Find a runtime that supports the requested method and is available
        foreach (var runtime in _runtimes)
        {
            if (runtime.SupportedMethods.Contains(method) && await runtime.IsAvailableAsync(ct))
                return runtime;
        }

        // Fall back to any available runtime
        foreach (var runtime in _runtimes)
        {
            if (await runtime.IsAvailableAsync(ct))
                return runtime;
        }

        throw new InvalidOperationException(
            $"No available training runtime supports {method}. " +
            $"Registered: [{string.Join(", ", _runtimes.Select(r => r.Id))}].");
    }

    private async Task<ITrainingRuntime> ResolveAnyRuntime(CancellationToken ct)
    {
        if (_runtimes.Count == 0)
            throw new InvalidOperationException(
                "No training runtime registered. Add a training runtime reference to enable training.");

        foreach (var runtime in _runtimes)
        {
            if (await runtime.IsAvailableAsync(ct))
                return runtime;
        }

        throw new InvalidOperationException(
            $"No training runtime is currently available. " +
            $"Registered: [{string.Join(", ", _runtimes.Select(r => r.Id))}].");
    }
}
