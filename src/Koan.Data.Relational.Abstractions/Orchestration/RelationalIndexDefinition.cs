namespace Koan.Data.Relational.Orchestration;

/// <summary>An index resolved onto compiled physical parts, each carrying its own shared encoding identity.</summary>
public sealed record RelationalIndexDefinition
{
    public RelationalIndexDefinition(
        string name,
        IEnumerable<RelationalIndexPart> parts,
        bool unique,
        bool primary,
        bool ttl,
        bool rewriteFree,
        bool keysSupported = true,
        bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parts);
        Name = name;
        Parts = Array.AsReadOnly(parts.ToArray());
        if (Parts.Count == 0) throw new ArgumentException("An index requires at least one physical part.", nameof(parts));
        Unique = unique;
        Primary = primary;
        Ttl = ttl;
        RewriteFree = rewriteFree;
        KeysSupported = keysSupported;
        Required = required;
    }

    public string Name { get; }
    public IReadOnlyList<RelationalIndexPart> Parts { get; }
    public bool Unique { get; }
    public bool Primary { get; }
    public bool Ttl { get; }
    public bool RewriteFree { get; }

    /// <summary>Whether every part of this index is a value the store can use as a key.</summary>
    public bool KeysSupported { get; }

    /// <summary>Whether the application declared that it depends on this index existing natively.</summary>
    public bool Required { get; }

    /// <summary>Whether any part reads inside a structured value, which is what makes this an expression index.</summary>
    public bool IsExpression => Parts.Any(static part => part.Path.IsNested);
}
