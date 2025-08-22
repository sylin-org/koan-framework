using Microsoft.Extensions.Hosting;

namespace Sora.Scheduling;

// Contract: implement IScheduledTask and optionally one or more policy interfaces.
public interface IScheduledTask
{
    string Id { get; }
    Task RunAsync(CancellationToken ct);
}

public interface IOnStartup { }

public interface IFixedDelay
{
    TimeSpan Delay { get; }
}

public interface ICronScheduled
{
    string Cron { get; }
    TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
}

public interface IHasTimeout { TimeSpan Timeout { get; } }
public interface IIsCritical { bool Critical => true; }
public interface IHasMaxConcurrency { int MaxConcurrency { get; } }

// Optionally provide a distributed lock identity; provider binding is via configuration.
public interface IProvidesLock { string LockName { get; } }

// Calendar window when the task is allowed to run.
public interface IAllowedWindows
{
    // List of windows in local time of TimeZone (default UTC): tuples of (start,end), 24h clock.
    IReadOnlyList<(TimeSpan start, TimeSpan end)> Windows { get; }
    TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
}

// Allow tasks to surface custom health facts.
public interface IHealthFacts
{
    IReadOnlyDictionary<string, string> GetFacts();
}

// Attribute sugar; config will override these.
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ScheduledAttribute : Attribute
{
    public bool OnStartup { get; init; }
    public int? FixedDelaySeconds { get; init; }
    public string? Cron { get; init; }
    public bool Critical { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxConcurrency { get; init; }
}
