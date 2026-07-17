namespace Koan.Core.Semantics;

/// <summary>Normalized stable identity used by Koan's host composition kernel.</summary>
internal readonly struct SemanticId : IComparable<SemanticId>, IEquatable<SemanticId>
{
    public SemanticId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 256 || normalized.Any(static character =>
                !char.IsLetterOrDigit(character)
                && character is not '.' and not '-' and not '_' and not ':'))
        {
            throw new ArgumentException(
                "Semantic identities may contain only letters, numbers, '.', '-', '_', or ':' and must be at most 256 characters.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public int CompareTo(SemanticId other) => StringComparer.Ordinal.Compare(Value, other.Value);

    public bool Equals(SemanticId other) =>
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is SemanticId other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public static bool operator ==(SemanticId left, SemanticId right) => left.Equals(right);

    public static bool operator !=(SemanticId left, SemanticId right) => !left.Equals(right);

    public override string ToString() => Value;
}
