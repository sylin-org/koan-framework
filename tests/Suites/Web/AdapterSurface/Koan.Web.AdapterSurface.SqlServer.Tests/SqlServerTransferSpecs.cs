using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.SqlServer.Tests;

public sealed class SqlServerTransferSpecs : AdapterTransferSpecsBase<SqlServerAdapterFactory>
{
    public SqlServerTransferSpecs(SqlServerAdapterFactory factory) : base(factory) { }
}
