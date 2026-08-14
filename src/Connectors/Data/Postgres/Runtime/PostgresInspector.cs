using Koan.Data.Abstractions;
using Koan.Data.Relational;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Runtime;

internal sealed class PostgresInspector(PostgresRoute route) :
    IDataSourceInspectorAdapter,
    IDataSourceStatusInspector
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public IDataSourceNativeInspector Native => this;

    public async Task<DataSourceStorageState> Status(CancellationToken ct = default)
    {
        try
        {
            await using var connection = await Open(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return new DataSourceStorageState(
                DataSourceStorageStatus.Ready,
                Infrastructure.Constants.StorageStatus.Ready);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new DataSourceStorageState(
                DataSourceStorageStatus.Unavailable,
                Infrastructure.Constants.StorageStatus.Timeout);
        }
        catch (NpgsqlException)
        {
            return new DataSourceStorageState(
                DataSourceStorageStatus.Unavailable,
                Infrastructure.Constants.StorageStatus.Unavailable);
        }
        catch
        {
            return new DataSourceStorageState(
                DataSourceStorageStatus.Unavailable,
                Infrastructure.Constants.StorageStatus.Unavailable);
        }
    }

    public async Task<SourceContainerBatch> Containers(int take, string? providerContinuation, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = Continuation(providerContinuation);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            SELECT table_schema, table_name, table_type
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name
            LIMIT @take OFFSET @offset
            """, connection);
        command.Parameters.AddWithValue("take", take + 1);
        command.Parameters.AddWithValue("offset", offset);
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
        var schema = address.Namespace.LastOrDefault() ?? route.SearchPath;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT table_type FROM information_schema.tables WHERE table_schema=@schema AND table_name=@table", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", address.Name);
        var kind = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException($"PostgreSQL container '{address}' does not exist on source '{route.Source}'.");
        return new RelationalContainerReference(route.Source, StorageAddress.From(schema, address.Name), kind);
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var value = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"SELECT * FROM {Qualified(value.Address)} LIMIT 0", connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return Descriptor(value.Address.Namespace.Last(), value.Address.Name, value.ProviderKind,
            RelationalNeutralReader.Describe(reader));
    }

    public async Task<INeutralRecordReader> Sample(StorageContainerReference reference, int take, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var value = Require(reference);
        var connection = await Open(ct).ConfigureAwait(false);
        var command = new NpgsqlCommand($"SELECT * FROM {Qualified(value.Address)} LIMIT @take", connection);
        command.Parameters.AddWithValue("take", take + 1);
        return await RelationalNeutralReader.Open(connection, command, ct, take).ConfigureAwait(false);
    }

    private StorageContainerDescriptor Descriptor(string schema, string name, string kind, IReadOnlyList<DataField>? shape)
    {
        var address = StorageAddress.From(schema, name);
        var reference = new RelationalContainerReference(route.Source, address, kind);
        var view = kind.Contains("VIEW", StringComparison.OrdinalIgnoreCase);
        return new StorageContainerDescriptor(reference, address, $"{schema}/{name}", kind,
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
        return relational;
    }

    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(route.ConnectionString);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static string Qualified(StorageAddress address) =>
        $"{Quote(address.Namespace.Last())}.{Quote(address.Name)}";
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, out var offset) && offset >= 0 ? offset :
        throw new ArgumentException("PostgreSQL container continuation is invalid.", nameof(value));
}
