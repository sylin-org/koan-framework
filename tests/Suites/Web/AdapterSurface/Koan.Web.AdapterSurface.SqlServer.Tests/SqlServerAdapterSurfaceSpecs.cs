using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.SqlServer.Tests;

public sealed class SqlServerAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<SqlServerAdapterFactory>
{
    public SqlServerAdapterSurfaceSpecs(SqlServerAdapterFactory factory) : base(factory) { }
}
