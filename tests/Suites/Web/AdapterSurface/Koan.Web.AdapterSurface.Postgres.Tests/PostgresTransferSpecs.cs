using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Postgres.Tests;

public sealed class PostgresTransferSpecs : AdapterTransferSpecsBase<PostgresAdapterFactory>
{
    public PostgresTransferSpecs(PostgresAdapterFactory factory) : base(factory) { }
}
