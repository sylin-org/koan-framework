using System.Collections;

namespace Koan.Tagging;

/// <summary>
/// A named bucket of canonical tag strings within a <see cref="TagScope"/>. Set semantics
/// (no duplicates, case-insensitive comparison via <see cref="StringComparer.OrdinalIgnoreCase"/>).
/// Fluent API designed for the "set / unset / toggle" idiom on tagged entities:
/// <code>
/// scope["game"].Set("ffxiv").Set("sims4").Unset("removed");
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Stored values are <see cref="string.Trim()"/>-trimmed at write time. Case normalisation is
/// deliberately NOT applied — <c>"FFXIV"</c> and <c>"ffxiv"</c> compare equal but serialise as
/// stored. Canonicalisation (synonym resolution) is the caller's responsibility, typically via
/// the <see cref="Tag"/> entity's <see cref="Tag.ParentOf"/> registry before <c>Set</c> lands.
/// </para>
/// <para>
/// Serialisation: under Koan's polymorphic JSON layer, all public properties serialise. A
/// TagCategory emits its <see cref="Values"/> set as a JSON array. The <see cref="IEnumerable{T}"/>
/// implementation is for fluent iteration in C# code and for MongoDB.Bson's collection-shaped
/// auto-mapping; the <see cref="Add(string)"/> seam supports BSON deserialisation.
/// </para>
/// </remarks>
public sealed class TagCategory : IEnumerable<string>
{
    /// <summary>
    /// The underlying tag values. Exposed as a settable <see cref="HashSet{T}"/> so Koan's
    /// polymorphic serialiser, MongoDB BSON auto-mapping, and any other reflection-driven
    /// serialiser can round-trip the data. Consumers should mutate via the fluent
    /// <see cref="Set(string)"/> / <see cref="Unset(string)"/> API rather than touching this
    /// directly.
    /// </summary>
    /// <remarks>
    /// The setter accepts <see langword="null"/> and normalises to an empty set so partial
    /// deserialisation paths don't leave the type in a broken state.
    /// </remarks>
    public HashSet<string> Values
    {
        get => _values;
        set => _values = value is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Add a tag. Empty / whitespace inputs are ignored. Returns this for chaining.</summary>
    public TagCategory Set(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag)) _values.Add(tag.Trim());
        return this;
    }

    /// <summary>
    /// Add a tag — non-fluent, void return so MongoDB.Bson's collection-initialiser deserialiser
    /// can populate this category from a JSON array. Consumers should prefer <see cref="Set(string)"/>
    /// (same semantics, fluent return).
    /// </summary>
    public void Add(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag)) _values.Add(tag.Trim());
    }

    /// <summary>Add multiple tags. Returns this for chaining.</summary>
    public TagCategory Set(IEnumerable<string> tags)
    {
        if (tags is not null)
        {
            foreach (var t in tags) Set(t);
        }
        return this;
    }

    /// <summary>Remove a tag if present. Returns this for chaining.</summary>
    public TagCategory Unset(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag)) _values.Remove(tag.Trim());
        return this;
    }

    /// <summary>Remove multiple tags. Returns this for chaining.</summary>
    public TagCategory Unset(IEnumerable<string> tags)
    {
        if (tags is not null)
        {
            foreach (var t in tags) Unset(t);
        }
        return this;
    }

    /// <summary>Add the tag if absent; remove it if present. Returns this for chaining.</summary>
    public TagCategory Toggle(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return this;
        var n = tag.Trim();
        if (!_values.Add(n)) _values.Remove(n);
        return this;
    }

    /// <summary>Replace the entire set with the supplied tags (clears then sets). Returns this for chaining.</summary>
    public TagCategory Replace(IEnumerable<string> tags)
    {
        _values.Clear();
        return Set(tags);
    }

    /// <summary>Empty the category. Returns this for chaining.</summary>
    public TagCategory Clear()
    {
        _values.Clear();
        return this;
    }

    /// <summary>True when the category contains the tag (case-insensitive).</summary>
    public bool Contains(string tag)
        => !string.IsNullOrWhiteSpace(tag) && _values.Contains(tag.Trim());

    /// <summary>Number of tags in this category.</summary>
    public int Count => _values.Count;

    public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
