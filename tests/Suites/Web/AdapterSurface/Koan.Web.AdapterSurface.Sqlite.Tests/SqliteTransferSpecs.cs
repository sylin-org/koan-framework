using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

public sealed class SqliteTransferSpecs : AdapterTransferSpecsBase<SqliteAdapterFactory>
{
    public SqliteTransferSpecs(SqliteAdapterFactory factory) : base(factory) { }
}
