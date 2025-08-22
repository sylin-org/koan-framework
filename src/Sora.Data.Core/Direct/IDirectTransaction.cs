namespace Sora.Data.Core.Direct;

public interface IDirectTransaction : IAsyncDisposable
{
    Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default);
    Task Commit(CancellationToken ct = default);
    Task Rollback(CancellationToken ct = default);
}