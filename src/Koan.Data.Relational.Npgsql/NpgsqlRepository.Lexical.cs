using System.Collections.Concurrent;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Relational.Ado;
using Koan.Data.Relational.Npgsql.Runtime;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Relational.Npgsql;

/// <summary>
/// The lexical facet: a ranked search over the generated <c>tsvector</c> column, and a status that reads what the
/// store holds. The store computes the column itself, so a rebuild has nothing to re-derive and answers the row count.
/// </summary>
public sealed partial class NpgsqlRepository<TEntity, TKey> : ILexicalSearchRepository<TEntity, TKey>
{
    // The tables (one per partition) whose index this repository has seen the store hold; a search or a rebuild asks
    // the catalog once and never again after the first success.
    private readonly ConcurrentDictionary<string, byte> _lexicalHeld = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<LexicalMatch<TKey>>> Search(LexicalQuery query, Filter? scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lexical = RequireLexical();
        await Ready(ct).ConfigureAwait(false);
        await RequireIndex(lexical, ct).ConfigureAwait(false);
        var (where, values) = Where(scope);
        var parameters = Parameters(values);
        var column = NpgsqlDialect.Quote(NpgsqlLexical.ColumnName);
        var configuration = NpgsqlLexical.Configuration(lexical);
        var select = $"SELECT {_plan.Select}, ts_rank_cd({column}, koan_query)::float8 AS koan_score";
        if (query.Snippet is { } snippet)
        {
            select += $", ts_headline({configuration}, {NpgsqlLexical.Text(_plan.Dialect, lexical)}, koan_query, @koan_headline) AS koan_snippet";
            parameters.Add("koan_headline", NpgsqlLexical.Headline(snippet));
        }
        parameters.Add("koan_match", NpgsqlLexical.Match(query));
        var sql = $"{select} FROM {_plan.QualifiedTable}, {NpgsqlLexical.QueryFunction(query)}({configuration}, @koan_match) AS koan_query " +
                  $"WHERE {column} @@ koan_query" + (where is null ? string.Empty : $" AND ({where})") +
                  $" ORDER BY koan_score DESC, {string.Join(", ", _plan.IdentityRoots.Select(NpgsqlDialect.Quote))}" +
                  $" LIMIT {query.Size} OFFSET {query.Offset}";
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await AdoCommands.QueryRowsAsync(connection, sql, parameters, null, ct).ConfigureAwait(false);
        var matches = new List<LexicalMatch<TKey>>(rows.Count);
        foreach (var row in rows)
            matches.Add(new LexicalMatch<TKey>(
                Materialize(row).Id,
                Convert.ToDouble(row["koan_score"], System.Globalization.CultureInfo.InvariantCulture),
                query.Snippet is null ? null : row["koan_snippet"] as string));
        return matches;
    }

    public async Task<long> Rebuild(CancellationToken ct = default)
    {
        var lexical = RequireLexical();
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "lexical rebuild");
        await RequireIndex(lexical, ct).ConfigureAwait(false);
        // A stored generated column is computed by the server for every row, those that existed before the
        // declaration included, so there is nothing to re-derive; the answer is what the index covers.
        var status = await Status(ct).ConfigureAwait(false);
        return status.Indexed;
    }

    public async Task<LexicalIndexStatus> Status(CancellationToken ct = default)
    {
        var lexical = RequireLexical();
        await Ready(ct).ConfigureAwait(false);
        var schema = _schema.Plan(_plan.Mapping, _features, _schemaPolicy);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var exists = _features.SupportsLexicalIndex &&
                     await Ddl(connection).HasLexicalIndex(schema.Table, lexical, ct).ConfigureAwait(false);
        var rows = await AdoCommands.ExecuteScalarInt64Async(
            connection, $"SELECT COUNT(1) FROM {_plan.QualifiedTable}", null, null, ct).ConfigureAwait(false);
        return new LexicalIndexStatus(
            _options.ProviderName, lexical.Fields, lexical.Language, exists, rows, exists ? rows : 0, exists);
    }

    /// <summary>
    /// Confirms the store holds the column and its index before they are queried, so a route whose DDL policy did not
    /// build them refuses with the correction rather than failing on a missing column. Asked once per table; held
    /// afterwards.
    /// </summary>
    private async Task RequireIndex(RelationalLexicalDefinition lexical, CancellationToken ct)
    {
        var plan = _plan;
        if (_lexicalHeld.ContainsKey(plan.Target)) return;
        var schema = _schema.Plan(plan.Mapping, _features, _schemaPolicy);
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (!await Ddl(connection).HasLexicalIndex(schema.Table, lexical, ct).ConfigureAwait(false))
            throw new NotSupportedException(
                $"{typeof(TEntity).Name} declares [Lexical] properties, but route '{_options.Source}' on provider " +
                $"'{_options.ProviderName}' does not hold its lexical index '{lexical.Name}' (the generated column " +
                $"'{NpgsqlLexical.ColumnName}' and the index '{NpgsqlLexical.IndexName(schema.Table)}'). Allow DDL on the route " +
                "(DdlPolicy=AutoCreate) so the next use creates them, or create the column and the index yourself.");
        _lexicalHeld.TryAdd(plan.Target, 0);
    }

    private RelationalLexicalDefinition RequireLexical() =>
        _schema.Plan(_plan.Mapping, _features, _schemaPolicy).Lexical ?? throw new NotSupportedException(
            $"{typeof(TEntity).Name} declares no [Lexical] property. Place [Lexical] on the string properties to search.");
}
