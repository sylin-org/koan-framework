using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Enrichment
{
    /// <summary>
    /// Extension methods for Entity<> to get relationship graphs.
    /// </summary>
    public static class EntityGraphExtensions
    {
        public static async Task<RelationshipGraph<TEntity>> GetGraphAsync<TEntity>(this TEntity entity, CancellationToken ct = default)
        {
            // TODO: Implement graph construction logic using metadata/reflection
            var parents = new Dictionary<string, object?>();
            var children = new Dictionary<string, object?>();

            // Example: populate parents/children by inspecting attributes or metadata
            // For demonstration, these are empty

            await Task.CompletedTask;
            return new RelationshipGraph<TEntity>(entity, parents, children);
        }
    }

    /// <summary>
    /// DTO for relationship graph
    /// </summary>
    public class RelationshipGraph<TEntity>
    {
        public TEntity Entity { get; }
        /// <summary>
        /// Parents mapped by source property (e.g., AuthorId, ModeratorId)
        /// </summary>
        public Dictionary<string, object?> Parents { get; }

        /// <summary>
        /// Children mapped by source property (e.g., CommentIds, TagIds)
        /// </summary>
        public Dictionary<string, object?> Children { get; }

        public RelationshipGraph(TEntity entity,
            Dictionary<string, object?>? parents = null,
            Dictionary<string, object?>? children = null)
        {
            Entity = entity;
            Parents = parents ?? new();
            Children = children ?? new();
        }
    }
}
