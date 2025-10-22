using Koan.Data.Abstractions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Attributes;
using Koan.Data.Backup.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Koan.Data.Backup.Core;

public class EntityDiscoveryService : IEntityDiscoveryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityDiscoveryService> _logger;
    private readonly ConcurrentDictionary<string, EntityDiscoveryResult> _discoveryCache = new();
    private EntityDiscoveryResult? _currentDiscovery;

    public EntityDiscoveryService(IServiceProvider serviceProvider, ILogger<EntityDiscoveryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<EntityDiscoveryResult> DiscoverAllEntitiesAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting entity discovery...");

        var result = new EntityDiscoveryResult
        {
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
            result.TotalAssembliesScanned = assemblies.Count;

            var entityTypes = new List<EntityTypeInfo>();
            var totalTypesExamined = 0;

            foreach (var assembly in assemblies)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var types = assembly.GetTypes();
                    totalTypesExamined += types.Length;

                    var entityTypesInAssembly = types
                        .Where(IsEntityType)
                        .ToList();

                    foreach (var type in entityTypesInAssembly)
                    {
                        ct.ThrowIfCancellationRequested();

                        var keyType = GetEntityKeyType(type);
                        if (keyType != null)
                        {
                            var entityInfo = new EntityTypeInfo
                            {
                                EntityType = type,
                                KeyType = keyType,
                                Assembly = assembly.FullName,
                                Provider = ResolveProvider(type),
                                Sets = DiscoverEntitySets(type),
                                IsActive = true
                            };

                            entityTypes.Add(entityInfo);
                            _logger.LogDebug("Discovered entity: {EntityType} with key {KeyType} from {Assembly}",
                                type.Name, keyType.Name, assembly.GetName().Name);
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    _logger.LogWarning("Could not load types from assembly {Assembly}: {Error}",
                        assembly.FullName, ex.Message);
                    result.Errors.Add($"Assembly {assembly.GetName().Name}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing assembly {Assembly}", assembly.FullName);
                    result.Errors.Add($"Assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }

            result.Entities = entityTypes;
            result.TotalTypesExamined = totalTypesExamined;
            result.DiscoveryDuration = stopwatch.Elapsed;

            _logger.LogInformation("Entity discovery completed. Found {EntityCount} entities from {AssemblyCount} assemblies in {Duration}ms",
                entityTypes.Count, assemblies.Count, stopwatch.ElapsedMilliseconds);

            // Cache the result
            var cacheKey = ComputeAssemblyHash(assemblies);
            result.AssemblyHash = cacheKey;
            _discoveryCache[cacheKey] = result;
            _currentDiscovery = result;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Entity discovery failed");
            result.Errors.Add($"Discovery failed: {ex.Message}");
            result.DiscoveryDuration = stopwatch.Elapsed;
        }

        return Task.FromResult(result);
    }

    public async Task WarmupAllEntitiesAsync(CancellationToken ct = default)
    {
        var discovered = _currentDiscovery ?? await DiscoverAllEntitiesAsync(ct);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Warming up {Count} discovered entities...", discovered.Entities.Count);

        var warmedUp = 0;
        var failed = 0;

        foreach (var entity in discovered.Entities)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                AggregateConfigsExtensions.PreRegisterEntityByReflection(entity.EntityType, entity.KeyType, _serviceProvider);
                warmedUp++;

                if (warmedUp % 10 == 0)
                {
                    _logger.LogDebug("Warmed up {Count}/{Total} entities...", warmedUp, discovered.Entities.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up entity {EntityType}", entity.EntityType.Name);
                failed++;
            }
        }

        _logger.LogInformation("Entity warmup completed. Warmed up {WarmedUp} entities, {Failed} failures in {Duration}ms",
            warmedUp, failed, stopwatch.ElapsedMilliseconds);
    }

    public IEnumerable<EntityTypeInfo> GetDiscoveredEntities()
    {
        // First try to get from current discovery
        if (_currentDiscovery != null)
            return _currentDiscovery.Entities;

        // Fall back to AggregateConfigs if no discovery has been run
        return AggregateConfigsExtensions.GetAllRegisteredEntities();
    }

    public async Task RefreshDiscoveryAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing entity discovery cache...");
        _discoveryCache.Clear();
        _currentDiscovery = null;
        await DiscoverAllEntitiesAsync(ct);
    }

    public EntityDiscoveryResult GetDiscoveryStats()
    {
        return _currentDiscovery ?? new EntityDiscoveryResult
        {
            Entities = AggregateConfigsExtensions.GetAllRegisteredEntities().ToList(),
            DiscoveredAt = DateTimeOffset.UtcNow,
            DiscoveryDuration = TimeSpan.Zero
        };
    }

    private static bool IsEntityType(Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            return false;

        return type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IEntity<>));
    }

    private static Type? GetEntityKeyType(Type entityType)
    {
        var entityInterface = entityType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>));

        return entityInterface?.GetGenericArguments().FirstOrDefault();
    }

    private static string ResolveProvider(Type entityType)
    {
        // Look for SourceAdapter or DataAdapter attributes
        var sourceAdapter = entityType.GetCustomAttribute<SourceAdapterAttribute>();
        if (sourceAdapter != null && !string.IsNullOrWhiteSpace(sourceAdapter.Provider))
            return sourceAdapter.Provider;

        var dataAdapter = entityType.GetCustomAttribute<DataAdapterAttribute>();
        if (dataAdapter != null && !string.IsNullOrWhiteSpace(dataAdapter.Provider))
            return dataAdapter.Provider;

        return "default";
    }

    private static List<string> DiscoverEntitySets(Type entityType)
    {
        var sets = new List<string> { "root" };

        // Look for any set-related attributes or conventions
        // This could be enhanced to discover actual sets from usage patterns
        var setAttributes = entityType.GetCustomAttributes()
            .Where(attr => attr.GetType().Name.Contains("Set"))
            .ToList();

        foreach (var attr in setAttributes)
        {
            // Extract set names from attributes if they exist
            var setName = attr.ToString();
            if (!string.IsNullOrWhiteSpace(setName) && !sets.Contains(setName))
                sets.Add(setName);
        }

        return sets;
    }

    private static string ComputeAssemblyHash(IEnumerable<Assembly> assemblies)
    {
        var combined = string.Join("|", assemblies
            .Select(a => $"{a.GetName().Name}:{a.GetName().Version}")
            .OrderBy(x => x));

        return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(combined)).ToLowerInvariant();
    }

    public Task<BackupInventory> BuildInventoryAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Building backup inventory...");

        var inventory = new BackupInventory
        {
            GeneratedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // Get all discovered or registered entities
            var entityTypes = GetDiscoveredEntities().ToList();

            _logger.LogDebug("Processing {Count} entity types for backup inventory", entityTypes.Count);

            // Group entities by assembly for efficient scope resolution
            var entitiesByAssembly = entityTypes
                .GroupBy(e => e.EntityType.Assembly)
                .ToList();

            foreach (var assemblyGroup in entitiesByAssembly)
            {
                ct.ThrowIfCancellationRequested();

                var assembly = assemblyGroup.Key;
                var assemblyScope = GetAssemblyBackupScope(assembly);

                foreach (var entityTypeInfo in assemblyGroup)
                {
                    ct.ThrowIfCancellationRequested();

                    var entityType = entityTypeInfo.EntityType;
                    var entityAttr = entityType.GetCustomAttribute<EntityBackupAttribute>();

                    var policy = ResolveBackupPolicy(entityType, assemblyScope, entityAttr);

                    if (policy.IsIncluded)
                    {
                        inventory.IncludedEntities.Add(policy);
                        _logger.LogDebug("Entity {EntityName} included in backup: encrypt={Encrypt}, schema={IncludeSchema} (via {Source})",
                            policy.EntityName, policy.Encrypt, policy.IncludeSchema, policy.Source);
                    }
                    else if (policy.IsExcluded)
                    {
                        inventory.ExcludedEntities.Add(policy);
                        if (!string.IsNullOrWhiteSpace(policy.Reason))
                        {
                            _logger.LogDebug("Entity {EntityName} excluded from backup: {Reason}",
                                policy.EntityName, policy.Reason);
                        }
                        else
                        {
                            inventory.Warnings.Add($"Entity {policy.EntityName} excluded without documented reason");
                        }
                    }
                    else
                    {
                        // Entity has no coverage - generate warning
                        var scopeMode = assemblyScope?.Mode.ToString() ?? "None";
                        var warning = $"Entity {policy.EntityName} has no backup coverage (assembly scope: {scopeMode})";
                        inventory.Warnings.Add(warning);
                        _logger.LogWarning(warning);
                    }
                }
            }

            _logger.LogInformation(
                "Backup inventory built in {Duration}ms: {Included} included, {Excluded} excluded, {Warnings} warnings",
                stopwatch.ElapsedMilliseconds,
                inventory.TotalIncludedEntities,
                inventory.TotalExcludedEntities,
                inventory.TotalWarnings);

            return Task.FromResult(inventory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build backup inventory");
            inventory.Warnings.Add($"Inventory build failed: {ex.Message}");
            return Task.FromResult(inventory);
        }
    }

    private static EntityBackupScopeAttribute? GetAssemblyBackupScope(Assembly assembly)
    {
        return assembly.GetCustomAttribute<EntityBackupScopeAttribute>();
    }

    private static EntityBackupPolicy ResolveBackupPolicy(
        Type entityType,
        EntityBackupScopeAttribute? assemblyScope,
        EntityBackupAttribute? entityAttr)
    {
        var policy = new EntityBackupPolicy
        {
            EntityType = entityType
        };

        // Explicit opt-out takes precedence
        if (entityAttr?.Enabled == false)
        {
            policy.IsIncluded = false;
            policy.Reason = entityAttr.Reason;
            policy.Source = "Attribute";
            return policy;
        }

        // Determine inclusion based on scope and attribute
        var isIncludedByScope = assemblyScope?.Mode == BackupScope.All;
        var hasEntityAttribute = entityAttr != null;

        if (hasEntityAttribute)
        {
            // Entity explicitly opts in
            policy.IsIncluded = true;
            policy.Source = "Attribute";
            policy.Encrypt = entityAttr!.Encrypt;
            policy.IncludeSchema = entityAttr.IncludeSchema;
        }
        else if (isIncludedByScope)
        {
            // Entity included via assembly scope
            policy.IsIncluded = true;
            policy.Source = "Assembly";
            policy.Encrypt = assemblyScope!.EncryptByDefault;
            policy.IncludeSchema = true; // Default
        }
        else
        {
            // No coverage
            policy.IsIncluded = false;
            policy.Source = "Default";
        }

        return policy;
    }
}

// Placeholder attributes - these would need to be defined in the appropriate modules
public class SourceAdapterAttribute : Attribute
{
    public string Provider { get; }
    public SourceAdapterAttribute(string provider) => Provider = provider;
}

public class DataAdapterAttribute : Attribute
{
    public string Provider { get; }
    public DataAdapterAttribute(string provider) => Provider = provider;
}