using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    private string? _cliContainerId;
    private string? _dockerEndpoint;

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

        var password = DefaultPassword;
        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", password)
            .WithEnvironment("POSTGRES_USER", DefaultUsername)
            .WithEnvironment("POSTGRES_DB", Database)
            .WithCleanUp(true)
            .WithPortBinding(DefaultPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(DefaultPort));

        _dockerEndpoint = dockerFixture.Endpoint;
        var endpoint = _dockerEndpoint;
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            builder = builder.WithDockerEndpoint(endpoint);
        }

        TestcontainersContainer? container = null;
        try
        {
            container = builder.Build();
            _container = container;
            context.Diagnostics.Info("postgres.fixture.container.create", new { image = "postgres:16-alpine", endpoint });

            await container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = container.GetMappedPublicPort(DefaultPort);
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

            ConnectionString = Normalize(connection);
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("postgres.fixture.container.started", new { host = "localhost", port = mappedPort });
            return;
        }
        catch (Exception ex) when (IsTestcontainersMissingMethod(ex, out var mmex))
        {
            var missingMessage = mmex?.Message ?? ex.Message;
            context.Diagnostics.Warn("postgres.fixture.testcontainers.missingmethod", new { message = missingMessage });
            await DisposeContainerSilentlyAsync(container).ConfigureAwait(false);

            var (ok, connection, failureReason) = await TryStartWithDockerCliAsync(context, password).ConfigureAwait(false);
            if (ok && connection is not null)
            {
                ConnectionString = connection;
                IsAvailable = true;
                UnavailableReason = null;
                return;
            }

            UnavailableReason = failureReason ?? "Failed to start Postgres container via Docker CLI fallback.";
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Postgres container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("postgres.fixture.container.failed", new { message = ex.Message }, ex);
            await DisposeContainerSilentlyAsync(container).ConfigureAwait(false);
            return;
        }
    }

    private static bool IsTestcontainersMissingMethod(Exception ex, out MissingMethodException? missingMethod)
    {
        missingMethod = ex as MissingMethodException ?? ex.InnerException as MissingMethodException ?? (ex as TargetInvocationException)?.InnerException as MissingMethodException;
        if (missingMethod is not null)
        {
            return true;
        }

        if (ex is TargetInvocationException tie && tie.InnerException is not null)
        {
            return IsTestcontainersMissingMethod(tie.InnerException, out missingMethod);
        }

        return false;
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

    private async ValueTask DisposeContainerSilentlyAsync(TestcontainersContainer? container = null)
    {
        var target = container ?? _container;
        if (target is not null)
        {
            try
            {
                await target.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            try
            {
                await target.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        if (ReferenceEquals(target, _container))
        {
            _container = null;
        }

        await StopCliContainerAsync().ConfigureAwait(false);
    }

    private async Task<(bool ok, string? connectionString, string? failureReason)> TryStartWithDockerCliAsync(TestContext context, string password)
    {
        var containerName = $"koan-postgres-{Guid.NewGuid():N}";
        var runArgs = $"run --rm -d --name {containerName} -e POSTGRES_PASSWORD={password} -e POSTGRES_USER={DefaultUsername} -e POSTGRES_DB={Database} -p 127.0.0.1::{DefaultPort} postgres:16-alpine";
        var (runOk, runStdout, runStderr, runExitCode) = await RunDockerCommandAsync(runArgs, context.Cancellation).ConfigureAwait(false);

        if (!runOk)
        {
            context.Diagnostics.Warn("postgres.fixture.dockercli.run.failed", new { exitCode = runExitCode, stdout = Truncate(runStdout), stderr = Truncate(runStderr) });
            return (false, null, $"docker run failed (exit {runExitCode})");
        }

        _cliContainerId = containerName;

        var portOk = false;
        string portStdout = string.Empty;
        string portStderr = string.Empty;
        var portExitCode = 0;

        for (var attempt = 0; attempt < 5 && !portOk; attempt++)
        {
            (portOk, portStdout, portStderr, portExitCode) = await RunDockerCommandAsync($"port {containerName} {DefaultPort}/tcp", context.Cancellation).ConfigureAwait(false);
            if (portOk && !string.IsNullOrWhiteSpace(portStdout))
            {
                break;
            }

            portOk = false;

            try
            {
                await Task.Delay(200, context.Cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (!portOk || string.IsNullOrWhiteSpace(portStdout))
        {
            context.Diagnostics.Warn("postgres.fixture.dockercli.port.failed", new { exitCode = portExitCode, stdout = Truncate(portStdout), stderr = Truncate(portStderr) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, "Failed to determine published Postgres port from docker CLI");
        }

        var hostPort = ParseDockerPortOutput(portStdout);
        if (hostPort == 0)
        {
            context.Diagnostics.Warn("postgres.fixture.dockercli.port.parse", new { stdout = Truncate(portStdout) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, "Unable to parse published Postgres port from docker CLI output");
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = hostPort,
            Database = Database,
            Username = DefaultUsername,
            Password = password,
            Timeout = 3,
            KeepAlive = 0
        }.ConnectionString;

        var opened = false;
        for (var attempt = 0; attempt < 60 && !context.Cancellation.IsCancellationRequested; attempt++)
        {
            if (await CanOpenAsync(connection, context.Cancellation).ConfigureAwait(false))
            {
                opened = true;
                break;
            }

            try
            {
                await Task.Delay(500, context.Cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (!opened)
        {
            context.Diagnostics.Warn("postgres.fixture.dockercli.connect.timeout", new { port = hostPort });
            await DumpDockerLogsAsync(containerName, context.Cancellation, context).ConfigureAwait(false);
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, $"Postgres container did not accept connections on localhost:{hostPort} within timeout");
        }

        var normalized = Normalize(connection);
        ConnectionString = normalized;
        IsAvailable = true;
        UnavailableReason = null;
        context.Diagnostics.Info("postgres.fixture.dockercli.started", new { container = containerName, host = "localhost", port = hostPort });
        return (true, normalized, null);
    }

    private async Task<(bool ok, string stdout, string stderr, int exitCode)> RunDockerCommandAsync(string arguments, CancellationToken cancellation)
    {
        var psi = CreateDockerProcessStartInfo(arguments);
        Process? process = null;

        try
        {
            process = Process.Start(psi);
            if (process is null)
            {
                return (false, string.Empty, "Failed to start docker process", -1);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellation)).ConfigureAwait(false);

            return (process.ExitCode == 0, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }
            }

            return (false, string.Empty, "Cancelled", -1);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message, -1);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private ProcessStartInfo CreateDockerProcessStartInfo(string arguments)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "docker.exe" : "docker";
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(_dockerEndpoint) && !IsLocalNamedPipe(_dockerEndpoint))
        {
            psi.Environment["DOCKER_HOST"] = NormalizeDockerEndpointForCli(_dockerEndpoint!);
        }

        return psi;
    }

    private static bool IsLocalNamedPipe(string endpoint)
        => endpoint.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDockerEndpointForCli(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        if (endpoint.StartsWith("npipe://./", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = endpoint["npipe://./".Length..];
            return "npipe:////./" + suffix.TrimStart('/');
        }

        return endpoint;
    }

    private static int ParseDockerPortOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return 0;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var colonIndex = trimmed.LastIndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            var segment = trimmed[(colonIndex + 1)..];
            if (int.TryParse(segment, out var port))
            {
                return port;
            }
        }

        return 0;
    }

    private static string Truncate(string? value, int max = 256)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max] + "…";
    }

    private async Task DumpDockerLogsAsync(string containerName, CancellationToken cancellation, TestContext context)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return;
        }

        try
        {
            var (ok, stdout, stderr, exitCode) = await RunDockerCommandAsync($"logs {containerName}", cancellation).ConfigureAwait(false);
            if (!ok)
            {
                context.Diagnostics.Debug("postgres.fixture.dockercli.logs.failed", new { exitCode, stderr = Truncate(stderr), stdout = Truncate(stdout) });
                return;
            }

            context.Diagnostics.Info("postgres.fixture.dockercli.logs", new { container = containerName, logs = Truncate(stdout, 1024) });
        }
        catch
        {
            // ignored
        }
    }

    private async ValueTask StopCliContainerAsync()
    {
        if (string.IsNullOrWhiteSpace(_cliContainerId))
        {
            return;
        }

        try
        {
            await RunDockerCommandAsync($"rm -f {_cliContainerId}", CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
        finally
        {
            _cliContainerId = null;
        }
    }
}
