using Koan.AI.Contracts.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Compute;

/// <summary>
/// Static facade for the Koan compute fabric. Discover GPUs, resolve workload
/// placement, and check fleet readiness — all without injecting services.
///
/// <code>
/// var gpu = await Compute.Available();
/// var resolution = await Compute.Resolve(Compute.Require(minVram: 8));
/// bool ready = await Compute.Check(new ReadinessSpec { ... });
/// </code>
/// </summary>
public static class Compute
{
    // ── Discovery ──

    /// <summary>Returns the best available compute resource.</summary>
    public static async Task<ComputeResource?> Available(CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Available(ct);
    }

    /// <summary>Returns all known compute resources across local and network.</summary>
    public static async Task<IReadOnlyList<ComputeResource>> Fleet(CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Fleet(ct);
    }

    // ── Resolution ──

    /// <summary>Resolves the best compute target for a workload requirement.</summary>
    public static async Task<ComputeResolution> Resolve(
        ComputeRequirement workload, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Resolve(workload, ct);
    }

    // ── Requirement Builders ──

    /// <summary>Build a compute requirement with minimum constraints.</summary>
    public static ComputeRequirement Require(
        long? minVram = null,
        Accelerator? accelerator = null,
        ComputeLocation? location = null)
    {
        return new ComputeRequirement(
            Accelerator: accelerator ?? Accelerator.Any,
            MinVramBytes: minVram is not null ? minVram.Value * 1024 * 1024 * 1024 : null,
            Location: location);
    }

    /// <summary>Build a compute requirement with preferences (same shape, different semantics for callers).</summary>
    public static ComputeRequirement Prefer(
        long? minVram = null,
        Accelerator? accelerator = null,
        ComputeLocation? location = null)
    {
        return new ComputeRequirement(
            Accelerator: accelerator ?? Accelerator.Any,
            MinVramBytes: minVram is not null ? minVram.Value * 1024 * 1024 * 1024 : null,
            Location: location);
    }

    // ── Readiness ──

    /// <summary>Checks whether the fleet satisfies a readiness specification.</summary>
    public static async Task<bool> Check(ReadinessSpec readinessSpec, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Check(readinessSpec, ct);
    }

    // ── Internal ──

    private static IComputeService ResolveService()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Compute fabric not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Compute.*");

        return provider.GetRequiredService<IComputeService>();
    }
}
