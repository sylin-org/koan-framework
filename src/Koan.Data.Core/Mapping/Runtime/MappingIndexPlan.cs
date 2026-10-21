using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>An index declaration resolved onto the same compiled bindings used by reads and writes.</summary>
public sealed class MappingIndexPlan
{
    internal MappingIndexPlan(
        string name,
        IEnumerable<MappingBindingPlan> bindings,
        bool unique,
        bool primary,
        bool ttl,
        string planId,
        bool required = false,
        bool nameDeclared = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var copy = bindings.ToArray();
        if (copy.Length == 0) throw new ArgumentException("An index requires at least one binding.", nameof(bindings));
        Name = name;
        Bindings = Array.AsReadOnly(copy);
        Unique = unique;
        Primary = primary;
        Ttl = ttl;
        Required = required;
        NameDeclared = nameDeclared;
        Receipt = new MappingReceipt(planId, MappingConsumer.Index, copy.Select(static binding => binding.Id), nativeProofRequired: true);
    }

    public string Name { get; }
    public IReadOnlyList<MappingBindingPlan> Bindings { get; }
    public bool Unique { get; }
    public bool Primary { get; }
    public bool Ttl { get; }

    /// <summary>Whether the application declared that it depends on this index existing natively.</summary>
    public bool Required { get; }

    /// <summary>
    /// Whether the application chose <see cref="Name"/> (<c>[Index(Name = ...)]</c>) rather than Koan deriving it
    /// from the container and the indexed members. A declared name is the application's own and is reused as written;
    /// a derived one already carries its container.
    /// </summary>
    public bool NameDeclared { get; }

    public MappingReceipt Receipt { get; }
}
