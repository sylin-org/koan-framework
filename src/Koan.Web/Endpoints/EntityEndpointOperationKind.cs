namespace Koan.Web.Endpoints;

public enum EntityEndpointOperationKind
{
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
