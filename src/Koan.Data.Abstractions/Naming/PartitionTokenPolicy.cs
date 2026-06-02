using System.Text;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Declarative rules an adapter announces for turning a partition value into an identifier-safe token.
/// The framework (<see cref="StorageNameGenerator"/>) applies these uniformly — adapters no longer
/// hand-roll per-store sanitizers (the old SanitizeForSqlite/GraphQL/Qdrant/Milvus variants).
/// </summary>
public sealed record PartitionTokenPolicy
{
    /// <summary>Format used when the partition parses as a GUID. "N" = 32 hex (no hyphens); "D" = hyphenated.</summary>
    public string GuidFormat { get; init; } = "N";

    /// <summary>Non-alphanumeric characters kept verbatim; everything else becomes <see cref="Replacement"/>.</summary>
    public string AllowedExtraChars { get; init; } = "-._";

    /// <summary>Replacement character for disallowed characters.</summary>
    public char Replacement { get; init; } = '_';

    /// <summary>Lower-case the token (stores that require/fold to lowercase identifiers).</summary>
    public bool Lowercase { get; init; }

    /// <summary>Generic default: keep letters/digits and <c>- . _</c>; replace the rest with <c>_</c>; preserve case.</summary>
    public static readonly PartitionTokenPolicy Default = new();

    /// <summary>Turn a raw partition value into the identifier-safe token described by this policy.</summary>
    public string Format(string partition)
    {
        var token = Guid.TryParse(partition, out var guid) ? guid.ToString(GuidFormat) : partition;
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
            sb.Append(char.IsLetterOrDigit(ch) || AllowedExtraChars.IndexOf(ch) >= 0 ? ch : Replacement);
        var result = sb.ToString();
        return Lowercase ? result.ToLowerInvariant() : result;
    }
}
