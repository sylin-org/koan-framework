using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Sora.Secrets.Abstractions;
using Sora.Secrets.Core.Providers;
using Sora.Secrets.Core.Resolver;
using System.Threading;

namespace Sora.Secrets.Core.Configuration;

public sealed class SecretResolvingConfigurationSource(IServiceProvider? serviceProvider, IConfiguration baseConfig) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(serviceProvider, baseConfig);

    private sealed class Provider : IConfigurationProvider
    {
        private static readonly object _gate = new();
        private static readonly List<WeakReference<Provider>> _all = new();
        private readonly IConfiguration _base;
        private readonly IServiceProvider? _sp;
        private ISecretResolver _resolver;
        private ConfigurationReloadToken _reload = new();

        public Provider(IServiceProvider? sp, IConfiguration baseConfig)
        {
            _sp = sp;
            _base = baseConfig;
            // bootstrap chain: env + config
            var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
            _resolver = new ChainSecretResolver(new ISecretProvider[]
            {
                new EnvSecretProvider(),
                new ConfigurationSecretProvider(_base),
            }, cache, null);

            // Track instance for post-DI upgrade
            lock (_gate)
            {
                // purge dead entries
                _all.RemoveAll(wr => !wr.TryGetTarget(out _));
                _all.Add(new WeakReference<Provider>(this));
            }
        }

        public bool TryGet(string key, out string? value)
        {
            var ok = _base.AsEnumerable(true).FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Key is not null;
            value = _base[key];
            if (value is { Length: > 0 } && value.Contains("${secret://", StringComparison.Ordinal))
            {
                value = _resolver.ResolveAsync(value).GetAwaiter().GetResult();
            }
            else if (value is { Length: > 0 } && (value.StartsWith("secret://", StringComparison.Ordinal) || value.StartsWith("secret+", StringComparison.Ordinal)))
            {
                var sv = _resolver.GetAsync(SecretId.Parse(value!)).GetAwaiter().GetResult();
                value = sv.AsString();
            }
            return ok;
        }

        public void Set(string key, string? value) => _base[key] = value;
        public IChangeToken GetReloadToken() => _reload;
        public void Load() { }
        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath) => _base.GetSection(parentPath ?? string.Empty).GetChildren().Select(c => c.Key);

        public void UpgradeResolverFrom(IServiceProvider sp)
        {
            var r = sp.GetService<ISecretResolver>();
            if (r is not null)
            {
                _resolver = r;
                // Swap token before firing to avoid recursive re-registration stack overflow
                var previous = Interlocked.Exchange(ref _reload, new ConfigurationReloadToken());
                previous.OnReload();
            }
        }

        public static void UpgradeAll(IServiceProvider sp)
        {
            List<WeakReference<Provider>> snapshot;
            lock (_gate)
            {
                // take a snapshot to avoid holding the lock while upgrading
                _all.RemoveAll(wr => !wr.TryGetTarget(out _));
                snapshot = _all.ToList();
            }
            foreach (var wr in snapshot)
            {
                if (wr.TryGetTarget(out var p))
                {
                    p.UpgradeResolverFrom(sp);
                }
            }
        }
    }
}