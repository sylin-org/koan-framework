using Koan.Data.Abstractions;
using Koan.Data.Relational;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer.Runtime;

internal sealed class SqlServerInspector(SqlServerRoute route) : IDataSourceInspectorAdapter
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public async Task<SourceContainerBatch> Containers(int take, string? providerContinuation, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = Continuation(providerContinuation);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new SqlCommand("""
            SELECT s.name, o.name, CASE WHEN o.type = 'V' THEN 'VIEW' ELSE 'BASE TABLE' END
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
            WHERE o.type IN ('U','V') AND o.is_ms_shipped=0
            ORDER BY s.name, o.name OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY
            """, connection);
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
        var schema = address.Namespace.LastOrDefault() ?? route.Schema;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new SqlCommand("""
            SELECT CASE WHEN o.type = 'V' THEN 'VIEW' ELSE 'BASE TABLE' END
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
            WHERE s.name=@schema AND o.name=@table AND o.type IN ('U','V')
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", address.Name);
        var kind = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException($"SQL Server container '{address}' does not exist on source '{route.Source}'.");
        return new RelationalContainerReference(route.Source, StorageAddress.From(schema, address.Name), kind);
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var value = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = new SqlCommand($"SELECT TOP (0) * FROM {Qualified(value.Address)}", connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return Descriptor(value.Address.Namespace.Last(), value.Address.Name, value.ProviderKind,
            RelationalNeutralReader.Describe(reader));
    }

    public async Task<INeutralRecordReader> Sample(StorageContainerReference reference, int take, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var value = Require(reference);
        var connection = await Open(ct).ConfigureAwait(false);
        var command = new SqlCommand($"SELECT TOP (@take) * FROM {Qualified(value.Address)}", connection);
        command.Parameters.AddWithValue("take", take + 1);
        return await RelationalNeutralReader.Open(connection, command, ct, take).ConfigureAwait(false);
    }

    private StorageContainerDescriptor Descriptor(string schema, string name, string kind, IReadOnlyList<DataField>? shape)
    {
        var address = StorageAddress.From(schema, name);
        var reference = new RelationalContainerReference(route.Source, address, kind);
        var view = string.Equals(kind, "VIEW", StringComparison.OrdinalIgnoreCase);
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

    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var connection = new SqlConnection(route.ConnectionString);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static string Qualified(StorageAddress address) =>
        $"{SqlServerDialect.Quote(address.Namespace.Last())}.{SqlServerDialect.Quote(address.Name)}";
    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, out var offset) && offset >= 0 ? offset :
        throw new ArgumentException("SQL Server container continuation is invalid.", nameof(value));
}
