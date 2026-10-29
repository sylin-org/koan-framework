using System.Collections.Concurrent;
using Koan.Core;
using Koan.Core.Hosting.Registry;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Lexical;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Lexical;

/// <summary>
/// Reports, per entity that declares a lexical index, whether the routed store's index is current: it covers every
/// row, or rows that existed before the declaration still wait for a rebuild. Readiness is where a store's actual
/// state is read (DATA-0158 reports the journal mode each database holds the same way); the startup facts carry the
/// declaration and the elected provider. Inert when no entity declares one.
///
/// <para>A probe never runs an unbounded read. Currency is measured by <c>Status</c>, which counts the container, so
/// the contributor measures each entity at most once per <see cref="DataRuntimeOptions.LexicalReadinessInterval"/>
/// and answers the last measurement in between; the interval and the time of the measurement are in the report.
/// <c>Entity.Lexical.Status()</c> called by an application is always exact.</para>
/// </summary>
internal sealed class LexicalIndexHealthContributor(
    IServiceProvider services,
    IOptions<DataRuntimeOptions> options,
    TimeProvider? time = null) : IHealthContributor
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Type, Observation> _observations = new();
    private readonly Lazy<Type[]> _entities = new(() => KoanRegistry.GetDiscoveredImplementors(typeof(IEntity))
        .Where(static type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
        .Where(static type => LexicalMetadata.Get(type) is not null)
        .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal)
        .ToArray());
    private int _measurements;

    public string Name => "data:lexical";

    public bool IsCritical => false;

    /// <summary>How many times a probe measured an entity's index (ran its counts); the spec seam for the bound.</summary>
    internal int Measurements => Volatile.Read(ref _measurements);

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        var entities = _entities.Value;
        if (entities.Length == 0)
            return new HealthReport(Name, HealthState.Unknown, "No entity declares a lexical index", null, null);

        var interval = options.Value.LexicalReadinessInterval;
        var now = _time.GetUtcNow();
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        var stale = new List<string>();
        var unkept = new List<string>();
        foreach (var type in entities)
        {
            var name = type.Name;
            var observation = await Observe(type, now, interval, ct).ConfigureAwait(false);
            if (observation.Error is { } error)
            {
                data[name] = $"error:{error}";
                stale.Add(name);
                continue;
            }
            if (observation.Status is not { } index)
            {
                data[name] = "unkept";
                unkept.Add(name);
                continue;
            }
            data[name] = !index.Exists
                ? $"{index.Provider}:absent"
                : $"{index.Provider}:{(index.Current ? "current" : "stale")}:{index.Indexed}/{index.Rows}";
            if (!index.Current) stale.Add(name);
        }
        data["entities"] = string.Join(",", entities.Select(static type => type.Name));
        data["interval"] = interval.ToString();
        data["observedAt"] = _observations.Values.Select(static observation => observation.At).DefaultIfEmpty(now).Min().ToString("O");
        var state = stale.Count == 0 && unkept.Count == 0 ? HealthState.Healthy : HealthState.Degraded;
        var description = state == HealthState.Healthy
            ? "Every declared lexical index is current"
            : unkept.Count != 0
                ? $"A route cannot keep the lexical index of {string.Join(", ", unkept)}"
                : $"The lexical index of {string.Join(", ", stale)} is not current; run Rebuild";
        return new HealthReport(Name, state, description, null, data);
    }

    /// <summary>The entity's last measurement, taken again only once the interval has passed.</summary>
    private async Task<Observation> Observe(Type type, DateTimeOffset now, TimeSpan interval, CancellationToken ct)
    {
        if (_observations.TryGetValue(type, out var held) && now - held.At < interval) return held;
        Observation fresh;
        try
        {
            fresh = new Observation(now, await LexicalRoutes.Status(type, services, ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            fresh = new Observation(now, null, error.GetType().Name);
        }
        Interlocked.Increment(ref _measurements);
        _observations[type] = fresh;
        return fresh;
    }

    private sealed record Observation(DateTimeOffset At, LexicalIndexStatus? Status, string? Error);
}
