using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Diagnostics;

/// <summary>
/// Production debug hook for cache operations on a specific key. Set the environment
/// variable <c>KOAN_CACHE_TRACE_KEY</c> to the cache key you want to follow; all touches
/// of that key emit verbose Information-level log lines from the cache pillar — no
/// redeployment required.
/// </summary>
/// <remarks>
/// <para>
/// The trace key is read once at process startup (capturing the env var value into a
/// static field) — changes to the env var mid-process do not take effect. This keeps the
/// hot-path check to a single reference compare.
/// </para>
/// <para>
/// Match semantics: exact-string ordinal compare. Wildcards are intentionally absent —
/// debugging is most useful when targeted to one suspect entry. For broader observability,
/// use the OpenTelemetry metrics + spans emitted by <see cref="CacheInstrumentation"/>.
/// </para>
/// </remarks>
public static class CacheTraceFilter
{
    /// <summary>Environment variable name read at startup.</summary>
    public const string EnvironmentVariableName = "KOAN_CACHE_TRACE_KEY";

    private static readonly string? Configured = ReadOnce();

    /// <summary>True when a trace key is configured for this process.</summary>
    public static bool IsEnabled => Configured is not null;

    /// <summary>The configured trace key, or null if the env var is unset.</summary>
    public static string? TraceKey => Configured;

    /// <summary>Test/diagnostic hook to override the configured trace key. Returns previous value.</summary>
    internal static string? OverrideForTesting(string? value)
    {
        var prev = Interlocked.Exchange(ref _override, value);
        return prev;
    }

    private static string? _override;

    private static string? ReadOnce()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    /// <summary>True iff <paramref name="key"/> matches the configured trace key (or the test override).</summary>
    public static bool ShouldTrace(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var target = _override ?? Configured;
        return target is not null && string.Equals(target, key, StringComparison.Ordinal);
    }

    /// <summary>
    /// Emit a verbose Information-level line if <paramref name="key"/> matches the configured
    /// trace key. No-op otherwise. Use this from cache hot paths to gain key-specific visibility
    /// without flipping global log levels.
    /// </summary>
    public static void LogIfTraced(ILogger logger, string key, string action, string? outcome = null)
    {
        if (!ShouldTrace(key)) return;
        logger.LogInformation("Koan.Cache TRACE key={Key} action={Action} outcome={Outcome}", key, action, outcome ?? "-");
    }
}
