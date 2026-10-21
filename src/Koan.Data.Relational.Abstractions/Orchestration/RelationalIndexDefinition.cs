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
        bool required = false,
        bool nameDeclared = false)
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
        NameDeclared = nameDeclared;
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

    /// <summary>Whether the application chose <see cref="Name"/> (<c>[Index(Name = ...)]</c>) rather than Koan deriving it.</summary>
    public bool NameDeclared { get; }

    /// <summary>
    /// For a unique index that leads with the entity's row-scoped managed fields (the tenant), the same declaration
    /// without them: the global index an earlier Koan built under <see cref="Unscoped"/>'s name (DATA-0150). Koan never
    /// builds it again and never drops it; it only looks for it, to tell the operator it can be dropped. Null for an
    /// index that is not scoped.
    /// </summary>
    public RelationalIndexDefinition? Unscoped { get; init; }

    /// <summary>Whether any part reads inside a structured value, which is what makes this an expression index.</summary>
    public bool IsExpression => Parts.Any(static part => part.Path.IsNested);
}
