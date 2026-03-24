using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Shared;
using Koan.AI.Resolution;
using Koan.Core.AI;

namespace Koan.AI.Training;

/// <summary>
/// Training service that resolves training operations through adapters
/// with <see cref="AiCapability.Train"/> and <see cref="AiCapability.Align"/> capabilities.
/// </summary>
internal sealed class TrainingService(IAiAdapterRegistry registry) : ITrainingService
{

    public async Task<TrainingJob> Train(
        TrainOptions options, IProgress<TrainingProgress>? progress, CancellationToken ct)
    {
        var adapter = AdapterResolver.Resolve(registry, AiCapability.Train, options.Compute?.PreferredNode);
        var runtime = ResolveTrainingRuntime(adapter);
        return await runtime.Launch(options, progress, ct);
    }

    public async Task<TrainingJob> Run(
        RunOptions options, IProgress<TrainingProgress>? progress, CancellationToken ct)
    {
        var adapter = AdapterResolver.Resolve(registry, AiCapability.Train, options.Compute?.PreferredNode);
        var runtime = ResolveTrainingRuntime(adapter);
        return await runtime.LaunchScript(options, progress, ct);
    }

    public async Task<TrainingJob> Align(AlignOptions options, CancellationToken ct)
    {
        var adapter = AdapterResolver.Resolve(registry, AiCapability.Align, options.Compute?.PreferredNode);
        var runtime = ResolveTrainingRuntime(adapter);

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

        return await runtime.Launch(trainOptions, null, ct);
    }

    public Task<TrainingEstimate> Estimate(TrainOptions options, CancellationToken ct)
    {
        var trainAdapters = AdapterResolver.ResolveAll(registry, AiCapability.Train);

        var estimate = new TrainingEstimate
        {
            Tokens = 0,
            EstimatedGpuHours = 0,
            EstimatedCost = null,
            RecommendedCompute = trainAdapters.Count > 0
                ? trainAdapters[0].Id
                : "No training adapter available",
            FitsLocalGpu = false,
            Reason = trainAdapters.Count == 0
                ? "No adapter with Train capability registered."
                : $"Available training adapters: {string.Join(", ", trainAdapters.Select(a => a.Id))}"
        };

        return Task.FromResult(estimate);
    }

    public async Task<TrainingJob> Status(string jobId, CancellationToken ct)
    {
        var trainAdapters = AdapterResolver.ResolveAll(registry, AiCapability.Train);

        foreach (var adapter in trainAdapters)
        {
            var runtime = adapter as ITrainingRuntime;
            if (runtime is null) continue;

            try
            {
                return await runtime.Status(jobId, ct);
            }
            catch
            {
                // This adapter doesn't know about this job — try next
            }
        }

        throw new InvalidOperationException(
            $"Job '{jobId}' not found in any registered training adapter.");
    }

    public async Task Cancel(string jobId, CancellationToken ct)
    {
        var trainAdapters = AdapterResolver.ResolveAll(registry, AiCapability.Train);

        foreach (var adapter in trainAdapters)
        {
            var runtime = adapter as ITrainingRuntime;
            if (runtime is null) continue;

            try
            {
                await runtime.Cancel(jobId, ct);
                return;
            }
            catch
            {
                // Try next adapter
            }
        }

        throw new InvalidOperationException(
            $"Job '{jobId}' not found in any registered training adapter.");
    }

    public async Task<TrainingJob> Resume(string jobId, string? checkpoint, CancellationToken ct)
    {
        var trainAdapters = AdapterResolver.ResolveAll(registry, AiCapability.Train);

        foreach (var adapter in trainAdapters)
        {
            var runtime = adapter as ITrainingRuntime;
            if (runtime is null) continue;

            try
            {
                return await runtime.Resume(jobId, checkpoint, ct);
            }
            catch
            {
                // Try next adapter
            }
        }

        throw new InvalidOperationException(
            $"Job '{jobId}' not found in any registered training adapter.");
    }

    public Task<IReadOnlyList<TrainingJob>> List(CancellationToken ct)
    {
        IReadOnlyList<TrainingJob> empty = [];
        return Task.FromResult(empty);
    }

    private static ITrainingRuntime ResolveTrainingRuntime(IAiAdapter adapter)
    {
        return adapter as ITrainingRuntime
            ?? throw new InvalidOperationException(
                $"Adapter '{adapter.Id}' has Train capability but does not implement ITrainingRuntime.");
    }
}
