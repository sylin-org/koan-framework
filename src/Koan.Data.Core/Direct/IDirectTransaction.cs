namespace Koan.Data.Core.Direct;

public interface IDirectTransaction : IAsyncDisposable
{
    Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default);
    /// <summary>The rows <paramref name="sql"/> returns, at most the session's MaxRows; <see cref="DirectRows{T}.Completion"/>
    /// says whether the command had more.</summary>
    Task<DirectRows<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default);
    /// <summary>The rows <paramref name="sql"/> returns as column-name dictionaries, at most the session's MaxRows;
    /// <see cref="DirectRows{T}.Completion"/> says whether the command had more.</summary>
    Task<DirectRows<object>> Query(string sql, object? parameters = null, CancellationToken ct = default);
    Task Commit(CancellationToken ct = default);
    Task Rollback(CancellationToken ct = default);
}