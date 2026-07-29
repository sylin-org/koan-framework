using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Abstractions;
using Koan.Data.Core.SourceIntegration.Runtime;

namespace Koan.Data.Core.Direct;

internal sealed class DirectTransaction(
    DbConnection conn,
    DbTransaction tx,
    TimeSpan timeout,
    int maxRows,
    DataSourcePlan sourcePlan,
    DataOperationEffect effect,
    RecordSetMaterializer materializer) : IDirectTransaction
{
    public async Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default)
    {
        sourcePlan.Demand(effect, "direct transaction execute");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = (int)timeout.TotalSeconds;
        var dict = DirectSession.ToDictionary(parameters);
        if (dict is not null)
        {
            foreach (var kv in dict)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                p.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        sourcePlan.Demand(effect, "direct transaction scalar");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = (int)timeout.TotalSeconds;
        var dict = DirectSession.ToDictionary(parameters);
        if (dict is not null)
        {
            foreach (var kv in dict)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                p.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }
        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is null || res is DBNull) return default; return (T)Convert.ChangeType(res, typeof(T));
    }

    public async Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default)
    {
        sourcePlan.Demand(effect, "direct transaction query");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = (int)timeout.TotalSeconds;
        var dict = DirectSession.ToDictionary(parameters);
        if (dict is not null)
        {
            foreach (var kv in dict)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                p.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await DirectSession.MaterializeAsObjects(reader, maxRows, ct);
    }

    public async Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        sourcePlan.Demand(effect, "direct transaction typed query");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = (int)timeout.TotalSeconds;
        var dict = DirectSession.ToDictionary(parameters);
        if (dict is not null)
        {
            foreach (var kv in dict)
            {
                var parameter = cmd.CreateParameter();
                parameter.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                parameter.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(parameter);
            }
        }
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var limits = new RecordSetLimits(maxRows, long.MaxValue, long.MaxValue, timeout);
        var records = await materializer.Materialize(
                new DirectSession.AdoNeutralRecordReader(reader),
                limits,
                "direct transaction typed query",
                ct)
            .ConfigureAwait(false);
        return records.Project<T>();
    }

    public async Task Commit(CancellationToken ct = default)
    {
        sourcePlan.Demand(effect, "direct transaction commit");
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    public async Task Rollback(CancellationToken ct = default)
    {
        await tx.RollbackAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }
    }
}
