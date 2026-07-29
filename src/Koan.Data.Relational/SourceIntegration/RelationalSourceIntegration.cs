using System.Data.Common;
using Koan.Data.Abstractions;

namespace Koan.Data.Relational;

/// <summary>Registered SQL execution shared by relational adapters.</summary>
public sealed class RelationalSourceIntegration(
    Func<string, DbConnection> readConnectionFactory,
    IReadOnlySet<string> readLanes,
    Func<DbConnection, CancellationToken, Task<DbTransaction>> beginReadTransaction,
    IDataSourceInspectorAdapter inspector) : IDataSourceIntegration
{
    public SourceIntegrationCapabilities Capabilities =>
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar;
    public IDataSourceInspectorAdapter Inspector { get; } = inspector;

    public bool Supports(IDataOperationBinding binding, OperationResultKind result) =>
        binding is SqlOperationBinding && result is OperationResultKind.Records or OperationResultKind.Scalar;
    public bool EnforcesReadLane(Koan.Data.Abstractions.Sources.DataReadLanePlan lane) =>
        readLanes.Contains(lane.Name);

    public async Task<INeutralRecordReader> ExecuteRecords(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        var connection = Connection(plan);
        DbTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            transaction = await beginReadTransaction(connection, ct).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = binding.CommandText;
            command.CommandTimeout = checked((int)Math.Ceiling(plan.Timeout.TotalSeconds));
            command.Transaction = transaction;
            Bind(command, parameters);
            return await RelationalNeutralReader.Open(connection, command, ct, transaction: transaction)
                .ConfigureAwait(false);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
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
        await using var connection = Connection(plan);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await beginReadTransaction(connection, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = binding.CommandText;
        command.CommandTimeout = checked((int)Math.Ceiling(plan.Timeout.TotalSeconds));
        command.Transaction = transaction;
        Bind(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var fields = reader.FieldCount;
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return new SourceScalarResult(0, fields, null);
        var value = reader.IsDBNull(0) ? null : reader.GetValue(0);
        var type = reader.GetDataTypeName(0);
        var records = 1;
        if (await reader.ReadAsync(ct).ConfigureAwait(false)) records++;
        return new SourceScalarResult(records, fields, value, type, HasAdditionalResultChannels: false);
    }

    private DbConnection Connection(OperationPlan plan)
    {
        var lane = plan.Lane ?? throw new InvalidOperationException(
            $"Relational operation '{plan.Name}' requires a provider-enforced read lane.");
        if (!readLanes.Contains(lane.Name))
            throw new InvalidOperationException(
                $"Read lane '{lane.Name}' is not configured for source '{plan.Source}'.");
        return readConnectionFactory(lane.Name);
    }

    private static SqlOperationBinding Require(OperationPlan plan) =>
        plan.Binding as SqlOperationBinding
        ?? throw new NotSupportedException($"Relational sources do not support registered binding '{plan.Binding.Kind}'.");

    private static void Bind(DbCommand command, IReadOnlyList<BoundOperationParameter> parameters)
    {
        foreach (var value in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = value.Name.StartsWith('@') ? value.Name : "@" + value.Name;
            parameter.Value = value.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
