using Koan.Data.Abstractions;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteInspector(SqliteRoute route, SqliteConnectionManager connections) : IDataSourceInspectorAdapter
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers |
        SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer |
        SourceInspectionCapabilities.SampleRecords;

    public async Task<SourceContainerBatch> Containers(int take, string? providerContinuation, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = ParseContinuation(providerContinuation);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, type
            FROM sqlite_schema
            WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            LIMIT @take OFFSET @offset
            """;
        command.Parameters.AddWithValue("@take", checked(take + 1));
        command.Parameters.AddWithValue("@offset", offset);
        var values = new List<StorageContainerDescriptor>(take + 1);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Descriptor(reader.GetString(0), reader.GetString(1), shape: null));

        var more = values.Count > take;
        if (more) values.RemoveAt(values.Count - 1);
        return new SourceContainerBatch(
            values,
            more ? StorageContainerPageCompletion.MoreAvailable : StorageContainerPageCompletion.Complete,
            more ? (offset + take).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public async Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace.Count > 1 ||
            address.Namespace.Count == 1 && !string.Equals(address.Namespace[0], "main", StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"SQLite source '{route.Source}' has no namespace '{string.Join('/', address.Namespace)}'.");
        await using var connection = await Open(ct).ConfigureAwait(false);
        var kind = await Kind(connection, address.Name, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"SQLite container '{address}' does not exist on source '{route.Source}'.");
        return new SqliteContainerReference(route.Source, StorageAddress.From("main", address.Name), kind);
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var sqlite = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var fields = await Fields(connection, sqlite.Address.Name, ct).ConfigureAwait(false);
        return Descriptor(sqlite.Address.Name, sqlite.Kind, fields);
    }

    public async Task<INeutralRecordReader> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var sqlite = Require(reference);
        var connection = await Open(ct).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {SqliteDialect.Quote(sqlite.Address.Name)} LIMIT @take";
        command.Parameters.AddWithValue("@take", checked(take + 1));
        return await SqliteNeutralReader.Open(
            connection,
            command,
            NeutralRecordReaderCompletion.Complete,
            ct,
            take).ConfigureAwait(false);
    }

    private StorageContainerDescriptor Descriptor(string name, string kind, IReadOnlyList<DataField>? shape)
    {
        var address = StorageAddress.From("main", name);
        var reference = new SqliteContainerReference(route.Source, address, kind);
        var view = string.Equals(kind, "view", StringComparison.OrdinalIgnoreCase);
        return new StorageContainerDescriptor(
            reference,
            address,
            $"main/{name}",
            kind,
            StorageContainerTraits.Records |
            (view ? StorageContainerTraits.Virtual | StorageContainerTraits.ReadOnly : StorageContainerTraits.Physical),
            StorageContainerOperations.Describe |
            StorageContainerOperations.Sample |
            StorageContainerOperations.Query |
            (view ? StorageContainerOperations.None : StorageContainerOperations.Write),
            shape);
    }

    private SqliteContainerReference Require(StorageContainerReference reference)
    {
        if (reference is not SqliteContainerReference sqlite ||
            !string.Equals(sqlite.Source, route.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(route.Source, reference.Source);
        return sqlite;
    }

    private async Task<SqliteConnection> Open(CancellationToken ct)
    {
        var connection = connections.Create(route.Options.ConnectionString, route.Source);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task<string?> Kind(SqliteConnection connection, string name, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM sqlite_schema WHERE name = @name AND type IN ('table', 'view') LIMIT 1";
        command.Parameters.AddWithValue("@name", name);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private static async Task<IReadOnlyList<DataField>> Fields(SqliteConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {SqliteDialect.Quote(table)} LIMIT 0";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return SqliteNeutralReader.Describe((SqliteDataReader)reader);
    }

    private static int ParseContinuation(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset) && offset >= 0
            ? offset
            : throw new ArgumentException("SQLite container continuation is invalid.", nameof(value));
}
