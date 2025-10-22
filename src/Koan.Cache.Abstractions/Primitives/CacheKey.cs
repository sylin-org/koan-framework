using System;
using System.Diagnostics.CodeAnalysis;

namespace Koan.Cache.Abstractions.Primitives;

public readonly record struct CacheKey
{
    public CacheKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Cache key cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static implicit operator string(CacheKey key) => key.Value;

    public static implicit operator CacheKey(string value) => new(value);

    public override string ToString() => Value;

    public bool Equals(CacheKey other)
        => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public bool Matches(string value)
        => string.Equals(Value, value, StringComparison.Ordinal);

    public CacheKey WithPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Prefix cannot be null or whitespace.", nameof(prefix));
        }

        return new CacheKey(prefix + Value);
    }

    public CacheKey WithSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new ArgumentException("Suffix cannot be null or whitespace.", nameof(suffix));
        }

        return new CacheKey(Value + suffix);
    }

    public bool StartsWith(ReadOnlySpan<char> prefix)
        => Value.AsSpan().StartsWith(prefix, StringComparison.Ordinal);

    public bool EndsWith(ReadOnlySpan<char> suffix)
        => Value.AsSpan().EndsWith(suffix, StringComparison.Ordinal);

    public static CacheKey Concat(CacheKey left, CacheKey right)
        => new(left.Value + right.Value);

    public static bool TryParse(string? value, [NotNullWhen(true)] out CacheKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        key = new CacheKey(value.Trim());
        return true;
    }
}
