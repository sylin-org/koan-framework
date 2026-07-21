using System.Collections.Immutable;
using Koan.Core.Infrastructure;

namespace Koan.Core.Semantics.Contributions;

/// <summary>Dispatches exact generated bindings over one host's retained modules in constitution order.</summary>
internal static class SemanticContributionCompiler
{
    public static SemanticContributionTargetSnapshot Compile<TTarget>(
        SemanticHostConstitution constitution,
        SemanticModuleRuntime modules,
        Func<SemanticId, TTarget> targetForOwner)
    {
        ArgumentNullException.ThrowIfNull(constitution);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(targetForOwner);

        var targetType = typeof(TTarget);
        var appliedOwners = ImmutableArray.CreateBuilder<SemanticId>();
        foreach (var descriptor in constitution.ActiveDescriptors)
        {
            if (!descriptor.TryGetContribution(targetType, out var binding)) continue;

            try
            {
                var target = targetForOwner(descriptor.Id);
                if (target is null)
                {
                    throw new InvalidOperationException("The contribution target factory returned null.");
                }

                binding.Apply(modules.GetModule(descriptor.Id), target);
                appliedOwners.Add(descriptor.Id);
            }
            catch (Exception exception) when (exception is not SemanticModuleRuntime.SemanticRuntimeException)
            {
                throw new SemanticModuleRuntime.SemanticRuntimeException(
                    new SemanticProblem(
                        descriptor.Id,
                        Constants.Semantics.Reasons.ModuleContributionFailed,
                        $"Fix the contribution from '{descriptor.ImplementationType.FullName}' to '{targetType.FullName}', or remove the capability reference."),
                    exception);
            }
        }

        var decisionsByOwner = constitution.Decisions.ToDictionary(static decision => decision.Component);
        var declaredDecisions = constitution.ActiveDescriptors
            .Concat(constitution.InactiveDescriptors)
            .Where(descriptor => descriptor.TryGetContribution(targetType, out _))
            .Select(descriptor => decisionsByOwner[descriptor.Id]);

        return new SemanticContributionTargetSnapshot(
            targetType,
            appliedOwners,
            declaredDecisions,
            constitution.Problems);
    }
}
