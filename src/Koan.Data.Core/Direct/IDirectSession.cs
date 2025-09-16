namespace Koan.Data.Core.Direct;

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