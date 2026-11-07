using Koan.Web.AdapterSurface.TestKit.Declared;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

/// <summary>WEB-0087 on SQLite.</summary>
public sealed class SqliteDeclaredControllerSpecs(SqliteAdapterFactory factory) : DeclaredControllerSpecsBase<SqliteAdapterFactory>(factory);
