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
    /// <summary>Maximum number of jobs that may execute concurrently in this lane.</summary>
    public int MaxConcurrency { get; set; }
}
