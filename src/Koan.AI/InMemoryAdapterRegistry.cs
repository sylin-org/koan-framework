using System;
using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;

namespace Koan.AI;

internal sealed class InMemoryAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private IReadOnlyList<IAiAdapter> _all = [];
    private IReadOnlyDictionary<string, IAiAdapter> _byId =
        new Dictionary<string, IAiAdapter>(StringComparer.OrdinalIgnoreCase);
    private bool _compiled;

    public IReadOnlyList<IAiAdapter> All
    {
        get
        {
            lock (_gate) return _all;
        }
    }

    internal void Compile(IEnumerable<IAiAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        lock (_gate)
        {
            if (_compiled)
            {
                throw new InvalidOperationException(
                    "The AI adapter registry is already compiled for this host and cannot be changed.");
            }

            var ordered = adapters
                .Select(adapter => adapter ?? throw new InvalidOperationException(
                    "AI provider activation returned a null adapter."))
                .OrderBy(static adapter => adapter.Id, StringComparer.Ordinal)
                .ToArray();
            var byId = new Dictionary<string, IAiAdapter>(StringComparer.OrdinalIgnoreCase);
            foreach (var adapter in ordered)
            {
                if (string.IsNullOrWhiteSpace(adapter.Id))
                {
                    throw new InvalidOperationException(
                        $"AI adapter '{adapter.GetType().FullName}' has an empty provider identity.");
                }

                if (!byId.TryAdd(adapter.Id.Trim(), adapter))
                {
                    throw new InvalidOperationException(
                        $"AI adapter identity '{adapter.Id}' is activated more than once. " +
                        "One provider identity must resolve to exactly one adapter.");
                }
            }

            _all = ordered;
            _byId = byId;
            _compiled = true;
        }
    }

    /// <summary>Internal pre-start builder seam used by focused routing tests.</summary>
    internal void Add(IAiAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (_gate)
        {
            if (_compiled)
            {
                throw new InvalidOperationException(
                    "The AI adapter registry is already compiled for this host and cannot be changed.");
            }

            if (_byId.ContainsKey(adapter.Id))
            {
                throw new InvalidOperationException(
                    $"AI adapter identity '{adapter.Id}' is activated more than once.");
            }

            var next = _all.Append(adapter)
                .OrderBy(static candidate => candidate.Id, StringComparer.Ordinal)
                .ToArray();
            _all = next;
            _byId = next.ToDictionary(static candidate => candidate.Id, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IAiAdapter? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate) return _byId.GetValueOrDefault(id.Trim());
    }
}
