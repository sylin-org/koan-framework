using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Direct;

public interface IDirectDataService
{
    IDirectSession Direct(string sourceOrAdapter);
}

public interface IDirectSession
{
    IDirectSession WithConnectionString(string value);
    IDirectSession WithTimeout(TimeSpan timeout);
    IDirectSession WithMaxRows(int maxRows);
    IDirectTransaction Begin(CancellationToken ct = default);

    Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default);
}

public interface IDirectTransaction : IAsyncDisposable
{
    Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default);
    Task Commit(CancellationToken ct = default);
    Task Rollback(CancellationToken ct = default);
}
