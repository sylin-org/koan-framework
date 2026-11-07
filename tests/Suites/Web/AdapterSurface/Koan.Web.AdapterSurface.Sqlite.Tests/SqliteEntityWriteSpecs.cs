using Koan.Web.AdapterSurface.TestKit.EntityWrites;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

/// <summary>WEB-0086 on SQLite, which declares held transactions: a refused write leaves nothing.</summary>
public sealed class SqliteEntityWriteSpecs(SqliteAdapterFactory factory) : EntityWriteSpecsBase<SqliteAdapterFactory>(factory)
{
    protected override bool Holds => true;
}
