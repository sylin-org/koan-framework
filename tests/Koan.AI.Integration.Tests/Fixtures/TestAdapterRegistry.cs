using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;

namespace Koan.AI.Integration.Tests.Fixtures;

/// <summary>
/// A simple, thread-safe adapter registry for integration tests.
/// Mirrors the registration semantics of <see cref="InMemoryAdapterRegistry"/>
/// without requiring internal access.
/// </summary>
internal sealed class TestAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private readonly List<AiAdapterRegistration> _registrations = [];

    public IReadOnlyList<IAiAdapter> All
    {
        get
        {
            lock (_gate) { return _registrations.Select(r => r.Adapter).ToArray(); }
        }
    }

    public IReadOnlyList<AiAdapterRegistration> Registrations
    {
        get
        {
            lock (_gate) { return _registrations.ToArray(); }
        }
    }

    public void Add(IAiAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        lock (_gate)
        {
            if (_registrations.Any(r =>
                    string.Equals(r.Adapter.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)))
                return;

            _registrations.Add(new AiAdapterRegistration
            {
                Adapter = adapter,
                Priority = 0,
                Weight = 1,
                RegisteredAt = DateTimeOffset.UtcNow
            });
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_gate)
        {
            return _registrations.RemoveAll(r =>
                string.Equals(r.Adapter.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    public IAiAdapter? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            return _registrations
                .FirstOrDefault(r => string.Equals(r.Adapter.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.Adapter;
        }
    }
}
