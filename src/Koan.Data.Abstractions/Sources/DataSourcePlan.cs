using System.Collections.ObjectModel;

namespace Koan.Data.Abstractions.Sources;

/// <summary>
/// Immutable, redacted source decision consumed by every data execution path in one Koan host.
/// </summary>
public sealed class DataSourcePlan
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    private static readonly IReadOnlyDictionary<string, DataReadLanePlan> EmptyReadLanes =
        new ReadOnlyDictionary<string, DataReadLanePlan>(
            new Dictionary<string, DataReadLanePlan>(StringComparer.OrdinalIgnoreCase));

    public static DataSourcePlan Default { get; } = new(
        "Default",
        "unresolved",
        StorageLifecycle.Managed,
        DataSourceAccess.ReadWrite,
        "default",
        "unresolved",
        EmptySettings,
        EmptyReadLanes);

    public DataSourcePlan(
        string source,
        string adapter,
        StorageLifecycle storageLifecycle,
        DataSourceAccess access,
        string routeIdentity,
        string connectionIdentity,
        IReadOnlyDictionary<string, string>? settings = null,
        IReadOnlyDictionary<string, DataReadLanePlan>? readLanes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionIdentity);

        Source = source;
        Adapter = adapter;
        StorageLifecycle = storageLifecycle;
        Access = access;
        RouteIdentity = routeIdentity;
        ConnectionIdentity = connectionIdentity;
        Settings = settings is null || settings.Count == 0
            ? EmptySettings
            : new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase));
        ReadLanes = readLanes is null || readLanes.Count == 0
            ? EmptyReadLanes
            : new ReadOnlyDictionary<string, DataReadLanePlan>(
                new Dictionary<string, DataReadLanePlan>(readLanes, StringComparer.OrdinalIgnoreCase));
    }

    public string Source { get; }
    public string Adapter { get; }
    public StorageLifecycle StorageLifecycle { get; }
    public DataSourceAccess Access { get; }
    public string RouteIdentity { get; }
    public string ConnectionIdentity { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }
    public IReadOnlyDictionary<string, DataReadLanePlan> ReadLanes { get; }

    public bool UsesLegacyProvisioningReadiness =>
        StorageLifecycle == StorageLifecycle.Managed && Access == DataSourceAccess.ReadWrite;

    /// <summary>Rejects an operation that would exceed this immutable source ceiling.</summary>
    public void Demand(DataOperationEffect effect, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (effect == DataOperationEffect.Unknown &&
            (StorageLifecycle != StorageLifecycle.Managed || Access != DataSourceAccess.ReadWrite))
        {
            throw new DataSourcePolicyException(
                Source,
                operation,
                effect,
                StorageLifecycle,
                Access,
                DataSourcePolicyException.UnknownEffectCode,
                "Declare and validate the operation effect, or use an unrestricted Managed + ReadWrite source.");
        }

        if (Access == DataSourceAccess.ReadOnly && effect is DataOperationEffect.Write or DataOperationEffect.SchemaOrAdmin)
        {
            throw new DataSourcePolicyException(
                Source,
                operation,
                effect,
                StorageLifecycle,
                Access,
                DataSourcePolicyException.PolicyDeniedCode,
                "Use a proven read, or explicitly configure Access=ReadWrite with appropriately privileged credentials.");
        }

        if (StorageLifecycle == StorageLifecycle.External && effect == DataOperationEffect.SchemaOrAdmin)
        {
            throw new DataSourcePolicyException(
                Source,
                operation,
                effect,
                StorageLifecycle,
                Access,
                DataSourcePolicyException.PolicyDeniedCode,
                "Provision or repair the target outside Koan, or explicitly configure StorageLifecycle=Managed.");
        }
    }

    public override string ToString() =>
        $"{Source} ({Adapter}; StorageLifecycle={StorageLifecycle}; Access={Access}; Route={RouteIdentity})";
}
