using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Direct;

internal sealed class DirectTransaction(DbConnection conn, DbTransaction tx, TimeSpan timeout, int maxRows) : Koan.Data.Core.Direct.IDirectTransaction
{
    public async Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default)
    {
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
        return await DirectSession.MaterializeAsJsonObjects(reader, maxRows, ct);
    }

    public async Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var rows = await Query(sql, parameters, ct);
        var settings = JsonSettings.Default;
        var list = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var json = JsonConvert.SerializeObject(row, settings);
            var item = JsonConvert.DeserializeObject<T>(json, settings);
            if (item != null) list.Add(item);
        }
        return list;
    }

    public Task Commit(CancellationToken ct = default)
    {
        tx.Commit(); return Task.CompletedTask;
    }
    public Task Rollback(CancellationToken ct = default)
    {
        try { tx.Rollback(); } catch { }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { await conn.DisposeAsync(); } catch { }
    }
}