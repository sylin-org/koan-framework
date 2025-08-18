using System;
using System.Collections.Generic;
using System.Reflection;
using Sora.Data.Abstractions.Annotations;

namespace Sora.Data.Relational.Schema;

public sealed record RelationalTable(string Name, string? Namespace, RelationalColumn PrimaryKey, IReadOnlyList<RelationalColumn> Columns, IReadOnlyList<RelationalIndex> Indexes);
public sealed record RelationalColumn(string Name, Type ClrType, bool IsNullable, bool IsJson, PropertyInfo SourceProperty);
public sealed record RelationalIndex(string? Name, IReadOnlyList<RelationalColumn> Columns, bool Unique, bool IsPrimaryKey);

public interface IRelationalSchemaModel
{
    RelationalTable Table { get; }
}

public interface IRelationalDialect
{
    string QuoteIdent(string ident);
    string MapType(Type clr, bool isJson);
    string CreateTable(RelationalTable table);
    IEnumerable<string> CreateIndexes(RelationalTable table);
}

public interface IRelationalSchemaReader
{
    // Optional in v1
}

public interface IRelationalSchemaSynchronizer
{
    void EnsureCreated(IRelationalDialect dialect, IRelationalSchemaModel model, System.Data.IDbConnection connection);
}
