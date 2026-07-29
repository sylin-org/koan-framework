using System.Collections.ObjectModel;

namespace Koan.Data.Abstractions.Sources;

/// <summary>An immutable, redacted provider-enforced read route owned by one source.</summary>
public sealed class DataReadLanePlan
{
    public DataReadLanePlan(
        string name,
        string routeIdentity,
        string connectionIdentity,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionIdentity);
        Name = name;
        RouteIdentity = routeIdentity;
        ConnectionIdentity = connectionIdentity;
        Settings = new ReadOnlyDictionary<string, string>(
            settings is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase));
    }

    public string Name { get; }
    public string RouteIdentity { get; }
    public string ConnectionIdentity { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }

    public override string ToString() => $"{Name} (Route={RouteIdentity})";
}
