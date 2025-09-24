namespace Koan.Web.Endpoints;

public enum EntityEndpointOperationKind
{
    None = 0,
    Collection,
    Query,
    GetNew,
    GetById,
    Upsert,
    UpsertMany,
    Delete,
    DeleteMany,
    DeleteByQuery,
    DeleteAll,
    Patch
}
