using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Semantics.Contributions;

/// <summary>
/// A fully compiled and frozen target whose host registrations remain staged until every target compiles.
/// </summary>
internal sealed class SemanticPendingContributionResult
{
    private readonly Action<IServiceCollection> _commit;
    private int _committed;

    public SemanticPendingContributionResult(
        SemanticContributionTargetSnapshot snapshot,
        Action<IServiceCollection> commit)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(commit);

        Snapshot = snapshot;
        _commit = commit;
    }

    public SemanticContributionTargetSnapshot Snapshot { get; }

    public void Commit(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (Interlocked.Exchange(ref _committed, 1) != 0)
        {
            throw new InvalidOperationException(
                $"The semantic contribution target '{Snapshot.TargetType.FullName}' was already committed.");
        }

        _commit(services);
    }
}
