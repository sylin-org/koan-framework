using System.Threading.Tasks;
namespace Koan.Web.Endpoints;

public interface IEntityEndpointService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    Task<EntityCollectionResult<TEntity>> GetCollection(EntityCollectionRequest request);
    Task<EntityCollectionResult<TEntity>> Query(EntityQueryRequest request);
    Task<EntityModelResult<TEntity>> GetNew(EntityGetNewRequest request);
    Task<EntityModelResult<TEntity>> GetById(EntityGetByIdRequest<TKey> request);
    Task<EntityModelResult<TEntity>> Upsert(EntityUpsertRequest<TEntity> request);
    Task<EntityEndpointResult> UpsertMany(EntityUpsertManyRequest<TEntity> request);
    Task<EntityModelResult<TEntity>> Delete(EntityDeleteRequest<TKey> request);
    Task<EntityEndpointResult> DeleteMany(EntityDeleteManyRequest<TKey> request);
    Task<EntityEndpointResult> DeleteByQuery(EntityDeleteByQueryRequest request);
    Task<EntityEndpointResult> DeleteAll(EntityDeleteAllRequest request);
    Task<EntityModelResult<TEntity>> Patch(EntityPatchRequest<TEntity, TKey> request);
}

