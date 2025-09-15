using System.Reflection;

namespace Koan.Data.Core;

public sealed record ProjectedProperty(
    PropertyInfo Property,
    string ColumnName,
    bool IsEnum,
    bool IsIndexed
);