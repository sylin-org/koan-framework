namespace Koan.Data.Abstractions;

/// <summary>An immutable, exact-case logical property path. Empty means the aggregate root.</summary>
public sealed class MappingPath : IEquatable<MappingPath>
{
    private readonly string[] _segments;

    private MappingPath(string[] segments)
    {
        _segments = segments;
        Segments = Array.AsReadOnly(_segments);
    }

    public static MappingPath Root { get; } = new([]);

    public IReadOnlyList<string> Segments { get; }
    public bool IsRoot => _segments.Length == 0;
    public string Leaf => IsRoot ? string.Empty : _segments[^1];

    public static MappingPath Of(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Logical path segments cannot be blank.", nameof(segments));
        return segments.Length == 0
            ? Root
            : new MappingPath(segments.Select(static segment => segment.Trim()).ToArray());
    }

    public MappingPath Append(MappingPath suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        return suffix.IsRoot ? this : Of(_segments.Concat(suffix._segments).ToArray());
    }

    public bool IsPrefixOf(MappingPath candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (_segments.Length > candidate._segments.Length) return false;
        for (var i = 0; i < _segments.Length; i++)
            if (!string.Equals(_segments[i], candidate._segments[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public bool Equals(MappingPath? other) =>
        other is not null && _segments.AsSpan().SequenceEqual(other._segments);

    public override bool Equals(object? obj) => obj is MappingPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _segments) hash.Add(segment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => IsRoot ? "$" : string.Join('.', _segments);
}
