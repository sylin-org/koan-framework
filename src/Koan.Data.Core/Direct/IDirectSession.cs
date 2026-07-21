namespace Koan.Data.Core.Direct;

public interface IDirectSession
{
    /// <summary>
    /// Uses <paramref name="value"/> as a literal physical connection-string override.
    /// </summary>
    /// <remarks>
    /// Empty values and <c>auto</c> are not physical connections. Use a source- or adapter-routed
    /// Direct session when Koan should resolve configuration or autonomous discovery.
    /// </remarks>
    IDirectSession WithConnectionString(string value);
    IDirectSession WithTimeout(TimeSpan timeout);
    IDirectSession WithMaxRows(int maxRows);
    IDirectTransaction Begin(CancellationToken ct = default);

    Task<int> Execute(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> Scalar<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<IReadOnlyList<object>> Query(string sql, object? parameters = null, CancellationToken ct = default);
}
