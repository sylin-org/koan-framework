using System;
using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Routing;

namespace Koan.AI;

internal sealed class InMemoryAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private readonly List<AiAdapterRegistration> _registrations = new();

    public IReadOnlyList<Contracts.Adapters.IAiAdapter> All
    {
        get
        {
            lock (_gate)
            {
                return _registrations.Select(r => r.Adapter).ToArray();
            }
        }
    }

    public IReadOnlyList<AiAdapterRegistration> Registrations
    {
        get
        {
            lock (_gate)
            {
                return _registrations.ToArray();
            }
        }
    }

    public void Add(Contracts.Adapters.IAiAdapter adapter)
    {
        if (adapter is null) throw new ArgumentNullException(nameof(adapter));

        lock (_gate)
        {
            if (_registrations.Any(a => string.Equals(a.Adapter.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var descriptor = adapter.GetType().GetCustomAttributes(typeof(Contracts.Adapters.AiAdapterDescriptorAttribute), inherit: true)
                .FirstOrDefault() as Contracts.Adapters.AiAdapterDescriptorAttribute;

            var weight = Math.Max(1, descriptor?.Weight ?? 1);
            var registration = new AiAdapterRegistration
            {
                Adapter = adapter,
                Priority = descriptor?.Priority ?? 0,
                Weight = weight,
                RegisteredAt = DateTimeOffset.UtcNow
            };

            _registrations.Add(registration);
            _registrations.Sort(static (left, right) =>
            {
                var priorityCompare = right.Priority.CompareTo(left.Priority);
                if (priorityCompare != 0) return priorityCompare;
                var typeCompare = string.Compare(left.Adapter.GetType().FullName, right.Adapter.GetType().FullName, StringComparison.Ordinal);
                if (typeCompare != 0) return typeCompare;
                return left.RegisteredAt.CompareTo(right.RegisteredAt);
            });
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        lock (_gate)
        {
            return _registrations.RemoveAll(a => string.Equals(a.Adapter.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    public Contracts.Adapters.IAiAdapter? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        lock (_gate)
        {
            return _registrations.FirstOrDefault(a => string.Equals(a.Adapter.Id, id, StringComparison.OrdinalIgnoreCase))?.Adapter;
        }
    }
}