using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>The entity's lexical declaration resolved onto the same compiled bindings its reads and writes use.</summary>
public sealed class MappingLexicalPlan
{
    internal MappingLexicalPlan(string name, IEnumerable<MappingBindingPlan> bindings, string language, string planId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        var copy = bindings.ToArray();
        if (copy.Length == 0) throw new ArgumentException("A lexical index requires at least one binding.", nameof(bindings));
        Name = name;
        Bindings = Array.AsReadOnly(copy);
        Language = language;
        Receipt = new MappingReceipt(planId, MappingConsumer.Index, copy.Select(static binding => binding.Id), nativeProofRequired: true);
    }

    /// <summary>The name the store builds the index under, derived from the container so it is unique per table.</summary>
    public string Name { get; }

    /// <summary>The indexed string values, in declaration order.</summary>
    public IReadOnlyList<MappingBindingPlan> Bindings { get; }

    /// <summary>The language the store tokenizes by.</summary>
    public string Language { get; }

    /// <summary>The declared property names, in declaration order.</summary>
    public IReadOnlyList<string> Fields => Bindings.Select(static binding => binding.LogicalPath.Leaf).ToArray();

    public MappingReceipt Receipt { get; }
}
