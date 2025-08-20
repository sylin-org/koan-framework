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
using System.IO.Pipes;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.SqlServer.Tests;

public sealed class SqlServerAutoFixture : IAsyncLifetime, IDisposable
{
    private TestcontainersContainer? _container;
    private string? _localDbName;
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
            // Try LocalDB on Windows first to avoid Docker dependency
            if (OperatingSystem.IsWindows())
            {
                if (await TryUseLocalDbAsync()) { /* done */ }
                else if (await TryUseSqlExpressAsync()) { /* done */ }
            }

            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                // Start a SQL Server container only if explicitly enabled
                var useDocker = (Environment.GetEnvironmentVariable("SORA_SQLSERVER__USE_DOCKER") ?? string.Empty).Trim();
                if (string.Equals(useDocker, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(useDocker, "true", StringComparison.OrdinalIgnoreCase))
                {
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
                else
                {
                    throw new InvalidOperationException("No SQL Server available. Options: set SORA_SQLSERVER__CONNECTION_STRING, install LocalDB or SQL Express, or set SORA_SQLSERVER__USE_DOCKER=1 with Docker running.");
                }
            }
        }

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>(Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionString, ConnectionString),
                new KeyValuePair<string,string?>("Sora:Environment", "Test"),
                new KeyValuePair<string,string?>(Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.DefaultPageSize, "5"),
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
    await using var smokeConn = new SqlConnection(ConnectionString);
    await smokeConn.OpenAsync();
    await using var smokeCmd = new SqlCommand("SELECT 1", smokeConn);
    await smokeCmd.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await Task.Yield();
            ServiceProvider.Dispose();
        }
        if (_localDbName is not null)
        {
            try
            {
                var master = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;";
                await using var masterConn = new SqlConnection(master);
                await masterConn.OpenAsync();
                await using var masterCmd = new SqlCommand($"ALTER DATABASE [{_localDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_localDbName}];", masterConn);
                await masterCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // best effort cleanup
            }
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

    private async Task<bool> TryUseLocalDbAsync()
    {
        try
        {
            // Ensure LocalDB instance exists and is started
            var psi = new ProcessStartInfo
            {
                FileName = "sqllocaldb",
                Arguments = "start MSSQLLocalDB",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(2000));

            var master = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=5;";
            await using var masterConn = new SqlConnection(master);
            await masterConn.OpenAsync();
            _localDbName = "sora_test_" + Guid.NewGuid().ToString("N");
            await using (var masterCmd = new SqlCommand($"CREATE DATABASE [{_localDbName}];", masterConn))
            {
                await masterCmd.ExecuteNonQueryAsync();
            }
            ConnectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_localDbName};Integrated Security=True;TrustServerCertificate=True;";
            return true;
        }
        catch { return false; }
    }

    private async Task<bool> TryUseSqlExpressAsync()
    {
        try
        {
            var master = "Server=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=5;";
            await using var masterConn = new SqlConnection(master);
            await masterConn.OpenAsync();
            _localDbName = "sora_test_" + Guid.NewGuid().ToString("N");
            await using (var masterCmd = new SqlCommand($"CREATE DATABASE [{_localDbName}];", masterConn))
            {
                await masterCmd.ExecuteNonQueryAsync();
            }
            ConnectionString = $"Server=.\\SQLEXPRESS;Database={_localDbName};Integrated Security=True;TrustServerCertificate=True;";
            return true;
        }
        catch { return false; }
    }
}
