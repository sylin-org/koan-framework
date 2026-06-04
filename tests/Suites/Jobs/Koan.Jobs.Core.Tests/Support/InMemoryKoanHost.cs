using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Core.Tests.Support;

/// <summary>
/// Boot a minimal Koan host backed by the in-memory data adapter so <see cref="Koan.Jobs.Model.Job{T}"/>
/// statics (Get/Query/Upsert/SaveSelf) resolve to a real repository without Mongo. Mirrors the
/// pattern used by S7.Meridian.Tests.JobCoordinatorTestHost. Background services are disabled so
/// the reaper / worker / recovery loops don't race the test body.
/// </summary>
internal static class InMemoryKoanHost
{
    public static Task<HostScope> Start() => Start(null);

    public static Task<HostScope> Start(Action<IServiceCollection>? configureServices)
    {
        var previousHost = AppHost.Current;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "memory",
                ["Koan:Data:Vector:EnableWorkflows"] = "false",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Logging:EventLog:LogLevel:Default"] = "None"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddKoan();
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();

        AppHost.Current = scope.ServiceProvider;
        KoanEnv.TryInitialize(scope.ServiceProvider);

        return Task.FromResult(new HostScope(previousHost, provider, scope));
    }

    internal sealed class HostScope : IAsyncDisposable
    {
        private readonly IServiceProvider? _previous;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public HostScope(IServiceProvider? previous, ServiceProvider provider, AsyncServiceScope scope)
        {
            _previous = previous;
            _provider = provider;
            _scope = scope;
        }

        /// <summary>Scoped service provider — resolve test dependencies from this.</summary>
        public IServiceProvider Services => _scope.ServiceProvider;

        public async ValueTask DisposeAsync()
        {
            if (ReferenceEquals(AppHost.Current, _scope.ServiceProvider))
            {
                AppHost.Current = _previous;
            }

            await _scope.DisposeAsync().ConfigureAwait(false);
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
