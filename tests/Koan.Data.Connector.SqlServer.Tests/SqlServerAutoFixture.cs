using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Relational.Tests;
using Koan.Testing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Koan.Data.Connector.SqlServer.Tests;

public sealed class SqlServerAutoFixture : IRelationalTestFixture<SqlServerSchemaGovernanceTests.Doc, string>, IAsyncLifetime, IDisposable
{
    private TestcontainersContainer? _container;
    private string? _localDbName;
    public string ConnectionString { get; private set; } = string.Empty;
    public IServiceProvider ServiceProvider { get; private set; } = default!;
    public IDataService Data { get; private set; } = default!;
    public bool SkipTests { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        // Prefer env/local connection if provided
        var envCxn = Environment.GetEnvironmentVariable("Koan_SQLSERVER__CONNECTION_STRING")
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
            }

            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                // Fallback to Docker if available; otherwise skip
                try
                {
                    Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
                    var probe = await DockerEnvironment.ProbeAsync();
                    if (!probe.Available)
                    {
                        SkipTests = true;
                        SkipReason = probe.Message ?? "Docker not available";
                        return;
                    }
                    var password = "yourStrong(!)Password";
                    _container = new TestcontainersBuilder<TestcontainersContainer>()
                        .WithDockerEndpoint(probe.Endpoint!)
                        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                        .WithEnvironment("ACCEPT_EULA", "Y")
                        .WithEnvironment("MSSQL_SA_PASSWORD", password)
                        .WithPortBinding(14333, 1433)
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
                        .Build();
                    await _container.StartAsync();
                    ConnectionString = $"Server=localhost,14333;User Id=sa;Password={password};TrustServerCertificate=True;Encrypt=False;Connect Timeout=5";
                }
                catch (Exception ex)
                {
                    SkipTests = true;
                    SkipReason = $"Docker start failed: {ex.Message}";
                    return;
                }
            }
        }

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>(Infrastructure.Constants.Configuration.Keys.ConnectionString, ConnectionString),
                new KeyValuePair<string,string?>("Koan:Environment", "Test"),
                new KeyValuePair<string,string?>(Infrastructure.Constants.Configuration.Keys.DefaultPageSize, "5"),
                new KeyValuePair<string,string?>(Infrastructure.Constants.Configuration.Keys.MaxPageSize, "50")
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddKoanCore();
        services.AddKoanDataCore();
        services.AddSqlServerAdapter();
        ServiceProvider = services.BuildServiceProvider();
        Data = ServiceProvider.GetRequiredService<IDataService>();

        if (!SkipTests)
        {
            // Smoke probe
            await using var smokeConn = new SqlConnection(ConnectionString);
            await smokeConn.OpenAsync();
            await using var smokeCmd = new SqlCommand("SELECT 1", smokeConn);
            await smokeCmd.ExecuteScalarAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await Task.Yield();
            try { (ServiceProvider as IDisposable)?.Dispose(); } catch { }
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
            _localDbName = "Koan_test_" + Guid.NewGuid().ToString("N");
            await using (var masterCmd = new SqlCommand($"CREATE DATABASE [{_localDbName}];", masterConn))
            {
                await masterCmd.ExecuteNonQueryAsync();
            }
            ConnectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_localDbName};Integrated Security=True;TrustServerCertificate=True;";
            return true;
        }
        catch { return false; }
    }

    // SQL Express probe removed: prefer LocalDB (Windows) and container fallback for CI reproducibility.
}

