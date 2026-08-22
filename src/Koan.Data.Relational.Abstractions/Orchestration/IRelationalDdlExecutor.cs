namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// The grammar half of relational schema work: how one store spells a table, a column, and an index.
///
/// <para>Everything an adapter contributes here is words. Whether a table may be created at all, which columns
/// the mapping implies, which indexes are worth building, whether the environment consents to automatic DDL —
/// those are decided once by the relational schema orchestrator and are not an adapter's to answer
/// (DATA-0119).</para>
///
/// <para>Every member takes the table it acts on, and the owner calls every member; there are no conveniences
/// layered over one another. A default that renders a value the wrong way is worse than no default at all —
/// the previous shape spelled a JSON path as <c>$.a.b</c> for every store, while each dialect's own reads spell
/// it otherwise, so an index built through it was one the planner would never choose.</para>
///
/// <para>An executor speaks over a connection its adapter has already opened, and lives for one schema
/// operation. Opening is a connection concern and stays with the adapter: whether a SQLite file may be created,
/// what a failed open means for an external lifecycle, and how a pool is reached are not schema questions.</para>
///
/// <para>Every member is asynchronous because provisioning is I/O against a live connection, and the four
/// adapters that do this work were already async throughout.</para>
/// </summary>
public interface IRelationalDdlExecutor
{
    /// <summary>
    /// Whether the column the store holds is the one the mapping asked for, judged in the store's own terms.
    ///
    /// <para>Type and nullability are both store conventions, and only the store can compare them. Translating a
    /// column type either way is lossy - SQLite answers TEXT for a string, a date and a Guid alike, while MySQL
    /// distinguishes <c>varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin</c> from <c>varchar(255)</c> in a
    /// way no CLR type can express. Nullability is no better settled: today SQLite declares only its key NOT
    /// NULL, PostgreSQL declares every column NOT NULL, and SQL Server and MySQL declare everything but the key
    /// nullable. A framework comparing those against one neutral answer would invent drift on three stores at
    /// once; unifying the convention is a schema change and belongs to its own decision.</para>
    ///
    /// <para>The framework still owns what surrounds this: which columns must exist, which carry identity, the
    /// order of the primary key, whether the store or the writer supplies a value, and whether a difference is
    /// fatal. This answers one question, and only the store can answer it.</para>
    ///
    /// <para>The default compares CLR type and nullability, which is right for a store describing its columns in
    /// those terms and harmless for one that describes nothing: an undescribed column never reaches here.</para>
    /// </summary>
    bool ColumnMatches(RelationalColumnDefinition expected, RelationalColumnState actual) =>
        actual.ClrType is not null && expected.ClrType == actual.ClrType;

    /// <summary>
    /// The table as the store holds it, or <see langword="null"/> when it does not hold it at all.
    ///
    /// <para>Absence and emptiness are the same answer here — no relational store keeps a table with no
    /// columns — so one read settles both existence and shape, and the two can never disagree.</para>
    /// </summary>
    Task<RelationalTableShape?> Describe(RelationalTableDefinition table, CancellationToken ct = default);

    Task Create(RelationalTableDefinition table, CancellationToken ct = default);

    Task AddColumn(RelationalTableDefinition table, RelationalColumnDefinition column, CancellationToken ct = default);

    /// <summary>
    /// Builds one index the mapping declared.
    ///
    /// <para>The default refuses, because the orchestrator only asks a store that answered
    /// <see cref="IRelationalStoreFeatures.SupportsMappedIndexes"/>. A store that says it can index and cannot
    /// spell one is a contradiction worth hearing about rather than a silently skipped index.</para>
    /// </summary>
    Task CreateIndex(RelationalTableDefinition table, RelationalIndexDefinition index, CancellationToken ct = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} declares mapped-index support but cannot spell index '{index.Name}' for {table}.");

    /// <summary>
    /// Rebuilds a projected column the store already holds, so that it computes what the mapping now says.
    ///
    /// <para>This is the one column an existing table can be corrected into rather than merely told about. A
    /// projected column holds no value of its own — the store recomputes it from the structured root — so
    /// replacing one loses nothing, whatever it had drifted into. The orchestrator decides when that applies;
    /// this only spells it.</para>
    ///
    /// <para>The default refuses, on the same reasoning as <see cref="CreateIndex"/>: the orchestrator asks
    /// only a store that answered <see cref="IRelationalStoreFeatures.SupportsPersistedComputedColumns"/>, and
    /// a store that computes a column it cannot restate is a contradiction worth hearing about.</para>
    /// </summary>
    Task RebuildProjection(
        RelationalTableDefinition table,
        RelationalColumnDefinition column,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} computes projected columns but cannot restate '{column.Name}' for {table}.");
}
