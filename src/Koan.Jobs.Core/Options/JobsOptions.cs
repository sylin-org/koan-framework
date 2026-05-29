using System;
using System.Collections.Generic;
using System.Text.Json;
using Koan.Jobs.Archival;

namespace Koan.Jobs.Options;

public sealed class JobsOptions
{
    /// <summary>Publish job lifecycle/progress notifications over messaging when available.</summary>
    public bool PublishEvents { get; set; } = true;
    public JobArchivalPolicy Archival { get; } = new();
    public JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Per-lane concurrency settings, keyed by lane name (see JOBS-0002). A lane is a named bound
    /// the worker honours when dispatching execution, so independent lanes run in parallel and each
    /// is capped. Lanes absent from this map use <see cref="DefaultLaneConcurrency"/>.
    /// Config: <c>Koan:Jobs:Lanes:{name}:MaxConcurrency</c>.
    /// </summary>
    public Dictionary<string, JobLaneOptions> Lanes { get; } = new(StringComparer.Ordinal);

    /// <summary>Concurrency cap for the default lane and any lane not listed in <see cref="Lanes"/>.</summary>
    public int DefaultLaneConcurrency { get; set; } = Environment.ProcessorCount;
}

/// <summary>Per-lane settings (see <see cref="JobsOptions.Lanes"/>).</summary>
public sealed class JobLaneOptions
{
    /// <summary>Maximum number of jobs that may execute concurrently in this lane — the global cap
    /// across every partition. 0 falls back to <see cref="JobsOptions.DefaultLaneConcurrency"/>.</summary>
    public int MaxConcurrency { get; set; }

    /// <summary>Optional second concurrency tier (JOBS-0004): the maximum jobs that may run
    /// concurrently for a single partition key within this lane. 0 (the default) disables
    /// partitioning, so only <see cref="MaxConcurrency"/> applies. When set, a job's
    /// <c>LanePartition</c> permit is acquired BEFORE the lane-global permit, so one hot partition's
    /// backlog cannot fill the global gate and starve the other partitions.</summary>
    public int MaxConcurrencyPerPartition { get; set; }

    /// <summary>Per-key overrides for <see cref="MaxConcurrencyPerPartition"/>, keyed by the partition
    /// value (e.g. an upstream brand). A value &gt; 0 caps that key specifically; keys absent here use
    /// <see cref="MaxConcurrencyPerPartition"/>. Config:
    /// <c>Koan:Jobs:Lanes:{lane}:PartitionOverrides:{key}</c>.</summary>
    public Dictionary<string, int> PartitionOverrides { get; } = new(StringComparer.Ordinal);
}
