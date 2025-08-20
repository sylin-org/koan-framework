using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.SqlServer;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.SqlServer.Tests;

public sealed class SqlServerAutoFixture : IAsyncLifetime, IDisposable
{
    private TestcontainersContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;
    public ServiceProvider ServiceProvider { get; private set; } = default!;
    public IDataService Data { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // Prefer env/local connection if provided
        var envCxn = Environment.GetEnvironmentVariable("SORA_SQLSERVER__CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");

        if (!string.IsNullOrWhiteSpace(envCxn))
        {
            ConnectionString = envCxn!;
        }
        else
        {
            // Start a SQL Server container
            var password = "yourStrong(!)Password";
            _container = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithEnvironment("MSSQL_SA_PASSWORD", password)
                .WithPortBinding(14333, 1433)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
                .Build();
            await _container.StartAsync();
            ConnectionString = $"Server=localhost,14333;User Id=sa;Password={password};TrustServerCertificate=True;Encrypt=False";
        }

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>(Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionString, ConnectionString),
                new KeyValuePair<string,string?>("Sora:Environment", "Test"),
                new KeyValuePair<string,string?>(Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.DefaultPageSize, "10"),
                new KeyValuePair<string,string?>(Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.MaxPageSize, "50")
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSoraCore();
        services.AddSoraDataCore();
        services.AddSqlServerAdapter();
        ServiceProvider = services.BuildServiceProvider();
        Data = ServiceProvider.GetRequiredService<IDataService>();

        // Smoke probe
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync();
    }

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

    public void Dispose()
    {
        // no-op
    }
}
