using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

public sealed class SqlitePartitionSpecs : AdapterPartitionSpecsBase<SqliteAdapterFactory>
{
    public SqlitePartitionSpecs(SqliteAdapterFactory factory) : base(factory) { }
}
