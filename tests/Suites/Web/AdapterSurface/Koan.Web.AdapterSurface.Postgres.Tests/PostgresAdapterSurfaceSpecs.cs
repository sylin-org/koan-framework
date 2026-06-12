using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Postgres.Tests;

public sealed class PostgresAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<PostgresAdapterFactory>
{
    public PostgresAdapterSurfaceSpecs(PostgresAdapterFactory factory) : base(factory) { }
}
