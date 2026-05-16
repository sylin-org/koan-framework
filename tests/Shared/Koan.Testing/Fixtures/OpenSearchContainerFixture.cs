using System.Diagnostics;
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

/// <summary>
/// Shared OpenSearch container fixture (per ARCH-0079). Mirrors the
/// <see cref="RedisContainerFixture"/> pattern: env-var override → local TCP ping →
/// Testcontainers Docker daemon → Docker CLI fallback.
/// </summary>
public sealed class OpenSearchContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const string DefaultEndpoint = "http://localhost:9200";
    private const string DockerFixtureDefaultKey = "docker";
    private const int OpenSearchHttpPort = 9200;
    private const string ImageName = "opensearchproject/opensearch:2.18.0";

    private TestcontainersContainer? _container;
    private string? _cliContainerId;
    private string? _dockerEndpoint;

    public OpenSearchContainerFixture(string dockerFixtureKey = DockerFixtureDefaultKey)
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

        context.Diagnostics.Info("opensearch.fixture.initialize", new { dockerKey = DockerFixtureKey });

        if (TryGetExplicitEndpoint(out var explicitEndpoint) && await CanPing(explicitEndpoint!, context.Cancellation).ConfigureAwait(false))
        {
            Endpoint = explicitEndpoint;
            IsAvailable = true;
            context.Diagnostics.Info("opensearch.fixture.explicit", new { source = "env" });
            return;
        }

        if (await CanPing(DefaultEndpoint, context.Cancellation).ConfigureAwait(false))
        {
            Endpoint = DefaultEndpoint;
            IsAvailable = true;
            context.Diagnostics.Info("opensearch.fixture.local", new { host = "localhost", port = OpenSearchHttpPort });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingOpenSearchContainer().";
            context.Diagnostics.Warn("opensearch.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("opensearch.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage(ImageName)
            .WithCleanUp(true)
            .WithPortBinding(OpenSearchHttpPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(OpenSearchHttpPort))
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
            .WithEnvironment("DISABLE_INSTALL_DEMO_CONFIG", "true")
            .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m");

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
            context.Diagnostics.Info("opensearch.fixture.container.create", new { image = ImageName, endpoint });

            await container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = container.GetMappedPublicPort(OpenSearchHttpPort);
            var connection = $"http://localhost:{mappedPort}";

            if (!await WaitForReady(connection, context.Cancellation).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Unable to confirm OpenSearch readiness.");
            }

            Endpoint = connection;
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("opensearch.fixture.container.started", new { host = "localhost", port = mappedPort });
            return;
        }
        catch (Exception ex) when (IsTestcontainersMissingMethod(ex, out var mmex))
        {
            var missingMessage = mmex?.Message ?? ex.Message;
            context.Diagnostics.Warn("opensearch.fixture.testcontainers.missingmethod", new { message = missingMessage });
            await DisposeContainerSilently(container).ConfigureAwait(false);

            var (ok, connection, failureReason) = await TryStartWithDockerCli(context).ConfigureAwait(false);
            if (ok && connection is not null)
            {
                Endpoint = connection;
                IsAvailable = true;
                UnavailableReason = null;
                return;
            }

            UnavailableReason = failureReason ?? "Failed to start OpenSearch container via Docker CLI fallback.";
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start OpenSearch container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("opensearch.fixture.container.failed", new { message = ex.Message }, ex);
            await DisposeContainerSilently(container).ConfigureAwait(false);
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
        await DisposeContainerSilently().ConfigureAwait(false);
    }

    private static bool TryGetExplicitEndpoint(out string? endpoint)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("Koan_TESTS_OPENSEARCH"),
            Environment.GetEnvironmentVariable("Koan_OPENSEARCH__ENDPOINT"),
            Environment.GetEnvironmentVariable("OPENSEARCH_ENDPOINT"),
            Environment.GetEnvironmentVariable("OpenSearch__Endpoint")
        };

        endpoint = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(endpoint) && !endpoint.Contains("://", StringComparison.Ordinal))
        {
            endpoint = $"http://{endpoint}";
        }

        return !string.IsNullOrWhiteSpace(endpoint);
    }

    private static async Task<bool> CanPing(string endpoint, CancellationToken cancellation = default)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
            http.Timeout = TimeSpan.FromSeconds(3);
            var resp = await http.GetAsync("/", cancellation).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForReady(string endpoint, CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await CanPing(endpoint, cancellation).ConfigureAwait(false))
            {
                return true;
            }

            try
            {
                await Task.Delay(500, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private async ValueTask DisposeContainerSilently(TestcontainersContainer? container = null)
    {
        var target = container ?? _container;
        if (target is not null)
        {
            try { await target.StopAsync().ConfigureAwait(false); } catch { }
            try { await target.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        if (ReferenceEquals(target, _container))
        {
            _container = null;
        }

        await StopCliContainer().ConfigureAwait(false);
    }

    private async Task<(bool ok, string? endpoint, string? failureReason)> TryStartWithDockerCli(TestContext context)
    {
        var containerName = $"koan-opensearch-{Guid.NewGuid():N}";
        var runArgs = $"run --rm -d --name {containerName} -p 127.0.0.1::{OpenSearchHttpPort} --env discovery.type=single-node --env DISABLE_SECURITY_PLUGIN=true --env DISABLE_INSTALL_DEMO_CONFIG=true --env OPENSEARCH_JAVA_OPTS=\"-Xms512m -Xmx512m\" {ImageName}";
        var (runOk, runStdout, runStderr, runExitCode) = await RunDockerCommand(runArgs, context.Cancellation).ConfigureAwait(false);

        if (!runOk)
        {
            context.Diagnostics.Warn("opensearch.fixture.dockercli.run.failed", new { exitCode = runExitCode, stdout = Truncate(runStdout), stderr = Truncate(runStderr) });
            return (false, null, $"docker run failed (exit {runExitCode})");
        }

        _cliContainerId = containerName;

        (bool ok, string stdout, string stderr, int exitCode) portResult = (false, "", "", 0);
        for (var attempt = 0; attempt < 5 && !portResult.ok; attempt++)
        {
            portResult = await RunDockerCommand($"port {containerName} {OpenSearchHttpPort}/tcp", context.Cancellation).ConfigureAwait(false);
            if (!portResult.ok || string.IsNullOrWhiteSpace(portResult.stdout))
            {
                await Task.Delay(200, context.Cancellation).ConfigureAwait(false);
            }
        }

        if (!portResult.ok || string.IsNullOrWhiteSpace(portResult.stdout))
        {
            context.Diagnostics.Warn("opensearch.fixture.dockercli.port.failed", new { exitCode = portResult.exitCode, stdout = Truncate(portResult.stdout), stderr = Truncate(portResult.stderr) });
            await StopCliContainer().ConfigureAwait(false);
            return (false, null, "Failed to determine published OpenSearch port from docker CLI");
        }

        var hostPort = ParseDockerPortOutput(portResult.stdout);
        if (hostPort == 0)
        {
            context.Diagnostics.Warn("opensearch.fixture.dockercli.port.parse", new { stdout = Truncate(portResult.stdout) });
            await StopCliContainer().ConfigureAwait(false);
            return (false, null, "Unable to parse published OpenSearch port from docker CLI output");
        }

        var connection = $"http://127.0.0.1:{hostPort}";
        if (!await WaitForReady(connection, context.Cancellation).ConfigureAwait(false))
        {
            context.Diagnostics.Warn("opensearch.fixture.dockercli.connect.timeout", new { port = hostPort });
            await StopCliContainer().ConfigureAwait(false);
            return (false, null, $"OpenSearch container did not respond on localhost:{hostPort} within timeout");
        }

        Endpoint = connection;
        IsAvailable = true;
        UnavailableReason = null;
        context.Diagnostics.Info("opensearch.fixture.dockercli.started", new { container = containerName, host = "localhost", port = hostPort });
        return (true, connection, null);
    }

    private async Task<(bool ok, string stdout, string stderr, int exitCode)> RunDockerCommand(string arguments, CancellationToken cancellation)
    {
        var psi = CreateDockerProcessStartInfo(arguments);
        Process? process = null;

        try
        {
            process = Process.Start(psi);
            if (process is null)
            {
                return (false, "", "Failed to start docker process", -1);
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
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            return (false, "", "Cancelled", -1);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message, -1);
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

    private async Task StopCliContainer()
    {
        if (string.IsNullOrWhiteSpace(_cliContainerId))
        {
            return;
        }

        try { await RunDockerCommand($"rm -f {_cliContainerId}", CancellationToken.None).ConfigureAwait(false); }
        catch { }
        finally { _cliContainerId = null; }
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

    private static string Truncate(string? value, int max = 256)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? "";
        }

        return value[..max];
    }
}
