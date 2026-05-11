namespace Koan.Tagging;

/// <summary>
/// One side of a <see cref="TagSet"/> — either the public or private half — holding tag
/// values grouped into open-ended named categories. Categories are auto-created on first
/// access via the indexer, so <c>scope["new-category"].Set("x")</c> works without explicit
/// initialisation.
/// </summary>
/// <remarks>
/// <para>
/// Category keys are case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>) so
/// <c>scope["Game"]</c> and <c>scope["game"]</c> reach the same bucket. Stored keys preserve
/// the first-write casing.
/// </para>
/// <para>
/// JSON shape: a TagScope serialises as a JSON object where each property name is a category
/// name and the value is an array of tag strings:
/// <code>
/// { "game": ["ffxiv"], "technique": ["dof", "clarity"] }
/// </code>
/// Empty categories are stripped from output. See <see cref="TagScopeJsonConverter"/>.
/// </para>
/// </remarks>
public sealed class TagScope
{
    /// <summary>
    /// Categories keyed by name. Exposed as a settable <see cref="Dictionary{TKey,TValue}"/> so MongoDB
    /// BSON auto-mapping and Koan's polymorphic JSON serialisation can round-trip the data. Consumers
    /// should access categories via the <see cref="this[string]"/> indexer rather than touching this
    /// directly — the indexer handles case-insensitive lookup and auto-create-on-miss semantics.
    /// </summary>
    /// <remarks>
    /// The setter accepts <see langword="null"/> and normalises to an empty dictionary so partial
    /// deserialisation paths (BSON skip-during-restore, etc.) don't leave the type in a broken state.
    /// </remarks>
    public Dictionary<string, TagCategory> Categories
    {
        get => _categories;
        set => _categories = value is null
            ? new Dictionary<string, TagCategory>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TagCategory>(value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, TagCategory> _categories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Access (auto-create on miss) the category with the given name. The returned
    /// <see cref="TagCategory"/> is the live instance; chained mutations persist.
    /// </summary>
    public TagCategory this[string category]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            if (!_categories.TryGetValue(category, out var c))
            {
                c = new TagCategory();
                _categories[category] = c;
            }
            return c;
        }
    }

    /// <summary>True when any category in this scope contains the tag.</summary>
    public bool Contains(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        foreach (var c in _categories.Values)
        {
            if (c.Contains(tag)) return true;
        }
        return false;
    }

    /// <summary>
    /// Find the category name (if any) that contains the tag. Used by <see cref="TagSet.Find(string)"/>
    /// to answer "where is this tag?" queries.
    /// </summary>
    public string? Locate(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        foreach (var (name, cat) in _categories)
        {
            if (cat.Contains(tag)) return name;
        }
        return null;
    }

    /// <summary>
    /// Flat, de-duplicated list of every tag across every category in this scope.
    /// Computed on access (cheap; not cached). Exposed publicly as a serialisable convenience.
    /// </summary>
    public IReadOnlyList<string> Flat
    {
        get
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in _categories.Values)
            {
                foreach (var t in cat) set.Add(t);
            }
            return set.ToArray();
        }
    }

    /// <summary>True when this scope holds no tags in any category.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var c in _categories.Values)
            {
                if (c.Count > 0) return false;
            }
            return true;
        }
    }
}
