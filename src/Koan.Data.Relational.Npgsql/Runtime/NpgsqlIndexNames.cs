using System.Security.Cryptography;
using System.Text;
using Koan.Core.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Relational.Npgsql.Runtime;

/// <summary>
/// PostgreSQL index names are unique per schema and are silently truncated to 63 bytes, and
/// <c>CREATE INDEX IF NOT EXISTS</c> skips on a name match whatever index it finds. A declared index can therefore be
/// missing while its creation reported success: a second table sharing an explicit name, a second partition table, or a
/// second index whose long name truncates to the first one's (NK-056). These rules decide, from the declared index and
/// what the catalog holds under its effective name, whether the declaration was honoured.
/// </summary>
internal static class NpgsqlIndexNames
{
    /// <summary>PostgreSQL's identifier limit: NAMEDATALEN - 1.</summary>
    internal const int MaximumBytes = 63;

    // One engine composes every Koan identifier (ARCH-0096): a name over the limit keeps a readable prefix and ends in
    // '_' plus a hash of the whole, so two names sharing a prefix stay distinct.
    private static readonly CompositionPolicy Policy = new("_", VerbatimParticleFormatter.Instance, MaximumBytes);

    /// <summary>
    /// The name this index is created under (DATA-0146). A derived name already carries its container, so it is only
    /// fitted to 63 bytes with a hash, never cut. A declared name (<c>[Index(Name = ...)]</c>) is the application's and
    /// is used as written, except on a partition table, where it is qualified by a hash of the table name: one
    /// declaration then yields one index per partition table instead of one for the whole schema.
    /// </summary>
    internal static string Name(RelationalTableDefinition table, RelationalIndexDefinition index, bool partitioned)
    {
        if (!index.NameDeclared || !partitioned)
            return IdentifierComposer.Compose(index.Name, [], Policy);
        Span<Particle> qualifier = [new Particle(0, "table", Hash(table.Name))];
        return IdentifierComposer.Compose(index.Name, qualifier, Policy);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 4).ToLowerInvariant();

    /// <summary>The name PostgreSQL stores: at most 63 UTF-8 bytes, cut at a character boundary.</summary>
    internal static string Effective(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaximumBytes) return name;
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaximumBytes) break;
            bytes += rune.Utf8SequenceLength;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    /// <summary>
    /// Records that this table's provisioning uses <paramref name="name"/>, and refuses a second declared index whose
    /// name PostgreSQL would store under the same truncated name.
    /// </summary>
    internal static string Claim(IDictionary<string, string> claimed, RelationalTableDefinition table, string name)
    {
        var effective = Effective(name);
        if (claimed.TryGetValue(effective, out var other) && !string.Equals(other, name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Indexes '{other}' and '{name}' on {table} both truncate to the PostgreSQL name '{effective}' (63 bytes), " +
                "so the second would never be created. Give one an explicit, shorter [Index(Name = ...)], or shorten the " +
                "storage name of the entity.");
        claimed[effective] = name;
        return effective;
    }

    /// <summary>One catalog row for an index name: the table it is on, whether it is unique, and its key count.</summary>
    internal sealed record CatalogEntry(string Table, bool Unique, int Keys);

    /// <summary>
    /// Refuses when the catalog does not hold the declared index on this table under its effective name: absent, owned
    /// by another table, not unique where uniqueness was declared, or keyed on a different number of columns.
    /// </summary>
    internal static void Verify(
        RelationalTableDefinition table,
        RelationalIndexDefinition index,
        string effective,
        IReadOnlyList<CatalogEntry> found)
    {
        var own = found.FirstOrDefault(entry => string.Equals(entry.Table, table.Name, StringComparison.Ordinal));
        if (own is null)
            throw new InvalidOperationException(found.Count == 0
                ? $"Index '{index.Name}' for {table} was not created: PostgreSQL holds no index named '{effective}' " +
                  $"in schema '{table.Schema}'."
                : $"Index '{index.Name}' for {table} was not created: PostgreSQL index names are unique per schema, and " +
                  $"'{effective}' already belongs to table '{found[0].Table}'. Give the index a name unique across the " +
                  "schema's tables, or omit [Index(Name)] so Koan derives one from the table.");
        if (index.Unique && !own.Unique)
            throw new InvalidOperationException(
                $"Index '{index.Name}' for {table} is declared unique, but the existing PostgreSQL index '{effective}' on " +
                "that table is not, so duplicates are not refused. Resolve the difference with an explicit migration; " +
                "Koan does not drop or replace existing indexes.");
        if (own.Keys != index.Parts.Count)
            throw new InvalidOperationException(
                $"Index '{index.Name}' for {table} declares {index.Parts.Count} key(s), but the existing PostgreSQL index " +
                $"'{effective}' on that table has {own.Keys}. Resolve the difference with an explicit migration; Koan " +
                "does not drop or replace existing indexes.");
    }
}
