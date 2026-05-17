using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

public sealed class SqliteAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<SqliteAdapterFactory>
{
    public SqliteAdapterSurfaceSpecs(SqliteAdapterFactory factory) : base(factory) { }
}
