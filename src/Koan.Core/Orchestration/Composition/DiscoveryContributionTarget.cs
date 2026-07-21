using System.ComponentModel;

namespace Koan.Core.Orchestration.Composition;

/// <summary>
/// Owner-bound structural target used by active Koan modules to declare dynamic discovery sources.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DiscoveryContributionTarget
{
    private readonly ServiceDiscoveryPlanBuilder _builder;
    private readonly string _owner;

    internal DiscoveryContributionTarget(ServiceDiscoveryPlanBuilder builder, string owner)
    {
        _builder = builder;
        _owner = owner;
    }

    /// <summary>
    /// Adds one stable source. Intent schemes are reserved exclusively by that source within the host plan.
    /// </summary>
    public void AddSource<TSource>(string id, params string[] intentSchemes)
        where TSource : class, IDiscoveryCandidateSource =>
        _builder.AddSource(_owner, id, typeof(TSource), intentSchemes);
}
