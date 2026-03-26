using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

namespace Koan.Data.Connector.OpenSearch.Tests.Support;

internal sealed class OpenSearchConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _adminHttp;

    private OpenSearchConnectorFixture(ServiceProvider provider, IDataService data, IVectorService vectors, IConfiguration configuration, HttpClient adminHttp, string endpoint)
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

    public static ValueTask<OpenSearchConnectorFixture> Create(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var opensearch = ctx.GetRequiredItem<OpenSearchContainerFixture>("opensearch");
        if (!opensearch.IsAvailable || string.IsNullOrWhiteSpace(opensearch.Endpoint))
        {
            throw new InvalidOperationException($"OpenSearch fixture is unavailable: {opensearch.UnavailableReason ?? "unspecified"}");
        }

        var endpoint = opensearch.Endpoint;
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:Data:VectorDefaults:DefaultProvider"] = "opensearch",
            ["Koan:Data:OpenSearch:Endpoint"] = endpoint,
            ["Koan:Data:OpenSearch:ConnectionString"] = endpoint,
            ["Koan:Data:Vector:Profiles:default:Adapter"] = "opensearch"
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

        return ValueTask.FromResult(new OpenSearchConnectorFixture(provider, data, vectors, configuration, adminHttp, endpoint));
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

        var indexName = typeof(TEntity).Name.ToLowerInvariant();
        var response = await _adminHttp.DeleteAsync($"/{Uri.EscapeDataString(indexName)}").ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Failed to reset OpenSearch index '{indexName}': {(int)response.StatusCode} {body}");
        }
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
