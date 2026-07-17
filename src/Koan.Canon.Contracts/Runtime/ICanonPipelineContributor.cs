using System.Threading;
using System.Threading.Tasks;
using Koan.Core;

namespace Koan.Canon;

/// <summary>
/// Non-generic discovery marker for Canon pipeline contributors.
/// </summary>
[KoanDiscoverable]
public interface ICanonPipelineContributor
{
}

/// <summary>
/// Represents a unit of work executed within a canonization pipeline phase.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
public interface ICanonPipelineContributor<TModel> : ICanonPipelineContributor
    where TModel : CanonEntity<TModel>, new()
{
    /// <summary>
    /// Pipeline phase that the contributor belongs to.
    /// </summary>
    CanonPipelinePhase Phase { get; }

    /// <summary>
    /// Relative order within <see cref="Phase"/>. Lower values run first; the default is zero.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Executes the contributor logic.
    /// </summary>
    ValueTask<CanonizationEvent?> Execute(CanonPipelineContext<TModel> context, CancellationToken cancellationToken);
}
