using System.Reflection;

namespace Sora.Data.Core;

public sealed record ProjectedProperty(
    PropertyInfo Property,
    string ColumnName,
    bool IsEnum,
    bool IsIndexed
);