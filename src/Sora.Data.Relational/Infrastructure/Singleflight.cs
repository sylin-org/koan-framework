namespace Sora.Data.Relational.Infrastructure;

/// <summary>
/// Compatibility shim forwarding to the core Singleflight implementation.
/// </summary>
public static class Singleflight
{
    public static Task RunAsync(string key, System.Func<CancellationToken, Task> work, CancellationToken ct = default)
        => Sora.Core.Infrastructure.Singleflight.RunAsync(key, work, ct);

    public static Task<T> RunAsync<T>(string key, System.Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
        => Sora.Core.Infrastructure.Singleflight.RunAsync<T>(key, work, ct);

    public static void Invalidate(string key)
        => Sora.Core.Infrastructure.Singleflight.Invalidate(key);
}
