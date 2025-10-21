using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;

namespace Koan.Testing.Fixtures;

public sealed class WeaviateContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const string DefaultEndpoint = "http://localhost:8080";
    private const string DockerFixtureDefaultKey = "docker";
    private const int WeaviateHttpPort = 8080;
    private const string ImageName = "semitechnologies/weaviate:1.25.6";

    private TestcontainersContainer? _container;
    private string? _cliContainerId;
    private string? _dockerEndpoint;

    public WeaviateContainerFixture(string dockerFixtureKey = DockerFixtureDefaultKey)
    {
        DockerFixtureKey = dockerFixtureKey;
    }

    public string DockerFixtureKey { get; }

    public bool IsAvailable { get; private set; }

    public string? Endpoint { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsAvailable)
        {
            return;
        }

        context.Diagnostics.Info("weaviate.fixture.initialize", new { dockerKey = DockerFixtureKey });

        if (TryGetExplicitEndpoint(out var explicitEndpoint) && await CanPingAsync(explicitEndpoint!, context.Cancellation).ConfigureAwait(false))
        {
            Endpoint = explicitEndpoint;
            IsAvailable = true;
            context.Diagnostics.Info("weaviate.fixture.explicit", new { source = "env" });
            return;
        }

        if (await CanPingAsync(DefaultEndpoint, context.Cancellation).ConfigureAwait(false))
        {
            Endpoint = DefaultEndpoint;
            IsAvailable = true;
            context.Diagnostics.Info("weaviate.fixture.local", new { host = "localhost", port = WeaviateHttpPort });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingWeaviateContainer().";
            context.Diagnostics.Warn("weaviate.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("weaviate.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage(ImageName)
            .WithCleanUp(true)
            .WithPortBinding(WeaviateHttpPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(WeaviateHttpPort))
            .WithEnvironment("DEFAULT_VECTORIZER_MODULE", "none")
            .WithEnvironment("ENABLE_MODULES", string.Empty)
            .WithEnvironment("PERSISTENCE_DATA_PATH", "/var/lib/weaviate")
            .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
            .WithEnvironment("AUTHORIZATION_ADMINLIST_ENABLED", "false")
            .WithEnvironment("CLUSTER_HOSTNAME", "node1")
            .WithEnvironment("QUERY_DEFAULTS_LIMIT", "25")
            .WithEnvironment("ENABLE_TELEMETRY", "false")
            .WithEnvironment("LOG_LEVEL", "info");

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
            context.Diagnostics.Info("weaviate.fixture.container.create", new { image = ImageName, endpoint });

            await container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = container.GetMappedPublicPort(WeaviateHttpPort);
            var connection = $"http://localhost:{mappedPort}";

            if (!await WaitForReadyAsync(connection, context.Cancellation).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Unable to confirm Weaviate readiness.");
            }

            Endpoint = connection;
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("weaviate.fixture.container.started", new { host = "localhost", port = mappedPort });
            return;
        }
        catch (Exception ex) when (IsTestcontainersMissingMethod(ex, out var mmex))
        {
            var missingMessage = mmex?.Message ?? ex.Message;
            context.Diagnostics.Warn("weaviate.fixture.testcontainers.missingmethod", new { message = missingMessage });
            await DisposeContainerSilentlyAsync(container).ConfigureAwait(false);

            var (ok, connection, failureReason) = await TryStartWithDockerCliAsync(context).ConfigureAwait(false);
            if (ok && connection is not null)
            {
                Endpoint = connection;
                IsAvailable = true;
                UnavailableReason = null;
                return;
            }

            UnavailableReason = failureReason ?? "Failed to start Weaviate container via Docker CLI fallback.";
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Weaviate container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("weaviate.fixture.container.failed", new { message = ex.Message }, ex);
            await DisposeContainerSilentlyAsync(container).ConfigureAwait(false);
            return;
        }
    }

    private static bool IsTestcontainersMissingMethod(Exception ex, out MissingMethodException? missingMethod)
    {
        missingMethod = ex as MissingMethodException
            ?? ex.InnerException as MissingMethodException
            ?? (ex as TargetInvocationException)?.InnerException as MissingMethodException;
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

    private static bool TryGetExplicitEndpoint(out string? endpoint)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("Koan_TESTS_WEAVIATE"),
            Environment.GetEnvironmentVariable("Koan_WEAVIATE__ENDPOINT"),
            Environment.GetEnvironmentVariable("WEAVIATE_ENDPOINT"),
            Environment.GetEnvironmentVariable("Weaviate__Endpoint")
        };

        endpoint = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(endpoint) && !endpoint.Contains("://", StringComparison.Ordinal))
        {
            endpoint = $"http://{endpoint}";
        }

        return !string.IsNullOrWhiteSpace(endpoint);
    }

    private static async Task<bool> CanPingAsync(string endpoint, CancellationToken cancellation = default)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
            http.Timeout = TimeSpan.FromSeconds(3);
            var resp = await http.GetAsync("/.well-known/ready", cancellation).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                return true;
            }

            resp = await http.GetAsync("/v1/.well-known/ready", cancellation).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForReadyAsync(string endpoint, CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await CanPingAsync(endpoint, cancellation).ConfigureAwait(false))
            {
                return true;
            }

            try
            {
                await Task.Delay(250, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
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
            }

            try
            {
                await target.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (ReferenceEquals(target, _container))
        {
            _container = null;
        }

        await StopCliContainerAsync().ConfigureAwait(false);
    }

    private async Task<(bool ok, string? endpoint, string? failureReason)> TryStartWithDockerCliAsync(TestContext context)
    {
        var containerName = $"koan-weaviate-{Guid.NewGuid():N}";
        var runArgs = $"run --rm -d --name {containerName} -p 127.0.0.1::{WeaviateHttpPort} --env DEFAULT_VECTORIZER_MODULE=none --env ENABLE_MODULES= --env PERSISTENCE_DATA_PATH=/var/lib/weaviate --env AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED=true --env AUTHORIZATION_ADMINLIST_ENABLED=false --env CLUSTER_HOSTNAME=node1 --env QUERY_DEFAULTS_LIMIT=25 --env ENABLE_TELEMETRY=false --env LOG_LEVEL=info {ImageName}";
        var (runOk, runStdout, runStderr, runExitCode) = await RunDockerCommandAsync(runArgs, context.Cancellation).ConfigureAwait(false);

        if (!runOk)
        {
            context.Diagnostics.Warn("weaviate.fixture.dockercli.run.failed", new { exitCode = runExitCode, stdout = Truncate(runStdout), stderr = Truncate(runStderr) });
            return (false, null, $"docker run failed (exit {runExitCode})");
        }

        _cliContainerId = containerName;

        (bool ok, string stdout, string stderr, int exitCode) portResult = (false, string.Empty, string.Empty, 0);
        for (var attempt = 0; attempt < 5 && !portResult.ok; attempt++)
        {
            portResult = await RunDockerCommandAsync($"port {containerName} {WeaviateHttpPort}/tcp", context.Cancellation).ConfigureAwait(false);
            if (!portResult.ok || string.IsNullOrWhiteSpace(portResult.stdout))
            {
                await Task.Delay(200, context.Cancellation).ConfigureAwait(false);
            }
        }

        if (!portResult.ok || string.IsNullOrWhiteSpace(portResult.stdout))
        {
            context.Diagnostics.Warn("weaviate.fixture.dockercli.port.failed", new { exitCode = portResult.exitCode, stdout = Truncate(portResult.stdout), stderr = Truncate(portResult.stderr) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, "Failed to determine published Weaviate port from docker CLI");
        }

        var hostPort = ParseDockerPortOutput(portResult.stdout);
        if (hostPort == 0)
        {
            context.Diagnostics.Warn("weaviate.fixture.dockercli.port.parse", new { stdout = Truncate(portResult.stdout) });
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, "Unable to parse published Weaviate port from docker CLI output");
        }

        var connection = $"http://127.0.0.1:{hostPort}";
        if (!await WaitForReadyAsync(connection, context.Cancellation).ConfigureAwait(false))
        {
            context.Diagnostics.Warn("weaviate.fixture.dockercli.connect.timeout", new { port = hostPort });
            await DumpDockerLogsAsync(containerName, context.Cancellation, context).ConfigureAwait(false);
            await StopCliContainerAsync().ConfigureAwait(false);
            return (false, null, $"Weaviate container did not respond on localhost:{hostPort} within timeout");
        }

        Endpoint = connection;
        IsAvailable = true;
        UnavailableReason = null;
        context.Diagnostics.Info("weaviate.fixture.dockercli.started", new { container = containerName, host = "localhost", port = hostPort });
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
            psi.Environment["DOCKER_HOST"] = _dockerEndpoint;
        }

        return psi;
    }

    private static bool IsLocalNamedPipe(string endpoint)
        => endpoint.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase);

    private async Task StopCliContainerAsync()
    {
        if (string.IsNullOrWhiteSpace(_cliContainerId))
        {
            return;
        }

        try
        {
            await RunDockerCommandAsync($"stop {_cliContainerId}", CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _cliContainerId = null;
        }
    }

    private async Task DumpDockerLogsAsync(string containerName, CancellationToken cancellation, TestContext context)
    {
        try
        {
            var (ok, stdout, stderr, exitCode) = await RunDockerCommandAsync($"logs {containerName}", cancellation).ConfigureAwait(false);
            context.Diagnostics.Debug("weaviate.fixture.dockercli.logs", new { ok, exitCode, stdout = Truncate(stdout), stderr = Truncate(stderr) });
        }
        catch
        {
        }
    }

    private static int ParseDockerPortOutput(string output)
    {
        var parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = parts.LastOrDefault();
        if (last is null)
        {
            return 0;
        }

        var colon = last.LastIndexOf(':');
        if (colon < 0)
        {
            return 0;
        }

        return int.TryParse(last[(colon + 1)..], out var port) ? port : 0;
    }

    private static string Truncate(string value, int max = 400)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }
}
