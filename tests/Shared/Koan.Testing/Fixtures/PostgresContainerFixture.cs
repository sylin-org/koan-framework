using System.Net.Sockets;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;
using Npgsql;

namespace Koan.Testing.Fixtures;

public sealed class PostgresContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const string DefaultDatabase = "Koan";
    private const string DefaultUsername = "postgres";
    private const string DefaultPassword = "postgres";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private const int DefaultPort = 5432;
    private const string DockerFixtureDefaultKey = "docker";

    private TestcontainersContainer? _container;

    public PostgresContainerFixture(string dockerFixtureKey = DockerFixtureDefaultKey, string database = DefaultDatabase)
    {
        DockerFixtureKey = dockerFixtureKey;
        Database = database;
    }

    public string DockerFixtureKey { get; }

    public string Database { get; }

    public bool IsAvailable { get; private set; }

    public string? ConnectionString { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsAvailable)
        {
            return;
        }

        context.Diagnostics.Info("postgres.fixture.initialize", new { dockerKey = DockerFixtureKey, database = Database });

        if (TryGetExplicitConnectionString(out var explicitConnection) && await CanOpenAsync(explicitConnection!, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = Normalize(explicitConnection!);
            IsAvailable = true;
            context.Diagnostics.Info("postgres.fixture.explicit", new { source = "env" });
            return;
        }

        if (await TryDetectLocalAsync(context.Cancellation).ConfigureAwait(false) is string localConnection)
        {
            ConnectionString = Normalize(localConnection);
            IsAvailable = true;
            context.Diagnostics.Info("postgres.fixture.local", new { connection = "localhost" });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingPostgresContainer().";
            context.Diagnostics.Warn("postgres.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("postgres.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        Environment.SetEnvironmentVariable(RyukVariable, "true");
        context.Diagnostics.Debug("postgres.fixture.ryuk.disabled", new { variable = RyukVariable });

        var endpoint = dockerFixture.Endpoint;
        var password = DefaultPassword;
        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", password)
            .WithEnvironment("POSTGRES_USER", DefaultUsername)
            .WithEnvironment("POSTGRES_DB", Database)
            .WithCleanUp(true)
            .WithPortBinding(DefaultPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(DefaultPort));

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            builder = builder.WithDockerEndpoint(endpoint);
        }

        _container = builder.Build();
        context.Diagnostics.Info("postgres.fixture.container.create", new { image = "postgres:16-alpine", endpoint });

        try
        {
            await _container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = _container.GetMappedPublicPort(DefaultPort);
            var connection = new NpgsqlConnectionStringBuilder
            {
                Host = "localhost",
                Port = mappedPort,
                Database = Database,
                Username = DefaultUsername,
                Password = password,
                Timeout = 3,
                KeepAlive = 0
            }.ConnectionString;

            if (!await CanOpenAsync(connection, context.Cancellation).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Unable to open connection to Postgres container.");
            }

            ConnectionString = connection;
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("postgres.fixture.container.started", new { host = "localhost", port = mappedPort });
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Postgres container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("postgres.fixture.container.failed", new { message = ex.Message }, ex);
            await DisposeContainerSilentlyAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeContainerSilentlyAsync().ConfigureAwait(false);
    }

    private static bool TryGetExplicitConnectionString(out string? connectionString)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("Koan_POSTGRES__CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"),
            BuildFromPgEnv()
        };

        connectionString = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static string Normalize(string value)
    {
        var builder = new NpgsqlConnectionStringBuilder(value)
        {
            Timeout = 3,
            KeepAlive = 0
        };
        return builder.ConnectionString;
    }

    private static async Task<bool> CanOpenAsync(string connectionString, CancellationToken cancellation)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellation).ConfigureAwait(false);
            await connection.CloseAsync().ConfigureAwait(false);
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
        var database = Environment.GetEnvironmentVariable("PGDATABASE") ?? DefaultDatabase;
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? DefaultUsername;
        var password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var parsed) ? parsed : DefaultPort,
            Database = database,
            Username = user,
            Password = password,
            Timeout = 3,
            KeepAlive = 0
        };

        return builder.ConnectionString;
    }

    private static async Task<string?> TryDetectLocalAsync(CancellationToken cancellation)
    {
        var envCandidate = BuildFromPgEnv();
        if (!string.IsNullOrWhiteSpace(envCandidate) && await CanOpenAsync(envCandidate!, cancellation).ConfigureAwait(false))
        {
            return envCandidate;
        }

        var ports = new[] { 5432, 5433, 5434 };
        var host = "localhost";
        if (!await AnyPortReachableAsync(host, ports, cancellation).ConfigureAwait(false))
        {
            return null;
        }

        var userCandidates = new[]
        {
            Environment.GetEnvironmentVariable("PGUSER"),
            DefaultUsername,
            Environment.UserName
        }.Where(value => !string.IsNullOrWhiteSpace(value))!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var databaseCandidates = new[]
        {
            Environment.GetEnvironmentVariable("PGDATABASE"),
            DefaultDatabase,
            "postgres"
        }.Where(value => !string.IsNullOrWhiteSpace(value))!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var passwordCandidates = new[]
        {
            Environment.GetEnvironmentVariable("PGPASSWORD"),
            DefaultPassword,
            string.Empty
        };

        foreach (var port in ports)
        {
            foreach (var user in userCandidates)
            {
                foreach (var database in databaseCandidates)
                {
                    foreach (var password in passwordCandidates)
                    {
                        var builder = new NpgsqlConnectionStringBuilder
                        {
                            Host = host,
                            Port = port,
                            Database = database!,
                            Username = user!,
                            Timeout = 3,
                            KeepAlive = 0
                        };

                        if (!string.IsNullOrWhiteSpace(password))
                        {
                            builder.Password = password;
                        }

                        var candidate = builder.ConnectionString;
                        if (await CanOpenAsync(candidate, cancellation).ConfigureAwait(false))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static async Task<bool> AnyPortReachableAsync(string host, IEnumerable<int> ports, CancellationToken cancellation)
    {
        foreach (var port in ports)
        {
            if (await CanTcpConnectAsync(host, port, cancellation).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanTcpConnectAsync(string host, int port, CancellationToken cancellation, int timeoutMs = 250)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var delayTask = Task.Delay(timeoutMs, cancellation);
            var completed = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async ValueTask DisposeContainerSilentlyAsync()
    {
        if (_container is null)
        {
            return;
        }

        try
        {
            await _container.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        try
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        _container = null;
    }
}
