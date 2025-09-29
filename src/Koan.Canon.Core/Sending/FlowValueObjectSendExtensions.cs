namespace Koan.Canon.Runtime;

public static class CanonRuntimeExtensions
{
    public static Task ReplayAllAsync(this ICanonRuntime runtime, CancellationToken ct = default)
        => runtime.ReplayAsync(null, null, ct);

    public static Task ReplayFromAsync(this ICanonRuntime runtime, DateTimeOffset from, CancellationToken ct = default)
        => runtime.ReplayAsync(from, null, ct);

    public static Task ReprojectAsync(this ICanonRuntime runtime, string referenceId, CancellationToken ct = default)
        => runtime.ReprojectAsync(referenceId, null, ct);

    public static Task ReprojectViewAsync(this ICanonRuntime runtime, string referenceId, string viewName, CancellationToken ct = default)
        => runtime.ReprojectAsync(referenceId, viewName, ct);
}


