namespace Koan.Data.Core.Querying;

/// <summary>
/// Where a list in a <see cref="Keyset{TEntity}"/> order continues (DATA-0170): the values of the order's members on the last row a
/// page held, the identity last. It names a row's place, not a position, so a row inserted or removed ahead of it moves nothing.
/// <see cref="Keyset{TEntity}.Write"/> and <see cref="Keyset{TEntity}.Read"/> carry it as text; a key that leaves the server travels
/// sealed (<c>SealedCursor</c> in Koan.Web).
/// </summary>
public sealed record KeysetKey
{
    internal KeysetKey(IReadOnlyList<object?> values) => Values = values;

    /// <summary>The order's members' values, in the order's sequence, the identity last.</summary>
    public IReadOnlyList<object?> Values { get; }

    public bool Equals(KeysetKey? other) => other is not null && Values.SequenceEqual(other.Values);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Values) hash.Add(value);
        return hash.ToHashCode();
    }
}
