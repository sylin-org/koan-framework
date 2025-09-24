using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Abstractions;
using Koan.Mcp.Options;
using Koan.Web.Endpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp;

/// <summary>
/// Discovers decorated entities and exposes MCP tool metadata.
/// </summary>
public sealed class McpEntityRegistry
{
    private readonly IEntityEndpointDescriptorProvider _descriptorProvider;
    private readonly DescriptorMapper _descriptorMapper;
    private readonly IOptionsMonitor<McpServerOptions> _optionsMonitor;
    private readonly ILogger<McpEntityRegistry> _logger;

    private readonly object _sync = new();
    private RegistrySnapshot _snapshot = RegistrySnapshot.Empty;
    private bool _initialized;

    public McpEntityRegistry(
        IEntityEndpointDescriptorProvider descriptorProvider,
        DescriptorMapper descriptorMapper,
        IOptionsMonitor<McpServerOptions> optionsMonitor,
        ILogger<McpEntityRegistry> logger)
    {
        _descriptorProvider = descriptorProvider ?? throw new ArgumentNullException(nameof(descriptorProvider));
        _descriptorMapper = descriptorMapper ?? throw new ArgumentNullException(nameof(descriptorMapper));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _optionsMonitor.OnChange(_ => Invalidate());
    }

    /// <summary>
    /// Returns the current entity registrations.
    /// </summary>
    public IReadOnlyList<McpEntityRegistration> Registrations => Snapshot.Items;

    /// <summary>
    /// Attempts to resolve a registration by entity name, friendly name, or type.
    /// </summary>
    public bool TryGetRegistration(string name, out McpEntityRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            registration = null!;
            return false;
        }

