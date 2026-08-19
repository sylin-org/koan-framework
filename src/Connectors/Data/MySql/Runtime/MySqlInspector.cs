using Koan.Data.Abstractions;
using Koan.Data.Relational;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Runtime;

internal sealed class MySqlInspector(MySqlRoute route) : IDataSourceInspectorAdapter
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public async Task<SourceContainerBatch> Containers(int take, string? providerContinuation, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = Continuation(providerContinuation);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT table_schema, table_name, table_type
            FROM information_schema.tables
            WHERE table_schema=@database AND table_type IN ('BASE TABLE','VIEW')
            ORDER BY table_name LIMIT @take OFFSET @offset
            """, connection);
        command.Parameters.AddWithValue("database", route.Database);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("take", take + 1);
        var values = new List<StorageContainerDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Descriptor(reader.GetString(0), reader.GetString(1), reader.GetString(2), null));
        var more = values.Count > take;
        if (more) values.RemoveAt(values.Count - 1);
        return new SourceContainerBatch(values,
            more ? StorageContainerPageCompletion.MoreAvailable : StorageContainerPageCompletion.Complete,
            more ? (offset + take).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public async Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        var database = RequireRouteDatabase(address, "container resolution");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT table_type FROM information_schema.tables
            WHERE table_schema=@database AND table_name=@table AND table_type IN ('BASE TABLE','VIEW')
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("database", database);
        command.Parameters.AddWithValue("table", address.Name);
        var kind = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException($"MySQL container '{address}' does not exist on source '{route.Source}'.");
        return new RelationalContainerReference(route.Source, StorageAddress.From(database, address.Name), kind);
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var value = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT * FROM {Qualified(value.Address)} LIMIT 0", connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return Descriptor(value.Address.Namespace.Last(), value.Address.Name, value.ProviderKind,
            RelationalNeutralReader.Describe(reader));
    }

    public async Task<INeutralRecordReader> Sample(StorageContainerReference reference, int take, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var value = Require(reference);
        var connection = await Open(ct).ConfigureAwait(false);
        var command = new MySqlCommand($"SELECT * FROM {Qualified(value.Address)} LIMIT @take", connection);
        command.Parameters.AddWithValue("take", take + 1);
        return await RelationalNeutralReader.Open(connection, command, ct, take).ConfigureAwait(false);
    }

    private StorageContainerDescriptor Descriptor(string database, string name, string kind, IReadOnlyList<DataField>? shape)
    {
        var address = StorageAddress.From(database, name);
        var reference = new RelationalContainerReference(route.Source, address, kind);
        var view = string.Equals(kind, "VIEW", StringComparison.OrdinalIgnoreCase);
        return new StorageContainerDescriptor(reference, address, $"{database}/{name}", kind,
            StorageContainerTraits.Records | (view
                ? StorageContainerTraits.Virtual | StorageContainerTraits.ReadOnly
                : StorageContainerTraits.Physical),
            StorageContainerOperations.Describe | StorageContainerOperations.Sample | StorageContainerOperations.Query |
            (view ? StorageContainerOperations.None : StorageContainerOperations.Write), shape);
    }

    private RelationalContainerReference Require(StorageContainerReference reference)
    {
        if (reference is not RelationalContainerReference relational ||
            !string.Equals(relational.Source, route.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(route.Source, reference.Source);
        _ = RequireRouteDatabase(relational.Address, "container inspection");
        return relational;
    }

    private string RequireRouteDatabase(StorageAddress address, string operation)
    {
        if (address.Namespace.Count == 0) return route.Database;
        if (address.Namespace.Count == 1 &&
            string.Equals(address.Namespace[0], route.Database, StringComparison.Ordinal))
            return route.Database;

        throw new InvalidOperationException(
            $"MySQL source '{route.Source}' is bound to database '{route.Database}'; {operation} cannot address '{address}'. Resolve the container from the selected source.");
    }

    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        var connection = new MySqlConnection(route.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static string Qualified(StorageAddress address) =>
        $"{MySqlDialect.Quote(address.Namespace.Last())}.{MySqlDialect.Quote(address.Name)}";
    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, out var offset) && offset >= 0 ? offset :
        throw new ArgumentException("MySQL container continuation is invalid.", nameof(value));
}
