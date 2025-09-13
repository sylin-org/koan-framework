using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Enrichment
{
    /// <summary>
    /// Enriches an entity with related data (parents, children, etc.).
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    public interface IEntityEnricher<TEntity>
    {
        /// <summary>
        /// Enriches the entity with relationships and additional data.
        /// </summary>
        /// <param name="entity">The entity to enrich</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Enriched entity</returns>
        Task<TEntity> EnrichAsync(TEntity entity, CancellationToken ct = default);
    }
}