        return Snapshot.RegistrationIndex.TryGetValue(name, out registration!);
    }

    /// <summary>
    /// Attempts to resolve a tool definition by its MCP name.
    /// </summary>
    public bool TryGetTool(string toolName, out McpEntityRegistration registration, out McpToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            registration = null!;
            tool = null!;
            return false;
        }

        if (Snapshot.ToolIndex.TryGetValue(toolName, out var entry))
        {
            registration = entry.Registration;
            tool = entry.Tool;
            return true;
        }

        registration = null!;
        tool = null!;
        return false;
    }

    /// <summary>
    /// Returns registrations that have STDIO enabled.
    /// </summary>
    public IReadOnlyList<McpEntityRegistration> RegistrationsForStdio()
        => Snapshot.Items.Where(r => r.EnableStdio).ToList();

    /// <summary>
    /// Returns registrations that have HTTP + SSE enabled.
    /// </summary>
    public IReadOnlyList<McpEntityRegistration> RegistrationsForHttpSse()
        => Snapshot.Items.Where(r => r.EnableHttpSse).ToList();

    private RegistrySnapshot Snapshot
    {
        get
        {
            if (_initialized) return _snapshot;
            lock (_sync)
            {
                if (_initialized) return _snapshot;
                _snapshot = BuildSnapshot();
                _initialized = true;
            }

            return _snapshot;
        }
    }

    private void Invalidate()
    {
        lock (_sync)
        {
            _snapshot = RegistrySnapshot.Empty;
            _initialized = false;
        }
    }

    private RegistrySnapshot BuildSnapshot()
    {
        var options = _optionsMonitor.CurrentValue;
        var registrations = new List<McpEntityRegistration>();
        var registrationIndex = new Dictionary<string, McpEntityRegistration>(StringComparer.OrdinalIgnoreCase);
        var toolIndex = new Dictionary<string, McpRegisteredTool>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in DiscoverEntityTypes())
        {
            var attribute = type.GetCustomAttribute<McpEntityAttribute>();
            if (attribute is null)
            {
                continue;
            }

            var keyType = ResolveKeyType(type);
            if (keyType is null)
            {
                _logger.LogDebug("Skipping {Entity} because it does not implement IEntity<TKey>.", type.FullName);
                continue;
            }

            var entityOverride = ResolveOverride(options, type, attribute);
            var effectiveAttribute = ApplyOverrides(attribute, entityOverride);
            var displayName = effectiveAttribute.Name ?? type.Name;

            if (!IsEntityAllowed(options, type, displayName))
            {
                _logger.LogDebug("Entity {Entity} denied by configuration.", type.FullName);
                continue;
            }

            var descriptor = _descriptorProvider.Describe(type, keyType);
            var tools = _descriptorMapper.Map(type, keyType, descriptor, effectiveAttribute, entityOverride, displayName);
            if (tools.Count == 0)
            {
                _logger.LogDebug("Entity {Entity} produced zero tools. Skipping registration.", type.FullName);
                continue;
            }

            var registration = new McpEntityRegistration(
                type,
                keyType,
                effectiveAttribute,
                descriptor,
                tools,
                displayName,
                effectiveAttribute.EnabledTransports,
                effectiveAttribute.RequireAuthentication ?? entityOverride?.RequireAuthentication);

            registrations.Add(registration);
            AddIndex(registrationIndex, type.FullName, registration);
            AddIndex(registrationIndex, type.Name, registration);
            AddIndex(registrationIndex, displayName, registration);

            foreach (var tool in tools)
            {
                AddTool(toolIndex, tool.Name, registration, tool);
            }
        }

        registrations.Sort((x, y) => string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase));

        return new RegistrySnapshot(
            registrationIndex.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            toolIndex.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            registrations.ToImmutableArray());
    }

    private static void AddIndex(IDictionary<string, McpEntityRegistration> index, string? key, McpEntityRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (index.TryGetValue(key, out var existing) && !ReferenceEquals(existing, registration))
        {
            return;
        }

        index[key] = registration;
    }

    private static void AddTool(IDictionary<string, McpRegisteredTool> index, string name, McpEntityRegistration registration, McpToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (index.ContainsKey(name)) return;
        index[name] = new McpRegisteredTool(registration, tool);
    }

    private static Type? ResolveKeyType(Type entityType)
    {
        return entityType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>))?
            .GetGenericArguments()
            .FirstOrDefault();
    }

    private static IEnumerable<Type> DiscoverEntityTypes()
    {
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type is null) continue;
                if (!type.IsClass || type.IsAbstract) continue;
                if (type.GetCustomAttribute<McpEntityAttribute>() is null) continue;
                yield return type;
            }
        }
    }

    private static McpEntityOverride? ResolveOverride(McpServerOptions options, Type entityType, McpEntityAttribute attribute)
    {
        if (options.EntityOverrides.Count == 0) return null;

        if (options.EntityOverrides.TryGetValue(entityType.FullName ?? string.Empty, out var entityOverride))
        {
            return entityOverride;
        }

        if (options.EntityOverrides.TryGetValue(entityType.Name, out entityOverride))
        {
            return entityOverride;
        }

        if (!string.IsNullOrWhiteSpace(attribute.Name) && options.EntityOverrides.TryGetValue(attribute.Name, out entityOverride))
        {
            return entityOverride;
        }

        return null;
    }

    private static McpEntityAttribute ApplyOverrides(McpEntityAttribute attribute, McpEntityOverride? entityOverride)
    {
        if (entityOverride is null)
        {
            return attribute;
        }

        var scopes = entityOverride.RequiredScopes.Length > 0
            ? entityOverride.RequiredScopes
            : attribute.RequiredScopes;

        var merged = new McpEntityAttribute
        {
            Name = entityOverride.Name ?? attribute.Name,
            Description = entityOverride.Description ?? attribute.Description,
            AllowMutations = entityOverride.AllowMutations ?? attribute.AllowMutations,
            SchemaOverride = entityOverride.SchemaOverride ?? attribute.SchemaOverride,
            ToolPrefix = attribute.ToolPrefix,
            RequireAuthentication = entityOverride.RequireAuthentication ?? attribute.RequireAuthentication,
            RequiredScopes = scopes.Length == 0 ? Array.Empty<string>() : scopes.ToArray()
        };

        merged.EnableStdio = entityOverride.EnableStdio ?? attribute.EnableStdio;
        merged.EnableHttpSse = entityOverride.EnableHttpSse ?? attribute.EnableHttpSse;

        if (entityOverride.EnabledTransports is McpTransportMode transports)
        {
            merged.EnabledTransports = transports;
        }

        return merged;
    }

    private static bool IsEntityAllowed(McpServerOptions options, Type entityType, string displayName)
    {
        if (options.DeniedEntities.Count > 0 && Matches(options.DeniedEntities, entityType, displayName))
        {
            return false;
        }

        if (options.AllowedEntities.Count == 0)
        {
            return true;
        }

        return Matches(options.AllowedEntities, entityType, displayName);
    }

    private static bool Matches(ISet<string> set, Type entityType, string displayName)
    {
        if (set.Count == 0) return false;

        return set.Contains(entityType.FullName ?? string.Empty)
            || set.Contains(entityType.Name)
            || (!string.IsNullOrWhiteSpace(displayName) && set.Contains(displayName));
    }

    private sealed record RegistrySnapshot(
        ImmutableDictionary<string, McpEntityRegistration> RegistrationIndex,
        ImmutableDictionary<string, McpRegisteredTool> ToolIndex,
        ImmutableArray<McpEntityRegistration> Items)
    {
        public static RegistrySnapshot Empty { get; } = new(
            ImmutableDictionary<string, McpEntityRegistration>.Empty,
            ImmutableDictionary<string, McpRegisteredTool>.Empty,
            ImmutableArray<McpEntityRegistration>.Empty);
    }

    private sealed record McpRegisteredTool(McpEntityRegistration Registration, McpToolDefinition Tool);
}
