using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Orchestration;

/// <summary>An index definition resolved onto compiled physical paths and shared encoding identities.</summary>
public sealed record RelationalIndexDefinition
{
    public RelationalIndexDefinition(
        string name,
        IEnumerable<PhysicalPath> parts,
        IEnumerable<string> encodingIds,
        bool unique,
        bool primary,
        bool ttl,
        bool rewriteFree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(encodingIds);
        Name = name;
        Parts = Array.AsReadOnly(parts.ToArray());
        EncodingIds = Array.AsReadOnly(encodingIds.ToArray());
        if (Parts.Count == 0 || Parts.Count != EncodingIds.Count)
            throw new ArgumentException("An index requires one encoding identity for each physical part.");
        Unique = unique;
        Primary = primary;
        Ttl = ttl;
        RewriteFree = rewriteFree;
    }

    public string Name { get; }
    public IReadOnlyList<PhysicalPath> Parts { get; }
    public IReadOnlyList<string> EncodingIds { get; }
    public bool Unique { get; }
    public bool Primary { get; }
    public bool Ttl { get; }
    public bool RewriteFree { get; }
}
