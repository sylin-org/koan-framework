using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Policies;

internal sealed class CachePolicyBootstrapper(
    CachePolicyRegistry registry,
    IOptionsMonitor<CacheOptions> options) : IHostedService
{
    private readonly object _gate = new();
    private AssemblyLoadEventHandler? _handler;
    private bool _initialized;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        lock (_gate)
        {
            if (_handler is null)
            {
                _handler = (_, _) => Rebuild();
                AppDomain.CurrentDomain.AssemblyLoad += _handler;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_handler is not null)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= _handler;
                _handler = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Materializes policies before the first consumer needs them. The startup composition report runs before
    /// later module hosted services, so it uses this same lifecycle owner instead of observing an empty registry.
    /// </summary>
    internal void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            RebuildLocked();
            _initialized = true;
        }
    }

    private void Rebuild()
    {
        lock (_gate)
        {
            RebuildLocked();
            _initialized = true;
        }
    }

    private void RebuildLocked()
    {
        var configured = options.CurrentValue;
        registry.Rebuild(SelectAssemblies(configured));
    }

    private static IEnumerable<Assembly> SelectAssemblies(CacheOptions options)
    {
        var all = AppDomain.CurrentDomain.GetAssemblies();
        if (options.PolicyAssemblies.Count == 0)
        {
            return all;
        }

        var set = new HashSet<string>(options.PolicyAssemblies, StringComparer.OrdinalIgnoreCase);
        return all.Where(a => set.Contains(a.GetName().Name ?? ""));
    }
}
