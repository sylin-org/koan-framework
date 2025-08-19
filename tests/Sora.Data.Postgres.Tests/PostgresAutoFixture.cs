using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sora.Testing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Xunit;

namespace Sora.Data.Postgres.Tests;

public sealed class PostgresAutoFixture : IAsyncLifetime, IDisposable
{
    private TestcontainersContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;
    public ServiceProvider ServiceProvider { get; private set; } = default!;
    public IDataService Data { get; private set; } = default!;
    public bool SkipTests { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
    // Disable Testcontainers' resource reaper to avoid Docker hijack issues on some Windows setups
    Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

    // Probe Docker daemon robustly and derive a stable endpoint string for Testcontainers
    var probe = await DockerEnvironment.ProbeAsync();

        var envCxn = Environment.GetEnvironmentVariable("SORA_POSTGRES__CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        if (!string.IsNullOrWhiteSpace(envCxn))
        {
            ConnectionString = envCxn!;
        }
        else
        {
            try
            {
                var password = "postgres";
                var builder = new TestcontainersBuilder<TestcontainersContainer>()
                    .WithDockerEndpoint(probe.Endpoint!)
                    .WithImage("postgres:16-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", password)
                    .WithEnvironment("POSTGRES_DB", "sora")
                    .WithPortBinding(54329, 5432)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                    ;
                _container = builder.Build();
                await _container.StartAsync();
                ConnectionString = $"Host=localhost;Port=54329;Database=sora;Username=postgres;Password={password}";
            }
            catch (Exception ex)
            {
                SkipTests = true;
                SkipReason = $"Docker not available: {ex.GetType().Name}: {ex.Message}";
                return;
            }
        }

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>(Sora.Data.Postgres.Infrastructure.Constants.Configuration.Keys.ConnectionString, ConnectionString),
                new KeyValuePair<string,string?>(Sora.Data.Postgres.Infrastructure.Constants.Configuration.Keys.DefaultPageSize, "10"),
                new KeyValuePair<string,string?>(Sora.Data.Postgres.Infrastructure.Constants.Configuration.Keys.MaxPageSize, "50"),
                new KeyValuePair<string,string?>("Sora:Environment", "Test")
            })
            .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(cfg);
    services.AddSoraCore();
    services.AddSoraDataCore();
    services.AddPostgresAdapter();
    ServiceProvider = services.BuildServiceProvider();
    Data = ServiceProvider.GetRequiredService<IDataService>();
    }

    // Docker probing moved to Sora.Testing.DockerEnvironment

    public async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await Task.Yield();
            ServiceProvider.Dispose();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public void Dispose() { }
}
