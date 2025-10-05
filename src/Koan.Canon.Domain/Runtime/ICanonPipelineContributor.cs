using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Represents a unit of work executed within a canonization pipeline phase.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
public interface ICanonPipelineContributor<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    /// <summary>
    /// Pipeline phase that the contributor belongs to.
    /// </summary>
    CanonPipelinePhase Phase { get; }

    /// <summary>
    /// Executes the contributor logic.
    /// </summary>
    ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<TModel> context, CancellationToken cancellationToken);
}
