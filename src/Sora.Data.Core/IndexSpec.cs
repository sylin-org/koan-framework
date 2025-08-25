using System.Reflection;

namespace Sora.Data.Core;

public sealed record IndexSpec(
    string? Name,
    IReadOnlyList<PropertyInfo> Properties,
    bool Unique,
    bool IsPrimaryKey,
    bool IsImplicit
);