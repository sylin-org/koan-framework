using System.Collections.Concurrent;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Testing.Tests;

public sealed class ConformanceOwnershipProbeModule : KoanModule
{
    internal const string OwnerConfigurationKey = "Koan:Testing:ConformanceOwnershipProbe:Owner";

    private static readonly ConcurrentDictionary<string, IServiceProvider> Markers =
        new(StringComparer.Ordinal);

    public override void Register(IServiceCollection services)
        => services.AddSingleton<IHostedService, OwnershipProbeHostedService>();

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);

    internal static IServiceProvider GetRequiredMarker(string owner)
        => Markers.TryGetValue(owner, out var marker)
            ? marker
            : throw new InvalidOperationException($"No conformance ownership marker was started for '{owner}'.");

    internal static bool HasMarker(string owner) => Markers.ContainsKey(owner);

    private sealed class OwnershipProbeHostedService(
        IConfiguration configuration,
        IServiceProvider hostServices) : IHostedService, IDisposable
    {
        private IDisposable? _lease;
        private string? _owner;
        private IServiceProvider? _marker;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var owner = configuration[OwnerConfigurationKey];
            if (string.IsNullOrWhiteSpace(owner))
            {
                return Task.CompletedTask;
            }

            Dispose();
            _owner = owner;
            _marker = new DelegatingServiceProvider(hostServices);
            Markers[owner] = _marker;
            _lease = AppHost.Attach(_marker);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();

            var owner = Interlocked.Exchange(ref _owner, null);
            var marker = Interlocked.Exchange(ref _marker, null);
            if (owner is not null
                && marker is not null
                && Markers.TryGetValue(owner, out var current)
                && ReferenceEquals(current, marker))
            {
                Markers.TryRemove(owner, out _);
            }
        }
    }

    private sealed class DelegatingServiceProvider(IServiceProvider services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetService(serviceType);
    }
}
