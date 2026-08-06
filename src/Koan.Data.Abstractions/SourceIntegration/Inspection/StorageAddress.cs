namespace Koan.Data.Abstractions;

/// <summary>Provider-neutral source-relative address: zero or more namespace segments plus one local name.</summary>
public sealed record StorageAddress
{
    private StorageAddress(IReadOnlyList<string> namespaceSegments, string name)
    {
        Namespace = namespaceSegments;
        Name = name;
    }

    public IReadOnlyList<string> Namespace { get; }
    public string Name { get; }

    public static StorageAddress From(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A storage address requires one or more non-blank segments.", nameof(segments));
        return new StorageAddress(
            Array.AsReadOnly(segments[..^1].ToArray()),
            segments[^1]);
    }

    public override string ToString() => string.Join("/", Namespace.Append(Name));
}
