namespace Koan.Data.Relational.Infrastructure;

/// <summary>
/// Compatibility shim forwarding to the core Singleflight implementation.
/// </summary>
public static class Singleflight
{
    public static Task RunAsync(string key, Func<CancellationToken, Task> work, CancellationToken ct = default)
        => Koan.Core.Infrastructure.Singleflight.RunAsync(key, work, ct);

    public static Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
        => Koan.Core.Infrastructure.Singleflight.RunAsync<T>(key, work, ct);

    public static void Invalidate(string key)
        => Koan.Core.Infrastructure.Singleflight.Invalidate(key);
}
