using System.ComponentModel;

namespace Koan.AI.Providers;

/// <summary>Owner-bound structural target used by AI provider modules.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AiProviderContributionTarget
{
    private readonly AiProviderPlanBuilder _builder;
    private readonly string _owner;

    internal AiProviderContributionTarget(AiProviderPlanBuilder builder, string owner)
    {
        _builder = builder;
        _owner = owner;
    }

    public void Add<TActivator>(string id)
        where TActivator : class, IAiProviderActivator =>
        _builder.Add(_owner, id, typeof(TActivator));
}
