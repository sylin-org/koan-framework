using Koan.Data.Abstractions;
using Koan.Data.Relational;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteSourceIntegration(SqliteRoute route, SqliteConnectionManager connections) : IDataSourceIntegration
{
    public SourceIntegrationCapabilities Capabilities =>
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar;

    public IDataSourceInspectorAdapter Inspector { get; } = new SqliteInspector(route, connections);

    public bool Supports(IDataOperationBinding binding, OperationResultKind result) =>
        binding is SqlOperationBinding &&
        result is OperationResultKind.Records or OperationResultKind.Scalar;

    public bool EnforcesReadLane(Koan.Data.Abstractions.Sources.DataReadLanePlan lane) =>
        route.ReadLanes.ContainsKey(lane.Name);

    public async Task<INeutralRecordReader> ExecuteRecords(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        var connection = connections.Create(ConnectionString(plan), route.Source);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnforceReadLane(connection, plan, ct).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = binding.CommandText;
            command.CommandTimeout = checked((int)Math.Ceiling(plan.Timeout.TotalSeconds));
            SqliteCommandParameters.Bind(command, parameters);
            return await SqliteNeutralReader.Open(
                connection,
                command,
                NeutralRecordReaderCompletion.Complete,
                ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SourceScalarResult> ExecuteScalar(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        await using var connection = connections.Create(ConnectionString(plan), route.Source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await EnforceReadLane(connection, plan, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = binding.CommandText;
        command.CommandTimeout = checked((int)Math.Ceiling(plan.Timeout.TotalSeconds));
        SqliteCommandParameters.Bind(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var fields = reader.FieldCount;
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return new SourceScalarResult(0, fields, null);
        var value = reader.IsDBNull(0) ? null : reader.GetValue(0);
        var type = reader.GetDataTypeName(0);
        var records = 1;
        if (await reader.ReadAsync(ct).ConfigureAwait(false)) records++;
        return new SourceScalarResult(records, fields, value, type, HasAdditionalResultChannels: false);
    }

    private static SqlOperationBinding Require(OperationPlan plan) =>
        plan.Binding as SqlOperationBinding
        ?? throw new NotSupportedException($"SQLite does not support registered binding '{plan.Binding.Kind}'.");

    private string ConnectionString(OperationPlan plan) =>
        plan.Lane is null
            ? route.Options.ConnectionString
            : route.ReadLanes.TryGetValue(plan.Lane.Name, out var connectionString)
                ? connectionString
                : throw new InvalidOperationException(
                    $"SQLite read lane '{plan.Lane.Name}' is not configured for source '{route.Source}'.");

    private static async Task EnforceReadLane(
        SqliteConnection connection,
        OperationPlan plan,
        CancellationToken ct)
    {
        if (plan.Lane is null) return;
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only = ON";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
