using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sora.Core;
using Sora.Data.Core;
using Sora.Data.Relational.Tests;
using Sora.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.Postgres.Tests;

public sealed class PostgresAutoFixture : IRelationalTestFixture<PostgresSchemaGovernanceSharedTests.Todo, string>, IAsyncLifetime, IDisposable
{
    private TestcontainersContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;
    public IServiceProvider ServiceProvider { get; private set; } = default!;
    public IDataService Data { get; private set; } = default!;
    public bool SkipTests { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        // Disable Testcontainers' resource reaper to avoid Docker hijack issues on some Windows setups
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        // Probe Docker daemon robustly and derive a stable endpoint string for Testcontainers
        var probe = await DockerEnvironment.ProbeAsync();

        // 1) Explicit env var wins
        var envCxn = Environment.GetEnvironmentVariable("SORA_POSTGRES__CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        if (TryNormalize(envCxn, out var normalized))
        {
            ConnectionString = normalized!;
        }
        else if (await TryDetectLocalAsync() is string localCxn)
        {
            // 2) Local instance discovered
            ConnectionString = localCxn;
        }
        else
        {
            // 3) Fallback to Testcontainers if Docker is available
            try
            {
                if (!probe.Available)
                {
                    SkipTests = true;
                    SkipReason = probe.Message ?? "Docker not available";
                    return;
                }

                var password = "postgres";
                var builder = new TestcontainersBuilder<TestcontainersContainer>()
                    .WithDockerEndpoint(probe.Endpoint!)
                    .WithImage("postgres:16-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", password)
                    .WithEnvironment("POSTGRES_DB", "sora")
                    .WithPortBinding(54329, 5432)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432));

                _container = builder.Build();
                await _container.StartAsync();
                ConnectionString = $"Host=localhost;Port=54329;Database=sora;Username=postgres;Password={password};Timeout=3";
            }
            catch (Exception ex)
            {
                SkipTests = true;
                SkipReason = $"Docker start failed: {ex.GetType().Name}: {ex.Message}";
                return;
            }
        }

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>(Infrastructure.Constants.Configuration.Keys.ConnectionString, ConnectionString),
                new KeyValuePair<string,string?>(Infrastructure.Constants.Configuration.Keys.DefaultPageSize, "10"),
                new KeyValuePair<string,string?>(Infrastructure.Constants.Configuration.Keys.MaxPageSize, "50"),
                new KeyValuePair<string,string?>("Sora:Environment", "Test")
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSoraCore();
        services.AddSoraDataCore();
        services.AddPostgresAdapter(o =>
        {
            o.ConnectionString = ConnectionString;
            o.DdlPolicy = SchemaDdlPolicy.AutoCreate;
            o.AllowProductionDdl = true;
        });
        ServiceProvider = services.BuildServiceProvider();
        Data = ServiceProvider.GetRequiredService<IDataService>();
    }

    // Docker probing moved to Sora.Testing.DockerEnvironment

    public async Task DisposeAsync()
    {
        if (ServiceProvider is IDisposable disp)
        {
            await Task.Yield();
            disp.Dispose();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public void Dispose() { }

    private static bool TryNormalize(string? cxn, out string? normalized)
    {
        if (!string.IsNullOrWhiteSpace(cxn))
        {
            // Ensure short timeout so failures are fast in CI
            var builder = new NpgsqlConnectionStringBuilder(cxn!)
            {
                Timeout = 3,
                KeepAlive = 0
            };
            normalized = builder.ConnectionString;
            return true;
        }
        normalized = null;
        return false;
    }

    private static async Task<bool> CanTcpConnectAsync(string host, int port, int timeoutMs = 250)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CanOpenAsync(string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await conn.CloseAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? BuildFromPgEnv()
    {
        var host = Environment.GetEnvironmentVariable("PGHOST");
        var port = Environment.GetEnvironmentVariable("PGPORT");
        var db = Environment.GetEnvironmentVariable("PGDATABASE");
        var user = Environment.GetEnvironmentVariable("PGUSER");
        var pwd = Environment.GetEnvironmentVariable("PGPASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user)) return null;
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var p) ? p : 5432,
            Database = string.IsNullOrWhiteSpace(db) ? "postgres" : db,
            Username = user,
            Timeout = 3,
        };
        if (!string.IsNullOrWhiteSpace(pwd)) b.Password = pwd;
        return b.ConnectionString;
    }

    private static async Task<string?> TryDetectLocalAsync()
    {
        // PG* environment variables first
        var envCxn = BuildFromPgEnv();
        if (envCxn is not null && await CanOpenAsync(envCxn)) return envCxn;

        // Probe common localhost ports quickly
        var ports = new[] { 5432, 5433, 5434 };
        var host = "localhost";
        var anyOpen = false;
        foreach (var port in ports)
        {
            if (await CanTcpConnectAsync(host, port)) { anyOpen = true; break; }
        }
        if (!anyOpen) return null;

        // Try a few common credential combos
        var users = new[]
        {
            Environment.GetEnvironmentVariable("PGUSER"),
            "postgres",
            Environment.UserName
        }.Where(s => !string.IsNullOrWhiteSpace(s))!.Distinct().ToArray();
        var dbs = new[]
        {
            Environment.GetEnvironmentVariable("PGDATABASE"),
            "sora",
            "postgres"
        }.Where(s => !string.IsNullOrWhiteSpace(s))!.Distinct().ToArray();
        var pwds = new[]
        {
            Environment.GetEnvironmentVariable("PGPASSWORD"),
            "postgres",
            string.Empty
        };

        foreach (var port in ports)
        {
            foreach (var user in users)
            {
                foreach (var db in dbs)
                {
                    foreach (var pwd in pwds)
                    {
                        var b = new NpgsqlConnectionStringBuilder
                        {
                            Host = host,
                            Port = port,
                            Database = db,
                            Username = user!,
                            Timeout = 3,
                        };
                        if (pwd is not null) b.Password = pwd;
                        var cs = b.ConnectionString;
                        if (await CanOpenAsync(cs)) return cs;
                    }
                }
            }
        }

        return null;
    }
}
