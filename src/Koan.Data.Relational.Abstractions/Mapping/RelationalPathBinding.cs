using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Mapping;

/// <summary>One relational command's immutable reference to a compiled mapping binding.</summary>
public sealed record RelationalPathBinding(
    string BindingId,
    MappingPath LogicalPath,
    PhysicalPath PhysicalPath,
    MappingValueShape Shape,
    Type PhysicalType,
    string EncodingId,
    bool IsIdentity);
