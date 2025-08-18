using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Messaging.Core.Infrastructure;

namespace Sora.Messaging;

public sealed class MessagingOptions
{
    public string? DefaultBus { get; set; }
    // Default group used by providers when auto-subscribing without explicit Subscriptions
    public string DefaultGroup { get; set; } = "workers";
    // When true, include @v{Version} in type aliases derived from [Message]
    public bool IncludeVersionInAlias { get; set; } = false;
}

public interface IMessageBusFactory
{
    int ProviderPriority { get; }
    string ProviderName { get; }
    (IMessageBus bus, IMessagingCapabilities caps) Create(IServiceProvider sp, string busCode, IConfiguration cfg);
}

public interface IMessageBusSelector
{
    IMessageBus ResolveDefault(IServiceProvider sp);
    IMessageBus Resolve(IServiceProvider sp, string busCode);
}

internal sealed class MessageBusSelector : IMessageBusSelector
{
    private readonly IServiceProvider _sp;
    private readonly IEnumerable<IMessageBusFactory> _factories;
    private readonly IConfiguration _cfg;
    private readonly IOptions<MessagingOptions> _opts;

    public MessageBusSelector(IServiceProvider sp, IEnumerable<IMessageBusFactory> factories, IConfiguration cfg, IOptions<MessagingOptions> opts)
    { _sp = sp; _factories = factories; _cfg = cfg; _opts = opts; }

    public IMessageBus ResolveDefault(IServiceProvider sp)
    {
    var code = _opts.Value.DefaultBus ?? "default";
        return Resolve(sp, code);
    }

    public IMessageBus Resolve(IServiceProvider sp, string busCode)
    {
        var selected = _factories
            .OrderByDescending(f => f.ProviderPriority)
            .ThenBy(f => f.ProviderName)
            .FirstOrDefault();
        if (selected is null) throw new InvalidOperationException("No messaging providers registered.");
    var (bus, caps) = selected.Create(_sp, busCode, _cfg.GetSection($"{Constants.Configuration.Buses}:{busCode}"));
    // Diagnostics are registered by providers when creating the bus
        return bus;
    }
}

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingCore(this IServiceCollection services)
    {
    services.AddOptions<MessagingOptions>().BindConfiguration(Constants.Configuration.Section);
        services.TryAddSingleton<IMessageBusSelector, MessageBusSelector>();
        services.TryAddSingleton<ITypeAliasRegistry, DefaultTypeAliasRegistry>();
    services.TryAddSingleton<IMessagingDiagnostics, MessagingDiagnostics>();
        return services;
    }
}

internal sealed class DefaultTypeAliasRegistry : ITypeAliasRegistry
{
    private readonly ConcurrentDictionary<Type, string> _to = new();
    private readonly ConcurrentDictionary<string, Type> _from = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _includeVersion;
    public DefaultTypeAliasRegistry(IOptions<MessagingOptions> opts)
    {
        _includeVersion = opts.Value.IncludeVersionInAlias;
    }
    public string GetAlias(Type type)
    {
        return _to.GetOrAdd(type, t =>
        {
            // Special case: Batch<T> => "batch:{alias(T)}"
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Batch<>))
            {
                var inner = t.GetGenericArguments()[0];
                var innerAlias = GetAlias(inner);
                var batchAlias = $"batch:{innerAlias}";
                _from[batchAlias] = t;
                return batchAlias;
            }

            var attr = t.GetCustomAttributes(typeof(MessageAttribute), false).FirstOrDefault() as MessageAttribute;
            var baseName = attr?.Alias ?? t.FullName ?? t.Name;
            string final = baseName;
            if (_includeVersion && attr is not null)
            {
                final = $"{baseName}@v{attr.Version}";
                // Accept both versioned and base aliases for resolution
                _from[baseName] = t;
            }
            _from[final] = t;
            return final;
        });
    }
    public Type? Resolve(string alias)
    {
        if (_from.TryGetValue(alias, out var t)) return t;

        // batch:{alias(T)} => Batch<T>
        if (alias.StartsWith("batch:", StringComparison.OrdinalIgnoreCase))
        {
            var innerAlias = alias.Substring("batch:".Length);
            var innerType = Resolve(innerAlias) ?? TryResolveByFullName(innerAlias);
            if (innerType is not null)
            {
                var closed = typeof(Batch<>).MakeGenericType(innerType);
                _from[alias] = closed;
                _to[closed] = alias;
                return closed;
            }
            return null;
        }

        // Fallback: try to resolve by full name using loaded assemblies
        var byName = TryResolveByFullName(alias);
        if (byName is not null)
        {
            // Cache for future lookups
            _from[alias] = byName;
            _to[byName] = alias;
        }
        return byName;
    }

    private static Type? TryResolveByFullName(string fullName)
    {
        var t = Type.GetType(fullName, throwOnError: false);
        if (t is not null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(fullName, throwOnError: false, ignoreCase: false); }
            catch { t = null; }
            if (t is not null) return t;
        }
        return null;
    }
}
