namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// The grammar half of relational schema work: how one store spells a table, a column, and an index.
///
/// <para>Everything an adapter contributes here is words. Whether a table may be created at all, which columns
/// the mapping implies, whether the environment consents to automatic DDL — those are decided once by
/// <see cref="IRelationalSchemaOrchestrator"/> and are not an adapter's to answer (DATA-0119).</para>
///
/// <para>Every member is asynchronous because provisioning is I/O against a live connection, and the four
/// adapters that do this work were already async throughout. The contract was synchronous when it was written,
/// which is why none of them could adopt it: honouring it meant blocking a connection thread on every probe.</para>
/// </summary>
public interface IRelationalDdlExecutor
{
    Task<bool> TableExists(string schema, string table, CancellationToken ct = default);

    Task<bool> ColumnExists(string schema, string table, string column, CancellationToken ct = default);

    /// <summary>
    /// The column as the store actually holds it, or <see langword="null"/> when it is absent.
    ///
    /// <para>A store that cannot describe its own columns says so through
    /// <see cref="IRelationalStoreFeatures.SupportsDefinitionValidation"/>; the orchestrator then records the
    /// column as unverified rather than pretending the shapes matched. This default is the shape of that
    /// admission — presence is known, definition is not.</para>
    /// </summary>
    async Task<RelationalColumnDefinition?> DescribeColumn(
        string schema,
        string table,
        string column,
        CancellationToken ct = default)
        => await ColumnExists(schema, table, column, ct).ConfigureAwait(false)
            ? new RelationalColumnDefinition(column, typeof(object), true)
            : null;

    Task CreateTableIdJson(
        string schema,
        string table,
        string idColumn = "Id",
        string jsonColumn = "Json",
        CancellationToken ct = default);

    Task CreateTableWithColumns(
        string schema,
        string table,
        IReadOnlyList<RelationalColumnDefinition> columns,
        CancellationToken ct = default);

    Task AddComputedColumnFromJson(
        string schema,
        string table,
        string column,
        string jsonPath,
        bool persisted,
        CancellationToken ct = default);

    Task AddPhysicalColumn(
        string schema,
        string table,
        string column,
        Type clrType,
        bool nullable,
        CancellationToken ct = default);

    Task AddMappedColumn(
        string schema,
        string table,
        RelationalColumnDefinition column,
        CancellationToken ct = default)
        => AddPhysicalColumn(schema, table, column.Name, column.ClrType, column.Nullable, ct);

    Task CreateIndex(
        string schema,
        string table,
        string indexName,
        IReadOnlyList<string> columns,
        bool unique,
        CancellationToken ct = default);

    Task CreateJsonExpressionIndex(
        string schema,
        string table,
        string indexName,
        IReadOnlyList<RelationalJsonIndexPart> parts,
        bool unique,
        CancellationToken ct = default);

    /// <summary>
    /// Routes an index to the spelling its parts require: a plain index over columns, or an expression index
    /// over JSON paths. Overriding this is for a store whose grammar genuinely differs, not for deciding which
    /// indexes to build — the orchestrator has already decided that.
    /// </summary>
    Task CreateMappedIndex(
        string schema,
        string table,
        RelationalIndexDefinition index,
        CancellationToken ct = default)
    {
        if (index.Ttl)
            throw new NotSupportedException("This relational DDL executor does not implement native TTL lowering.");
        if (index.Parts.All(static part => !part.IsNested))
            return CreateIndex(
                schema, table, index.Name, index.Parts.Select(static part => part.Name).ToArray(), index.Unique, ct);

        return CreateJsonExpressionIndex(
            schema,
            table,
            index.Name,
            index.Parts.Select(part => new RelationalJsonIndexPart(
                part.Name,
                part.IsNested ? "$." + string.Join('.', part.Segments) : null)).ToArray(),
            index.Unique,
            ct);
    }
}
