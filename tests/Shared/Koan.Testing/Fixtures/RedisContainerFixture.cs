using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;

namespace Koan.Testing.Fixtures;

public sealed class RedisContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const int RedisPort = 6379;
    private const string DefaultDockerFixtureKey = "docker";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private TestcontainersContainer? _container;
    private string? _cliContainerId;
    private string? _dockerEndpoint;

    public RedisContainerFixture(string dockerFixtureKey = DefaultDockerFixtureKey)
    {
        DockerFixtureKey = dockerFixtureKey;
    }

    public string DockerFixtureKey { get; }

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

        context.Diagnostics.Info("redis.fixture.initialize", new { dockerKey = DockerFixtureKey });

        if (TryGetExplicitConnectionString(out var explicitConnection))
        {
            ConnectionString = explicitConnection;
            IsAvailable = true;
            context.Diagnostics.Info("redis.fixture.explicit", new { source = "env" });
            return;
        }

        if (await CanTcpConnectAsync(IPAddress.Loopback, RedisPort, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = $"127.0.0.1:{RedisPort}";
            IsAvailable = true;
            context.Diagnostics.Info("redis.fixture.local", new { host = "127.0.0.1", port = RedisPort });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingRedisContainer().";
            context.Diagnostics.Warn("redis.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("redis.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        Environment.SetEnvironmentVariable(RyukVariable, "true");
        context.Diagnostics.Debug("redis.fixture.ryuk.disabled", new { variable = RyukVariable });

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .WithPortBinding(RedisPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(RedisPort));

        _dockerEndpoint = dockerFixture!.Endpoint;
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
            context.Diagnostics.Info("redis.fixture.container.create", new { image = "redis:7-alpine", endpoint });

            await container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = container.GetMappedPublicPort(RedisPort);
            ConnectionString = $"localhost:{mappedPort}";
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("redis.fixture.container.started", new { host = "localhost", port = mappedPort });
            return;
        }
        catch (Exception ex) when (IsTestcontainersMissingMethod(ex, out var mmex))
        {
            var missingMessage = mmex?.Message ?? ex.Message;
            context.Diagnostics.Warn("redis.fixture.testcontainers.missingmethod", new { message = missingMessage });
            await DisposeContainerSilentlyAsync(container).ConfigureAwait(false);

            var (ok, failureReason) = await TryStartWithDockerCliAsync(context).ConfigureAwait(false);
            if (ok)
            {
                return;
            }

            UnavailableReason = failureReason ?? $"Failed to start Redis container via Docker CLI fallback.";
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Redis container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("redis.fixture.container.failed", new { message = ex.Message }, ex);
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
            Environment.GetEnvironmentVariable("Koan_REDIS__CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("REDIS_URL"),
            Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
        };

        connectionString = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static Task<bool> CanTcpConnectAsync(string host, int port, CancellationToken cancellation, int timeoutMs = 250)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            return CanTcpConnectAsync(ip, port, cancellation, timeoutMs);
        }

        return CanTcpConnectByLookupAsync(host, port, cancellation, timeoutMs);
    }

    private static async Task<bool> CanTcpConnectByLookupAsync(string host, int port, CancellationToken cancellation, int timeoutMs)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);

            foreach (var address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork
                    && await CanTcpConnectAsync(address, port, cancellation, timeoutMs).ConfigureAwait(false))
                {
                    return true;
                }
            }

            foreach (var address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetworkV6
                    && await CanTcpConnectAsync(address, port, cancellation, timeoutMs).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static async Task<bool> CanTcpConnectAsync(IPAddress address, int port, CancellationToken cancellation, int timeoutMs = 250)
    {
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            var connectTask = client.ConnectAsync(address, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cancellation)).ConfigureAwait(false);
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

    private async Task<(bool ok, string? failureReason)> TryStartWithDockerCliAsync(TestContext context)
    {
        var containerName = $"koan-redis-{Guid.NewGuid():N}";
        var runArgs = $"run --rm -d --name {containerName} -p 127.0.0.1::6379 redis:7-alpine";
        var (runOk, runStdout, runStderr, runExitCode) = await RunDockerCommandAsync(runArgs, context.Cancellation).ConfigureAwait(false);

        if (!runOk)
        {
            context.Diagnostics.Warn("redis.fixture.dockercli.run.failed", new { exitCode = runExitCode, stdout = Truncate(runStdout), stderr = Truncate(runStderr) });
            return (false, $"docker run failed (exit {runExitCode})");
        }

        _cliContainerId = containerName;

        var portOk = false;
        string portStdout = string.Empty;
        string portStderr = string.Empty;
        var portExitCode = 0;

        for (var attempt = 0; attempt < 5 && !portOk; attempt++)
        {
            (portOk, portStdout, portStderr, portExitCode) = await RunDockerCommandAsync($"port {containerName} 6379/tcp", context.Cancellation).ConfigureAwait(false);
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
            context.Diagnostics.Warn("redis.fixture.dockercli.port.failed", new { exitCode = portExitCode, stdout = Truncate(portStdout), stderr = Truncate(portStderr) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, "Failed to determine published Redis port from docker CLI");
        }

        var hostPort = ParseDockerPortOutput(portStdout);
        if (hostPort == 0)
        {
            context.Diagnostics.Warn("redis.fixture.dockercli.port.parse", new { stdout = Truncate(portStdout) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, "Unable to parse published Redis port from docker CLI output");
        }

        var connected = false;
        for (var attempt = 0; attempt < 60 && !context.Cancellation.IsCancellationRequested; attempt++)
        {
            if (await CanTcpConnectAsync(IPAddress.Loopback, hostPort, context.Cancellation, 250).ConfigureAwait(false)
                || await CanTcpConnectAsync(IPAddress.IPv6Loopback, hostPort, context.Cancellation, 250).ConfigureAwait(false))
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
            context.Diagnostics.Warn("redis.fixture.dockercli.connect.timeout", new { port = hostPort });
            await DumpDockerLogsAsync(containerName, context.Cancellation, context).ConfigureAwait(false);
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, $"Redis container did not accept TCP connections on localhost:{hostPort} within timeout");
        }

    ConnectionString = $"127.0.0.1:{hostPort}";
        IsAvailable = true;
        UnavailableReason = null;
        context.Diagnostics.Info("redis.fixture.dockercli.started", new { container = containerName, host = "localhost", port = hostPort });
        return (true, null);
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
    {
        return endpoint.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase);
    }

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
                context.Diagnostics.Debug("redis.fixture.dockercli.logs.failed", new { exitCode, stderr = Truncate(stderr), stdout = Truncate(stdout) });
                return;
            }

            var preview = Truncate(stdout, 1024);
            context.Diagnostics.Info("redis.fixture.dockercli.logs", new { container = containerName, logs = preview });
        }
        catch
        {
            // ignored
        }
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

            var portSegment = trimmed[(colonIndex + 1)..];
            if (int.TryParse(portSegment, out var port))
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
