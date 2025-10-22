using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Testing.Fixtures;

public sealed class MongoContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const string DefaultDatabase = "Koan";
    private const string DockerFixtureDefaultKey = "docker";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private const int MongoPort = 27017;

    private TestcontainersContainer? _container;
    private string? _cliContainerId;
    private string? _dockerEndpoint;

    public MongoContainerFixture(string dockerFixtureKey = DockerFixtureDefaultKey, string database = DefaultDatabase)
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

        context.Diagnostics.Info("mongo.fixture.initialize", new { dockerKey = DockerFixtureKey, database = Database });

        if (TryGetExplicitConnectionString(out var explicitConnection) && await CanPingAsync(explicitConnection!, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = EnsureDatabase(explicitConnection!, Database);
            IsAvailable = true;
            context.Diagnostics.Info("mongo.fixture.explicit", new { source = "env" });
            return;
        }

        var localhost = "mongodb://localhost:27017";
        if (await CanPingAsync(localhost, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = EnsureDatabase(localhost, Database);
            IsAvailable = true;
            context.Diagnostics.Info("mongo.fixture.local", new { host = "localhost", port = MongoPort });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingMongoContainer().";
            context.Diagnostics.Warn("mongo.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("mongo.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        Environment.SetEnvironmentVariable(RyukVariable, "true");
        context.Diagnostics.Debug("mongo.fixture.ryuk.disabled", new { variable = RyukVariable });

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("mongo:7")
            .WithCleanUp(true)
            .WithPortBinding(MongoPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(MongoPort));

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
            context.Diagnostics.Info("mongo.fixture.container.create", new { image = "mongo:7", endpoint });

            await container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = container.GetMappedPublicPort(MongoPort);
            var connection = EnsureDatabase($"mongodb://localhost:{mappedPort}", Database);

            if (!await CanPingAsync(connection, context.Cancellation).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Unable to ping Mongo container.");
            }

            ConnectionString = connection;
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("mongo.fixture.container.started", new { host = "localhost", port = mappedPort });
            return;
        }
        catch (Exception ex) when (IsTestcontainersMissingMethod(ex, out var mmex))
        {
            var missingMessage = mmex?.Message ?? ex.Message;
            context.Diagnostics.Warn("mongo.fixture.testcontainers.missingmethod", new { message = missingMessage });
            await DisposeContainerSilentlyAsync(container).ConfigureAwait(false);

            var (ok, connection, failureReason) = await TryStartWithDockerCliAsync(context).ConfigureAwait(false);
            if (ok && connection is not null)
            {
                ConnectionString = connection;
                IsAvailable = true;
                UnavailableReason = null;
                return;
            }

            UnavailableReason = failureReason ?? "Failed to start Mongo container via Docker CLI fallback.";
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Mongo container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("mongo.fixture.container.failed", new { message = ex.Message }, ex);
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
            Environment.GetEnvironmentVariable("Koan_TESTS_MONGO"),
            Environment.GetEnvironmentVariable("Koan_MONGO__CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("Mongo__ConnectionString")
        };

        connectionString = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static string EnsureDatabase(string connectionString, string database)
    {
        var builder = new MongoUrlBuilder(connectionString)
        {
            DatabaseName = string.IsNullOrWhiteSpace(database)
                ? DefaultDatabase
                : database
        };

        return builder.ToString();
    }

    private static async Task<bool> CanPingAsync(string connectionString, CancellationToken cancellation = default)
    {
        try
        {
            var url = new MongoUrl(connectionString);
            var settings = MongoClientSettings.FromUrl(url);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
            settings.ConnectTimeout = TimeSpan.FromSeconds(3);
            settings.SocketTimeout = TimeSpan.FromSeconds(3);

            var client = new MongoClient(settings);
            var dbName = url.DatabaseName ?? "admin";
            var database = client.GetDatabase(dbName);
            var result = await database.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1), cancellationToken: cancellation).ConfigureAwait(false);
            return result is not null;
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

    private async Task<(bool ok, string? connectionString, string? failureReason)> TryStartWithDockerCliAsync(TestContext context)
    {
        var containerName = $"koan-mongo-{Guid.NewGuid():N}";
        var runArgs = $"run --rm -d --name {containerName} -p 127.0.0.1::{MongoPort} mongo:7";
        var (runOk, runStdout, runStderr, runExitCode) = await RunDockerCommandAsync(runArgs, context.Cancellation).ConfigureAwait(false);

        if (!runOk)
        {
            context.Diagnostics.Warn("mongo.fixture.dockercli.run.failed", new { exitCode = runExitCode, stdout = Truncate(runStdout), stderr = Truncate(runStderr) });
            return (false, null, $"docker run failed (exit {runExitCode})");
        }

        _cliContainerId = containerName;

        var portOk = false;
        string portStdout = string.Empty;
        string portStderr = string.Empty;
        var portExitCode = 0;

        for (var attempt = 0; attempt < 5 && !portOk; attempt++)
        {
            (portOk, portStdout, portStderr, portExitCode) = await RunDockerCommandAsync($"port {containerName} {MongoPort}/tcp", context.Cancellation).ConfigureAwait(false);
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
            context.Diagnostics.Warn("mongo.fixture.dockercli.port.failed", new { exitCode = portExitCode, stdout = Truncate(portStdout), stderr = Truncate(portStderr) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, "Failed to determine published Mongo port from docker CLI");
        }

        var hostPort = ParseDockerPortOutput(portStdout);
        if (hostPort == 0)
        {
            context.Diagnostics.Warn("mongo.fixture.dockercli.port.parse", new { stdout = Truncate(portStdout) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, "Unable to parse published Mongo port from docker CLI output");
        }

        var connection = EnsureDatabase($"mongodb://127.0.0.1:{hostPort}", Database);
        var connected = false;
        for (var attempt = 0; attempt < 60 && !context.Cancellation.IsCancellationRequested; attempt++)
        {
            if (await CanPingAsync(connection, context.Cancellation).ConfigureAwait(false))
            {
                connected = true;
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

        if (!connected)
        {
            context.Diagnostics.Warn("mongo.fixture.dockercli.connect.timeout", new { port = hostPort });
            await DumpDockerLogsAsync(containerName, context.Cancellation, context).ConfigureAwait(false);
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, $"Mongo container did not respond on localhost:{hostPort} within timeout");
        }

        ConnectionString = connection;
        IsAvailable = true;
        UnavailableReason = null;
        context.Diagnostics.Info("mongo.fixture.dockercli.started", new { container = containerName, host = "localhost", port = hostPort });
        return (true, connection, null);
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
                context.Diagnostics.Debug("mongo.fixture.dockercli.logs.failed", new { exitCode, stderr = Truncate(stderr), stdout = Truncate(stdout) });
                return;
            }

            context.Diagnostics.Info("mongo.fixture.dockercli.logs", new { container = containerName, logs = Truncate(stdout, 1024) });
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
