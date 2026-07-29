using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core;

/// <summary>
/// Registry for data sources discovered from configuration or registered programmatically.
///
/// Sources are named configurations that define adapter, connection string, and adapter-specific settings.
/// Discovered from "<see cref="ConfigurationConstants.Sources.Section"/>:{name}" configuration sections.
///
/// Example configuration:
/// {
///   "Koan": {
///     "Data": {
///       "Sources": {
///         "Analytics": {
///           "Adapter": "sqlserver",
///           "ConnectionString": "Server=analytics-db;...",
///           "MaxPageSize": "500"
///         }
///       }
///     }
///   }
/// }
/// </summary>
public sealed class DataSourceRegistry
{
    private readonly object _gate = new();
    private readonly Runtime.BoundedSingleFlightCache<PlanKey, DataSourcePlan> _plans;
    private readonly int _sourceCapacity;
    private ImmutableDictionary<string, SourceDefinition> _sources =
        ImmutableDictionary.Create<string, SourceDefinition>(StringComparer.OrdinalIgnoreCase);
    private int _frozen;

    public DataSourceRegistry() : this(Constants.Defaults.SourceEntries, Constants.Defaults.SourcePlanEntries) { }

    internal DataSourceRegistry(int sourceEntries, int sourcePlanEntries)
    {
        if (sourceEntries <= 0) throw new ArgumentOutOfRangeException(nameof(sourceEntries));
        if (sourcePlanEntries <= 0) throw new ArgumentOutOfRangeException(nameof(sourcePlanEntries));
        _sourceCapacity = sourceEntries;
        _plans = new Runtime.BoundedSingleFlightCache<PlanKey, DataSourcePlan>(
            sourcePlanEntries,
            "source-plan cache");
    }

    /// <summary>
    /// Definition of a data source with adapter and settings.
    /// </summary>
    /// <param name="Name">Source name (e.g., "Analytics", "Backup")</param>
    /// <param name="Adapter">Adapter identifier (e.g., "sqlserver", "mongodb")</param>
    /// <param name="ConnectionString">Connection string for this source</param>
    /// <param name="Settings">Adapter-specific settings (e.g., MaxPageSize, CommandTimeout)</param>
    /// <param name="StorageLifecycle">Whether Koan may mutate the physical storage shape</param>
    /// <param name="Access">Whether Koan may mutate mapped data</param>
    /// <param name="ReadLanes">Provider-enforced read routes owned by this source</param>
    public sealed record SourceDefinition(
        string Name,
        string Adapter,
        string ConnectionString,
        IReadOnlyDictionary<string, string> Settings,
        StorageLifecycle StorageLifecycle = StorageLifecycle.Managed,
        DataSourceAccess Access = DataSourceAccess.ReadWrite,
        IReadOnlyDictionary<string, ReadLaneDefinition>? ReadLanes = null);

    public sealed record ReadLaneDefinition(
        string Name,
        string ConnectionString,
        IReadOnlyDictionary<string, string> Settings);

