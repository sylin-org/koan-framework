using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;

namespace Koan.Testing.Fixtures;

/// <summary>
/// Shared Couchbase container fixture (per ARCH-0079). Boots a Couchbase Community node,
/// initializes the cluster (data + n1ql + index services), creates a default bucket, and
/// waits for readiness. Mirrors the <see cref="RedisContainerFixture"/> resilience pattern.
/// </summary>
public sealed class CouchbaseContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const string DefaultBucket = "koan";
    private const string DockerFixtureDefaultKey = "docker";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private const int CouchbaseRestPort = 8091;
    private const int CouchbaseKvPort = 11210;
    private const string ImageName = "couchbase:community-7.6.1";
    private const string DefaultUsername = "Administrator";
    private const string DefaultPassword = "password";

    private TestcontainersContainer? _container;
    private string? _cliContainerId;
    private string? _dockerEndpoint;

    public CouchbaseContainerFixture(string dockerFixtureKey = DockerFixtureDefaultKey, string bucket = DefaultBucket)
    {
        DockerFixtureKey = dockerFixtureKey;
        Bucket = bucket;
    }

    public string DockerFixtureKey { get; }

    public string Bucket { get; }

    public bool IsAvailable { get; private set; }

    public string? ConnectionString { get; private set; }

    public string? Username { get; private set; }

    public string? Password { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsAvailable)
        {
            return;
        }

        context.Diagnostics.Info("couchbase.fixture.initialize", new { dockerKey = DockerFixtureKey, bucket = Bucket });

        if (TryGetExplicitConnectionString(out var explicitConnection))
        {
            ConnectionString = explicitConnection;
            Username = Environment.GetEnvironmentVariable("COUCHBASE_USERNAME") ?? DefaultUsername;
            Password = Environment.GetEnvironmentVariable("COUCHBASE_PASSWORD") ?? DefaultPassword;
            IsAvailable = true;
            context.Diagnostics.Info("couchbase.fixture.explicit", new { source = "env" });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingCouchbaseContainer().";
            context.Diagnostics.Warn("couchbase.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("couchbase.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        Environment.SetEnvironmentVariable(RyukVariable, "true");

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage(ImageName)
            .WithCleanUp(true)
            .WithPortBinding(CouchbaseRestPort, assignRandomHostPort: true)
            .WithPortBinding(CouchbaseKvPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(CouchbaseRestPort));

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
            context.Diagnostics.Info("couchbase.fixture.container.create", new { image = ImageName, endpoint });

            await container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedRestPort = container.GetMappedPublicPort(CouchbaseRestPort);
            var mappedKvPort = container.GetMappedPublicPort(CouchbaseKvPort);

            var restEndpoint = $"http://localhost:{mappedRestPort}";
            if (!await WaitForReady(restEndpoint, context.Cancellation).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Couchbase did not become ready in time.");
            }

            await InitializeCluster(restEndpoint, context.Cancellation).ConfigureAwait(false);
            await CreateBucket(restEndpoint, Bucket, context.Cancellation).ConfigureAwait(false);
            await WaitForBucketReady(restEndpoint, Bucket, context.Cancellation).ConfigureAwait(false);

            ConnectionString = $"couchbase://localhost:{mappedKvPort}";
            Username = DefaultUsername;
            Password = DefaultPassword;
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("couchbase.fixture.container.started", new { host = "localhost", restPort = mappedRestPort, kvPort = mappedKvPort });
            return;
        }
        catch (Exception ex) when (IsTestcontainersMissingMethod(ex, out var mmex))
        {
            var missingMessage = mmex?.Message ?? ex.Message;
            context.Diagnostics.Warn("couchbase.fixture.testcontainers.missingmethod", new { message = missingMessage });
            await DisposeContainerSilently(container).ConfigureAwait(false);

            var (ok, connection, failureReason) = await TryStartWithDockerCli(context).ConfigureAwait(false);
            if (ok && connection is not null)
            {
                ConnectionString = connection;
                Username = DefaultUsername;
                Password = DefaultPassword;
                IsAvailable = true;
                UnavailableReason = null;
                return;
            }

            UnavailableReason = failureReason ?? "Failed to start Couchbase container via Docker CLI fallback.";
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Couchbase container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("couchbase.fixture.container.failed", new { message = ex.Message }, ex);
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

    private static bool TryGetExplicitConnectionString(out string? connectionString)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("Koan_TESTS_COUCHBASE"),
            Environment.GetEnvironmentVariable("Koan_COUCHBASE__CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("COUCHBASE_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("Couchbase__ConnectionString")
        };

        connectionString = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static async Task<bool> WaitForReady(string restEndpoint, CancellationToken cancellation)
    {
        using var http = new HttpClient { BaseAddress = new Uri(restEndpoint) };
        http.Timeout = TimeSpan.FromSeconds(5);

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                var response = await http.GetAsync("/pools", cancellation).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // not ready yet
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

    private static async Task InitializeCluster(string restEndpoint, CancellationToken cancellation)
    {
        using var http = new HttpClient { BaseAddress = new Uri(restEndpoint) };
        http.Timeout = TimeSpan.FromSeconds(30);

        // Initialize cluster services (data only for community edition)
        var setupPayload = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("services", "kv,n1ql,index"),
            new KeyValuePair<string, string>("clusterName", "koan-test")
        ]);
        var setupResponse = await http.PostAsync("/node/controller/setupServices", setupPayload, cancellation).ConfigureAwait(false);
        setupResponse.EnsureSuccessStatusCode();

        // Set admin credentials
        var credPayload = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", DefaultUsername),
            new KeyValuePair<string, string>("password", DefaultPassword),
            new KeyValuePair<string, string>("port", "SAME")
        ]);
        var credResponse = await http.PostAsync("/settings/web", credPayload, cancellation).ConfigureAwait(false);
        credResponse.EnsureSuccessStatusCode();

        // Set memory quota
        var quotaPayload = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("memoryQuota", "256"),
            new KeyValuePair<string, string>("indexMemoryQuota", "256")
        ]);

        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUsername}:{DefaultPassword}")));

        await http.PostAsync("/pools/default", quotaPayload, cancellation).ConfigureAwait(false);
    }

    private static async Task CreateBucket(string restEndpoint, string bucketName, CancellationToken cancellation)
    {
        using var http = new HttpClient { BaseAddress = new Uri(restEndpoint) };
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUsername}:{DefaultPassword}")));

        var bucketPayload = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("name", bucketName),
            new KeyValuePair<string, string>("ramQuota", "128"),
            new KeyValuePair<string, string>("bucketType", "couchbase"),
            new KeyValuePair<string, string>("flushEnabled", "1")
        ]);

        var response = await http.PostAsync("/pools/default/buckets", bucketPayload, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            throw new InvalidOperationException($"Failed to create Couchbase bucket '{bucketName}': {(int)response.StatusCode} {body}");
        }
    }

    private static async Task WaitForBucketReady(string restEndpoint, string bucketName, CancellationToken cancellation)
    {
        using var http = new HttpClient { BaseAddress = new Uri(restEndpoint) };
        http.Timeout = TimeSpan.FromSeconds(5);
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUsername}:{DefaultPassword}")));

        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var response = await http.GetAsync($"/pools/default/buckets/{Uri.EscapeDataString(bucketName)}", cancellation).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // not ready yet
            }

            try
            {
                await Task.Delay(500, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
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

    private async Task<(bool ok, string? connectionString, string? failureReason)> TryStartWithDockerCli(TestContext context)
    {
        var containerName = $"koan-couchbase-{Guid.NewGuid():N}";
        var runArgs = $"run --rm -d --name {containerName} -p 127.0.0.1::{CouchbaseRestPort} -p 127.0.0.1::{CouchbaseKvPort} {ImageName}";
        var (runOk, runStdout, runStderr, runExitCode) = await RunDockerCommand(runArgs, context.Cancellation).ConfigureAwait(false);

        if (!runOk)
        {
            context.Diagnostics.Warn("couchbase.fixture.dockercli.run.failed", new { exitCode = runExitCode, stdout = Truncate(runStdout), stderr = Truncate(runStderr) });
            return (false, null, $"docker run failed (exit {runExitCode})");
        }

        _cliContainerId = containerName;

        (bool ok, string stdout, string stderr, int exitCode) portResult = (false, "", "", 0);
        for (var attempt = 0; attempt < 5 && !portResult.ok; attempt++)
        {
            portResult = await RunDockerCommand($"port {containerName} {CouchbaseKvPort}/tcp", context.Cancellation).ConfigureAwait(false);
            if (!portResult.ok || string.IsNullOrWhiteSpace(portResult.stdout))
            {
                await Task.Delay(200, context.Cancellation).ConfigureAwait(false);
            }
        }

        if (!portResult.ok || string.IsNullOrWhiteSpace(portResult.stdout))
        {
            context.Diagnostics.Warn("couchbase.fixture.dockercli.port.failed", new { exitCode = portResult.exitCode, stdout = Truncate(portResult.stdout), stderr = Truncate(portResult.stderr) });
            await StopCliContainer().ConfigureAwait(false);
            return (false, null, "Failed to determine published Couchbase port from docker CLI");
        }

        var kvPort = ParseDockerPortOutput(portResult.stdout);
        var (restPortOk, restPortStdout, _, _) = await RunDockerCommand($"port {containerName} {CouchbaseRestPort}/tcp", context.Cancellation).ConfigureAwait(false);
        var restPort = restPortOk ? ParseDockerPortOutput(restPortStdout) : 0;

        if (kvPort == 0 || restPort == 0)
        {
            context.Diagnostics.Warn("couchbase.fixture.dockercli.port.parse", new { kvPort, restPort });
            await StopCliContainer().ConfigureAwait(false);
            return (false, null, "Unable to parse published Couchbase ports from docker CLI output");
        }

        var restEndpoint = $"http://127.0.0.1:{restPort}";
        if (!await WaitForReady(restEndpoint, context.Cancellation).ConfigureAwait(false))
        {
            context.Diagnostics.Warn("couchbase.fixture.dockercli.connect.timeout", new { restPort });
            await StopCliContainer().ConfigureAwait(false);
            return (false, null, $"Couchbase container did not respond on localhost:{restPort} within timeout");
        }

        await InitializeCluster(restEndpoint, context.Cancellation).ConfigureAwait(false);
        await CreateBucket(restEndpoint, Bucket, context.Cancellation).ConfigureAwait(false);
        await WaitForBucketReady(restEndpoint, Bucket, context.Cancellation).ConfigureAwait(false);

        var connection = $"couchbase://127.0.0.1:{kvPort}";
        context.Diagnostics.Info("couchbase.fixture.dockercli.started", new { container = containerName, host = "localhost", kvPort, restPort });
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
