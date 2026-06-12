using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.SqlServer.Tests;

public sealed class SqlServerPartitionSpecs : AdapterPartitionSpecsBase<SqlServerAdapterFactory>
{
    public SqlServerPartitionSpecs(SqlServerAdapterFactory factory) : base(factory) { }
}
