using System.Collections.Concurrent;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// Code-first schedules registered through the type gateway (<c>MyJob.Jobs.Schedule(action, expression)</c>) —
/// the runtime twin of <c>[JobAction(Schedule = "…")]</c>. Process-global per closed job type (host-wide
/// composition, not per-tenant configuration); the expression grammar is exactly the attribute's: a
/// <see cref="TimeSpan"/> interval, a cron expression (optionally <c>cron(…)</c>-wrapped), or the
/// <c>@boot</c> / <c>@continuous</c> sentinels. Re-registering an action with the same expression is
/// idempotent; re-registering it with a different expression fails correctively.
/// </summary>
internal static class JobScheduleRegistry
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, string>> Schedules = new();

    public static void Register<T>(string action, string expression)
        where T : Entity<T>, IKoanJob<T>
    {
        ArgumentNullException.ThrowIfNull(action);   // "" is the default single-action token, same as Submit("")
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var perType = Schedules.GetOrAdd(typeof(T), static _ => new(StringComparer.Ordinal));
        if (perType.TryGetValue(action, out var existing))
        {
            if (string.Equals(existing, expression, StringComparison.OrdinalIgnoreCase))
                return;   // idempotent re-entry — fluent configuration may run more than once per host
            throw new InvalidOperationException(
                $"A schedule for '{typeof(T).Name}.{action}' is already registered as '{existing}'; " +
                $"refusing to replace it with '{expression}'. One cadence per action — change the registration, not the rule.");
        }
        if (!perType.TryAdd(action, expression))
        {
            throw new InvalidOperationException(
                $"A schedule for '{typeof(T).Name}.{action}' was registered concurrently; " +
                "keep schedule composition on one host-configuration path.");
        }
    }

    public static IEnumerable<(string Action, string Expression)> For(Type workType)
        => Schedules.TryGetValue(workType, out var perType)
            ? perType.Select(kv => (kv.Key, kv.Value))
            : [];

    public static void Reset<T>() where T : Entity<T>, IKoanJob<T>
        => Schedules.TryRemove(typeof(T), out _);
}
