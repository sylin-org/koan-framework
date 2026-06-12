using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Postgres.Tests;

public sealed class PostgresPartitionSpecs : AdapterPartitionSpecsBase<PostgresAdapterFactory>
{
    public PostgresPartitionSpecs(PostgresAdapterFactory factory) : base(factory) { }
}