    /// <summary>
    /// Auto-discover sources from IConfiguration at "<see cref="ConfigurationConstants.Sources.Section"/>:{name}".
    /// Always ensures "Default" source exists (even if empty).
    /// </summary>
    /// <param name="config">Configuration to scan for sources</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public void DiscoverFromConfiguration(IConfiguration config, ILogger? logger = null)
    {
        var sourcesSection = config.GetSection(ConfigurationConstants.Sources.Section);

        foreach (var sourceConfig in sourcesSection.GetChildren())
        {
            var sourceName = sourceConfig.Key;
            var adapter = sourceConfig[ConfigurationConstants.Sources.Adapter];
            var connectionString = sourceConfig[ConfigurationConstants.Sources.Connection] ?? "";
            var storageLifecycle = ReadPolicyValue(
                sourceConfig,
                ConfigurationConstants.Sources.StorageLifecycle,
                StorageLifecycle.Managed);
            var access = ReadPolicyValue(
                sourceConfig,
                ConfigurationConstants.Sources.Access,
                DataSourceAccess.ReadWrite);
            var readLanes = ReadLanes(sourceConfig);

            // Skip sources without explicit adapter (unless it's Default)
            if (string.IsNullOrWhiteSpace(adapter) &&
                !string.Equals(sourceName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogWarning(
                    "Source '{SourceName}' has no adapter configured, skipping auto-discovery",
                    sourceName);
                continue;
            }

            // Policy is compiled separately; only adapter-owned values remain in Settings.
            var settings = sourceConfig.GetChildren()
                .Where(c => !IsFrameworkSourceKey(c.Key))
                .ToDictionary(
                    c => c.Key,
                    c => c.Value ?? "",
                    StringComparer.OrdinalIgnoreCase);

            RegisterSource(new SourceDefinition(
                sourceName,
                adapter ?? "",
                connectionString,
                settings,
                storageLifecycle,
                access,
                readLanes));

            logger?.LogDebug(
                "Discovered source '{SourceName}' with adapter '{Adapter}'",
                sourceName,
                adapter ?? "(none)");
        }

        // Always ensure "Default" source exists (may have empty adapter → resolved by priority)
        if (!_sources.ContainsKey("Default"))
        {
            RegisterSource(new SourceDefinition(
                "Default",
                "",
                "",
                new Dictionary<string, string>()));

            logger?.LogDebug("Created implicit 'Default' source with no adapter (uses priority resolution)");
        }
    }

    /// <summary>
    /// Register a source while the registry owner is composing its finite source set.
    /// </summary>
    /// <param name="source">Source definition to register</param>
    /// <exception cref="ArgumentException">Thrown when source name is empty</exception>
    public void RegisterSource(SourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Source name cannot be empty", nameof(source));

        var declaration = source with
        {
            Settings = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(source.Settings, StringComparer.OrdinalIgnoreCase)),
            ReadLanes = FreezeReadLanes(source.ReadLanes)
        };
        lock (_gate)
        {
            if (Volatile.Read(ref _frozen) != 0)
                throw new InvalidOperationException(
                    $"Data source '{source.Name}' cannot be registered after host composition. " +
                    "Declare it in the initial source configuration before AddKoan() completes.");
            if (_sources.ContainsKey(source.Name))
                throw new InvalidOperationException(
                    $"Data source '{source.Name}' is already declared. One host cannot replace a source decision.");
            if (_sources.Count >= _sourceCapacity)
                throw new InvalidOperationException(
                    $"The host source catalog reached its configured limit of {_sourceCapacity}. " +
                    "Reduce the declared source set or increase Koan Data Runtime SourceEntries.");
            _sources = _sources.Add(source.Name, declaration);
        }
    }

    /// <summary>Freezes the complete source decision set. A frozen registry is read-only and lock-free.</summary>
    internal void Freeze() => Interlocked.Exchange(ref _frozen, 1);

    /// <summary>
    /// Returns the immutable source plan for an already elected adapter and optional resolved physical route.
    /// Raw connection material is reduced to a one-way identity and is never retained by the plan.
    /// </summary>
    public DataSourcePlan GetPlan(string name, string adapter, string? resolvedConnection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);

