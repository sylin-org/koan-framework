using System.Collections;
using System.Text.Json.Serialization;
using Koan.Tagging.Json;

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
/// JSON shape: a TagCategory serialises as a JSON array of strings (e.g. <c>["ffxiv", "sims4"]</c>),
/// NOT as an object with a values property. See <see cref="TagCategoryJsonConverter"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(TagCategoryJsonConverter))]
public sealed class TagCategory : IEnumerable<string>
{
    private readonly HashSet<string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Add a tag. Empty / whitespace inputs are ignored. Returns this for chaining.</summary>
    public TagCategory Set(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag)) _values.Add(tag.Trim());
        return this;
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
