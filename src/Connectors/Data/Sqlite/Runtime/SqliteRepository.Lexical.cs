using System.Collections.Concurrent;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>
/// The lexical facet: a ranked search over the entity's FTS5 table joined back to its row, a rebuild that re-derives
/// every entry from the rows, and a status that compares what the index covers with what the table holds.
/// </summary>
internal sealed partial class SqliteRepository<TEntity, TKey> : ILexicalSearchRepository<TEntity, TKey>
{
    // The tables (one per partition) whose index this repository has seen the store hold; a search or a rebuild asks
    // the schema once and never again after the first success.
    private readonly ConcurrentDictionary<string, byte> _lexicalHeld = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<LexicalMatch<TKey>>> Search(LexicalQuery query, Filter? scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = Plan();
        _ = RequireLexical(plan);
        await Ready(plan, ct).ConfigureAwait(false);
        await RequireIndex(plan, ct).ConfigureAwait(false);
        var (where, values) = Where(plan, scope);
        var index = SqliteDialect.Quote(SqliteLexical.IndexName(plan.Table));
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // bm25 is lower-is-better; the score answered is its negation so that, as on every store, higher ranks first.
        // FTS5's auxiliary functions and MATCH name the virtual table itself, never an alias of it.
        var select = $"SELECT {plan.Select}, -bm25({index}) AS koan_score";
        if (query.Snippet is { } snippet)
        {
            select += $", snippet({index}, -1, @open, @close, @gap, @words) AS koan_snippet";
            command.Parameters.AddWithValue("@open", snippet.Open);
            command.Parameters.AddWithValue("@close", snippet.Close);
            command.Parameters.AddWithValue("@gap", "…");
            command.Parameters.AddWithValue("@words", snippet.Words);
        }
        command.Parameters.AddWithValue("@match", SqliteLexical.Match(query));
        AddParameters(command, values, "p");
        command.CommandText =
            $"{select} FROM {index} JOIN {plan.QualifiedTable} AS koan_row ON koan_row.rowid = {index}.rowid " +
            $"WHERE {index} MATCH @match" + (where is null ? string.Empty : $" AND ({where})") +
            $" ORDER BY bm25({index}), {string.Join(", ", plan.IdentityRoots.Select(static root => $"koan_row.{SqliteDialect.Quote(root)}"))}" +
            $" LIMIT {query.Limit} OFFSET {query.Offset}";
        var matches = new List<LexicalMatch<TKey>>(query.Limit);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var score = reader.GetOrdinal("koan_score");
        var excerpt = query.Snippet is null ? -1 : reader.GetOrdinal("koan_snippet");
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var entity = plan.Hydrate(reader);
            matches.Add(new LexicalMatch<TKey>(
                entity.Id,
                reader.GetDouble(score),
                excerpt < 0 || reader.IsDBNull(excerpt) ? null : reader.GetString(excerpt)));
        }
        return matches;
    }

    public async Task<long> Rebuild(CancellationToken ct = default)
    {
        var plan = Plan();
        _ = RequireLexical(plan);
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "lexical rebuild");
        await RequireIndex(plan, ct).ConfigureAwait(false);
        var index = SqliteDialect.Quote(SqliteLexical.IndexName(plan.Table));
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using (var rebuild = connection.CreateCommand())
        {
            rebuild.CommandText = $"INSERT INTO {index}({index}) VALUES ('rebuild')";
            await rebuild.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return await Scalar(connection, $"SELECT COUNT(1) FROM {SqliteDialect.Quote(SqliteLexical.DocumentSizeName(plan.Table))}", ct)
            .ConfigureAwait(false);
    }

    public async Task<LexicalIndexStatus> Status(CancellationToken ct = default)
    {
        var plan = Plan();
        var lexical = RequireLexical(plan);
        await Ready(plan, ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var ddl = Ddl(plan);
        var schema = _schema.Plan(plan.Mapping, SqliteStoreFeatures.Instance, _schemaPolicy);
        var definition = schema.Lexical!;
        var exists = await ddl.HasLexicalIndex(schema.Table, definition, ct).ConfigureAwait(false);
        var rows = await Scalar(connection, $"SELECT COUNT(1) FROM {plan.QualifiedTable}", ct).ConfigureAwait(false);
        var indexed = exists
            ? await Scalar(connection, $"SELECT COUNT(1) FROM {SqliteDialect.Quote(SqliteLexical.DocumentSizeName(plan.Table))}", ct)
                .ConfigureAwait(false)
            : 0;
        return new LexicalIndexStatus(
            Infrastructure.Constants.Provider, definition.Fields, lexical.Language, exists, rows, indexed, exists && indexed == rows);
    }

    /// <summary>
    /// Confirms the store holds the index before it is queried, so a route whose DDL policy did not build it refuses
    /// with the correction rather than failing on a missing table. Asked once per table; held afterwards.
    /// </summary>
    private async Task RequireIndex(SqliteEntityPlan<TEntity, TKey> plan, CancellationToken ct)
    {
        if (_lexicalHeld.ContainsKey(plan.Target)) return;
        await using var ddl = Ddl(plan);
        var schema = _schema.Plan(plan.Mapping, SqliteStoreFeatures.Instance, _schemaPolicy);
        if (!await ddl.HasLexicalIndex(schema.Table, schema.Lexical!, ct).ConfigureAwait(false))
            throw new NotSupportedException(
                $"{typeof(TEntity).Name} declares [Lexical] properties, but route '{_route.Source}' on provider " +
                $"'{Infrastructure.Constants.Provider}' does not hold its lexical index '{schema.Lexical!.Name}' (the FTS5 table " +
                $"'{SqliteLexical.IndexName(plan.Table)}', its source view and its triggers). Allow DDL on the route " +
                $"({Infrastructure.Constants.Configuration.Keys.DdlPolicy}=AutoCreate) so the next use creates it, or create " +
                "the structures yourself.");
        _lexicalHeld.TryAdd(plan.Target, 0);
    }

    private static MappingLexicalPlan RequireLexical(SqliteEntityPlan<TEntity, TKey> plan) =>
        plan.Mapping.Lexical ?? throw new NotSupportedException(
            $"{typeof(TEntity).Name} declares no [Lexical] property. Place [Lexical] on the string properties to search.");

    private static async Task<long> Scalar(Session session, string sql, CancellationToken ct)
    {
        await using var command = session.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