        var source = GetSource(name);
        var connectionIdentity = Identity(resolvedConnection ?? source?.ConnectionString);
        var key = new PlanKey(Normalize(name), Normalize(adapter), connectionIdentity);
        // Literal/resolved connection values can be unbounded Direct input. They are compiled for the
        // owning session but never admitted to the host source-plan cache.
        if (resolvedConnection is not null)
            return CompilePlan(name, adapter, connectionIdentity, source);
        return _plans.GetOrAdd(key, () => CompilePlan(name, adapter, connectionIdentity, source));
    }

    /// <summary>
    /// Get source definition by name (case-insensitive).
    /// </summary>
    /// <param name="name">Source name</param>
    /// <returns>Source definition or null if not found</returns>
    public SourceDefinition? GetSource(string name)
        => _sources.TryGetValue(name, out var source) ? source : null;

    /// <summary>
    /// Try to get source definition by name (case-insensitive).
    /// </summary>
    /// <param name="name">Source name</param>
    /// <param name="source">Source definition if found</param>
    /// <returns>True if source exists, false otherwise</returns>
    public bool TryGetSource(string name, out SourceDefinition source)
        => _sources.TryGetValue(name, out source!);

    /// <summary>
    /// Get all registered source names.
    /// </summary>
    public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.ToArray();

    /// <summary>
    /// Check if source exists (case-insensitive).
    /// </summary>
    public bool HasSource(string name) => _sources.ContainsKey(name);

    private static DataSourcePlan CompilePlan(
        string name,
        string adapter,
        string connectionIdentity,
        SourceDefinition? source)
    {
        var lifecycle = source?.StorageLifecycle ?? StorageLifecycle.Managed;
        var access = source?.Access ?? DataSourceAccess.ReadWrite;
        var settings = source?.Settings;
        var lanes = CompileReadLanes(name, adapter, source?.ReadLanes);
        var routeIdentity = Identity($"{Normalize(name)}|{Normalize(adapter)}|{connectionIdentity}");
        return new DataSourcePlan(
            name,
            adapter,
            lifecycle,
            access,
            routeIdentity,
            connectionIdentity,
            settings,
            lanes);
    }

    private static T ReadPolicyValue<T>(IConfigurationSection source, string key, T fallback)
        where T : struct, Enum
    {
        var raw = source[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (Enum.TryParse<T>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;

        throw new InvalidOperationException(
            $"Configuration '{source.Path}:{key}' has unsupported value '{raw}'. " +
            $"Use one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static bool IsFrameworkSourceKey(string key) =>
        key.Equals(ConfigurationConstants.Sources.Adapter, StringComparison.OrdinalIgnoreCase) ||
        key.Equals(ConfigurationConstants.Sources.Connection, StringComparison.OrdinalIgnoreCase) ||
        key.Equals(ConfigurationConstants.Sources.StorageLifecycle, StringComparison.OrdinalIgnoreCase) ||
        key.Equals(ConfigurationConstants.Sources.Access, StringComparison.OrdinalIgnoreCase) ||
        key.Equals(ConfigurationConstants.Sources.ReadLanes, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, ReadLaneDefinition> ReadLanes(IConfigurationSection source)
    {
        var lanes = new Dictionary<string, ReadLaneDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var lane in source.GetSection(ConfigurationConstants.Sources.ReadLanes).GetChildren())
        {
            var settings = lane.GetChildren()
                .Where(child => !child.Key.Equals(
                    ConfigurationConstants.Sources.Connection,
                    StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    child => child.Key,
                    child => child.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
            lanes.Add(lane.Key, new ReadLaneDefinition(
                lane.Key,
                lane[ConfigurationConstants.Sources.Connection] ?? string.Empty,
                settings));
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ReadLaneDefinition>(lanes);
    }

    private static IReadOnlyDictionary<string, ReadLaneDefinition> FreezeReadLanes(
        IReadOnlyDictionary<string, ReadLaneDefinition>? lanes)
    {
        var copy = new Dictionary<string, ReadLaneDefinition>(StringComparer.OrdinalIgnoreCase);
        if (lanes is not null)
            foreach (var (name, lane) in lanes)
                copy.Add(name, lane with
                {
                    Settings = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                        new Dictionary<string, string>(lane.Settings, StringComparer.OrdinalIgnoreCase))
                });
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ReadLaneDefinition>(copy);
    }

    private static IReadOnlyDictionary<string, DataReadLanePlan> CompileReadLanes(
        string source,
        string adapter,
        IReadOnlyDictionary<string, ReadLaneDefinition>? lanes)
    {
        var plans = new Dictionary<string, DataReadLanePlan>(StringComparer.OrdinalIgnoreCase);
        if (lanes is not null)
            foreach (var (name, lane) in lanes)
            {
                var connectionIdentity = Identity(lane.ConnectionString);
                var routeIdentity = Identity(
                    $"{Normalize(source)}|{Normalize(adapter)}|READ|{Normalize(name)}|{connectionIdentity}");
                plans.Add(name, new DataReadLanePlan(
                    name,
                    routeIdentity,
                    connectionIdentity,
                    lane.Settings));
            }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, DataReadLanePlan>(plans);
    }

    private static string Identity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private readonly record struct PlanKey(string Source, string Adapter, string ConnectionIdentity);
}
