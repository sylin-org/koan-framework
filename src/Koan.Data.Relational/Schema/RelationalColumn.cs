using System.Reflection;

namespace Koan.Data.Relational.Schema;

public sealed record RelationalColumn(string Name, Type ClrType, bool IsNullable, bool IsJson, PropertyInfo SourceProperty);