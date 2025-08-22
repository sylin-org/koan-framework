using System.Reflection;

namespace Sora.Data.Relational.Schema;

public sealed record RelationalColumn(string Name, Type ClrType, bool IsNullable, bool IsJson, PropertyInfo SourceProperty);