using Sora.Data.Relational.Orchestration;

namespace Sora.Data.SqlServer;

internal sealed class MsSqlStoreFeatures : IRelationalStoreFeatures
{
    public bool SupportsJsonFunctions => true; // JSON_VALUE is available since SQL Server 2016
    public bool SupportsPersistedComputedColumns => true;
    public bool SupportsIndexesOnComputedColumns => true;
}