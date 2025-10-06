using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Policies;

internal sealed class CachePolicyBootstrapper : IHostedService
{
    private readonly CachePolicyRegistry _registry;
    private readonly IOptionsMonitor<CacheOptions> _options;
    private readonly ILogger<CachePolicyBootstrapper> _logger;
    private AssemblyLoadEventHandler? _handler;

    public CachePolicyBootstrapper(CachePolicyRegistry registry, IOptionsMonitor<CacheOptions> options, ILogger<CachePolicyBootstrapper> logger)
    {
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Rebuild();
        _handler = (_, _) => Rebuild();
        AppDomain.CurrentDomain.AssemblyLoad += _handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null)
        {
            AppDomain.CurrentDomain.AssemblyLoad -= _handler;
            _handler = null;
        }

        return Task.CompletedTask;
    }

    private void Rebuild()
    {
        var opts = _options.CurrentValue;
        var assemblies = SelectAssemblies(opts);
        _registry.Rebuild(assemblies);
    }

    private static IEnumerable<Assembly> SelectAssemblies(CacheOptions options)
    {
        var all = AppDomain.CurrentDomain.GetAssemblies();
        if (options.PolicyAssemblies.Count == 0)
        {
            return all;
        }

        var set = new HashSet<string>(options.PolicyAssemblies, StringComparer.OrdinalIgnoreCase);
        return all.Where(a => set.Contains(a.GetName().Name ?? string.Empty));
    }
}
