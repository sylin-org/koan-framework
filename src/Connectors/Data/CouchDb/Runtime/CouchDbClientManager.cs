using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.CouchDb.Infrastructure;

namespace Koan.Data.Connector.CouchDb.Runtime;

internal sealed record CouchDbRoute(
    string Source,
    string Endpoint,
    string? UserId,
    string? Password,
    string DatabasePrefix,
    DataSourcePlan Policy);

/// <summary>
/// One HTTP client per distinct server endpoint, host-owned and bounded. Two sources on one server
/// share the client when their credentials agree and differ by database prefix otherwise; a disposed
/// host releases every client.
/// </summary>
internal sealed class CouchDbClientManager : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CouchDbClient> _clients = new(StringComparer.Ordinal);
    private const int MaximumClients = 8;

    public CouchDbClient Get(CouchDbRoute route)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(route.Endpoint, out var existing)) return existing;
            if (_clients.Count >= MaximumClients)
                throw new InvalidOperationException(
                    $"CouchDB reached the host bound of {MaximumClients} server endpoints. Reduce routed sources.");
            return _clients[route.Endpoint] = new CouchDbClient(route.Endpoint, route.UserId, route.Password);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var client in _clients.Values) client.Dispose();
            _clients.Clear();
        }
    }
}
