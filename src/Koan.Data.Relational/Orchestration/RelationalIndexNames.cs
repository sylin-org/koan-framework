using System.Security.Cryptography;
using System.Text;
using Koan.Core.Naming;

namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// The name an index is created under on a store whose index names span its containers: schema-wide on PostgreSQL,
/// database-wide on SQLite (DATA-0146, DATA-0160). One rule for every such store, so a declaration names the same
/// index set on each of them and the names cannot drift apart per connector.
///
/// <para>A derived name (<c>IX_&lt;table&gt;_&lt;members&gt;</c>) already carries its table, which carries its partition, so
/// it is only fitted to the store's limit. A declared name (<c>[Index(Name = ...)]</c>) is the application's and is used
/// as written on the entity's own table; on a partition table, which every partition of the entity shares the
/// declaration with, it is qualified by a hash of the table name, so one declaration yields one index per container.
/// Two entities that declare one name still meet on their own tables, where the store's refusal stands.</para>
/// </summary>
public static class RelationalIndexNames
{
    /// <summary>
    /// The name <paramref name="index"/> is created under on <paramref name="table"/>.
    /// </summary>
    /// <param name="table">The container the index belongs to.</param>
    /// <param name="index">The planned index, carrying whether the application declared its name.</param>
    /// <param name="partitioned">Whether <paramref name="table"/> is a partition table of its entity rather than the
    /// entity's own table.</param>
    /// <param name="maximumBytes">The store's identifier limit in UTF-8 bytes; a longer name keeps a readable prefix and
    /// ends in <c>_</c> plus a hash of the whole (ARCH-0096). <see langword="null"/> where the store has none.</param>
    public static string Name(
        RelationalTableDefinition table,
        RelationalIndexDefinition index,
        bool partitioned,
        int? maximumBytes = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(index);
        var policy = new CompositionPolicy("_", VerbatimParticleFormatter.Instance, maximumBytes);
        if (!index.NameDeclared || !partitioned)
            return IdentifierComposer.Compose(index.Name, [], policy);
        Span<Particle> qualifier = [new Particle(0, "table", Hash(table.Name))];
        return IdentifierComposer.Compose(index.Name, qualifier, policy);
    }

    /// <summary>Eight lower-case hex characters of the SHA-256 of the table name.</summary>
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 4).ToLowerInvariant();
}
