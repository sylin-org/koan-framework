using System.Threading.Tasks;
namespace Koan.Web.Endpoints;

public interface IEntityEndpointService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    Task<EntityCollectionResult<TEntity>> GetCollectionAsync(EntityCollectionRequest request);
    Task<EntityCollectionResult<TEntity>> QueryAsync(EntityQueryRequest request);
    Task<EntityModelResult<TEntity>> GetNewAsync(EntityGetNewRequest request);
    Task<EntityModelResult<TEntity>> GetByIdAsync(EntityGetByIdRequest<TKey> request);
    Task<EntityModelResult<TEntity>> UpsertAsync(EntityUpsertRequest<TEntity> request);
    Task<EntityEndpointResult> UpsertManyAsync(EntityUpsertManyRequest<TEntity> request);
    Task<EntityModelResult<TEntity>> DeleteAsync(EntityDeleteRequest<TKey> request);
    Task<EntityEndpointResult> DeleteManyAsync(EntityDeleteManyRequest<TKey> request);
    Task<EntityEndpointResult> DeleteByQueryAsync(EntityDeleteByQueryRequest request);
    Task<EntityEndpointResult> DeleteAllAsync(EntityDeleteAllRequest request);
    Task<EntityModelResult<TEntity>> PatchAsync(EntityPatchRequest<TEntity, TKey> request);
}

