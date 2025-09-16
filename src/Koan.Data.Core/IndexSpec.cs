using System.Reflection;

namespace Koan.Data.Core;

public sealed record IndexSpec(
    string? Name,
    IReadOnlyList<PropertyInfo> Properties,
    bool Unique,
    bool IsPrimaryKey,
    bool IsImplicit
);