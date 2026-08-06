namespace Koan.Data.Abstractions;

/// <summary>The complete single or composite identity decision for one map.</summary>
public sealed record MappingIdentityDescriptor
{
    public MappingIdentityDescriptor(MappingPath logicalPath, Type logicalType, IEnumerable<MappingBindingDescriptor> parts)
    {
        ArgumentNullException.ThrowIfNull(logicalPath);
        ArgumentNullException.ThrowIfNull(logicalType);
        ArgumentNullException.ThrowIfNull(parts);
        var copy = parts.ToArray();
        if (copy.Length == 0) throw new ArgumentException("Identity requires at least one physical part.", nameof(parts));
        LogicalPath = logicalPath;
        LogicalType = logicalType;
        Parts = Array.AsReadOnly(copy);
    }

    public MappingPath LogicalPath { get; }
    public Type LogicalType { get; }
    public IReadOnlyList<MappingBindingDescriptor> Parts { get; }
    public bool IsComposite => Parts.Count > 1 || !Parts[0].LogicalPath.Equals(LogicalPath);
    public bool IsGenerated => Parts.Any(static part => part.Generation == MappingGeneration.Provider);
}
