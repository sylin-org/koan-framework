using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Semantics.Contributions;

/// <summary>
/// Framework-facing entry point for compiling one concern-owned contribution target during host composition.
/// Ordinary applications should use <c>AddKoan()</c>; functional pillars use this seam to turn module declarations
/// into one immutable runtime plan.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SemanticContributionPlans
{
    public static void Schedule<TTarget, TPlan>(
        IServiceCollection services,
        Func<string, TTarget> targetForOwner,
        Func<TPlan> freeze,
        Action<IServiceCollection, TPlan> commit)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(targetForOwner);
        SemanticCompositionSession.GetOrCreate(services)
            .ScheduleContributions(owner => targetForOwner(owner.Value), freeze, commit);
    }
}
