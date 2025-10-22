using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Weaviate.Tests.Support;

internal sealed class WeaviateConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _adminHttp;

    private WeaviateConnectorFixture(ServiceProvider provider, IDataService data, IVectorService vectors, IConfiguration configuration, HttpClient adminHttp, string endpoint)
    {
        _provider = provider;
        Data = data;
        Vectors = vectors;
        _configuration = configuration;
        _adminHttp = adminHttp;
        Endpoint = endpoint;
    }

    public IServiceProvider Services => _provider;

    public IDataService Data { get; }

    public IVectorService Vectors { get; }

    public IConfiguration Configuration => _configuration;

    public string Endpoint { get; }

    public static ValueTask<WeaviateConnectorFixture> CreateAsync(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var weaviate = ctx.GetWeaviateFixture();
        if (!weaviate.IsAvailable || string.IsNullOrWhiteSpace(weaviate.Endpoint))
        {
            throw new InvalidOperationException($"Weaviate fixture is unavailable: {weaviate.UnavailableReason ?? "unspecified"}");
        }

        var endpoint = weaviate.Endpoint;
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:Data:VectorDefaults:DefaultProvider"] = "weaviate",
            ["Koan:Data:Weaviate:Endpoint"] = endpoint,
            ["Koan:Data:Weaviate:ConnectionString"] = endpoint,
            ["Koan:Data:Vector:Profiles:default:Adapter"] = "weaviate"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
    services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
    services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKoan();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        try
        {
            KoanEnv.TryInitialize(provider);
        }
        catch
        {
        }

        AppHost.Current = provider;
        var data = provider.GetRequiredService<IDataService>();
        var vectors = provider.GetRequiredService<IVectorService>();
        var adminHttp = new HttpClient { BaseAddress = new Uri(endpoint, UriKind.Absolute) };

        return ValueTask.FromResult(new WeaviateConnectorFixture(provider, data, vectors, configuration, adminHttp, endpoint));
    }

    public void BindHost()
    {
        AppHost.Current = _provider;
    }

    public async Task ResetAsync<TEntity>()
        where TEntity : class, IEntity<string>
    {
        BindHost();
        TestHooks.ResetDataConfigs();

        var className = ResolveClassName<TEntity>();
        if (string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        var response = await _adminHttp.DeleteAsync($"/v1/schema/{Uri.EscapeDataString(className)}").ConfigureAwait(false);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException($"Failed to reset Weaviate schema '{className}': {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AppHost.Current, _provider))
        {
            AppHost.Current = null;
        }

        TestHooks.ResetDataConfigs();
        _adminHttp.Dispose();
        return DisposeAsyncCore();
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (_provider is IAsyncDisposable asyncDisposable)
        {
            try
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch
            {
            }
        }

        _provider.Dispose();
    }

    private string ResolveClassName<TEntity>()
    {
        var providers = _provider.GetServices<Koan.Data.Abstractions.Naming.INamingDefaultsProvider>();
        var resolver = _provider.GetRequiredService<Koan.Data.Abstractions.Naming.IStorageNameResolver>();
        var weaviateProvider = providers.FirstOrDefault(p => string.Equals(p.Provider, "weaviate", StringComparison.OrdinalIgnoreCase));
        if (weaviateProvider is null)
        {
            return typeof(TEntity).Name;
        }

        var convention = weaviateProvider.GetConvention(_provider);
        var overrideFn = weaviateProvider.GetAdapterOverride(_provider);
        return Koan.Data.Abstractions.Naming.StorageNameSelector.ResolveName(null, resolver, typeof(TEntity), convention, overrideFn);
    }

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
