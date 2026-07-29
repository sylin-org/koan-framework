namespace Koan.Data.Abstractions;

/// <summary>An immutable provider-neutral physical root name and optional structured subpath.</summary>
public sealed class PhysicalPath : IEquatable<PhysicalPath>
{
    private readonly string[] _segments;

    public PhysicalPath(string name, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Physical path segments cannot be blank.", nameof(segments));
        Name = name.Trim();
        _segments = segments.Select(static segment => segment.Trim()).ToArray();
        Segments = Array.AsReadOnly(_segments);
    }

    public string Name { get; }
    public IReadOnlyList<string> Segments { get; }
    public bool IsNested => _segments.Length > 0;

    public bool IsPrefixOf(PhysicalPath candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!string.Equals(Name, candidate.Name, StringComparison.Ordinal) ||
            _segments.Length > candidate._segments.Length) return false;
        for (var i = 0; i < _segments.Length; i++)
            if (!string.Equals(_segments[i], candidate._segments[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public bool Equals(PhysicalPath? other) =>
        other is not null &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        _segments.AsSpan().SequenceEqual(other._segments);

    public override bool Equals(object? obj) => obj is PhysicalPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        foreach (var segment in _segments) hash.Add(segment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        _segments.Length == 0 ? Name : $"{Name}/{string.Join('/', _segments)}";
}
