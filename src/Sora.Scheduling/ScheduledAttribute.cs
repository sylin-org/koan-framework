using Microsoft.Extensions.Hosting;

namespace Sora.Scheduling;

// Contract: implement IScheduledTask and optionally one or more policy interfaces.

// Optionally provide a distributed lock identity; provider binding is via configuration.

// Calendar window when the task is allowed to run.

// Allow tasks to surface custom health facts.

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
