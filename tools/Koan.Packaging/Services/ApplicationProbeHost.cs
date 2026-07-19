using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class ApplicationProbeHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    private readonly string stateRoot;
    private bool stopped;

    private ApplicationProbeHost(Process process, HttpClient http, Task<string> stdout, Task<string> stderr, string stateRoot)
    {
        this.process = process;
        Http = http;
        this.stdout = stdout;
        this.stderr = stderr;
        this.stateRoot = stateRoot;
    }

    public HttpClient Http { get; }

    public static ApplicationProbeHost Start(
        string applicationDirectory,
        string projectFile,
        string stateName,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool configureIsolatedSqliteTarget = true)
    {
        var port = GrabFreePort();
        var stateRoot = Path.Combine(Path.GetTempPath(), $"koan-{stateName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateRoot);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = applicationDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "run", "--project", projectFile, "-c", "Release", "--no-build", "--urls", $"http://127.0.0.1:{port}"
        }) startInfo.ArgumentList.Add(argument);

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        if (configureIsolatedSqliteTarget)
        {
            startInfo.Environment["Koan__Data__Sqlite__ConnectionString"] =
                $"Data Source={Path.Combine(stateRoot, stateName + ".db")}";
        }
        if (environment is not null)
        {
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {projectFile}.");
        var http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(PackagingConstants.ApplicationProbe.HttpTimeoutSeconds)
        };
        return new ApplicationProbeHost(
            process,
            http,
            process.StandardOutput.ReadToEndAsync(),
            process.StandardError.ReadToEndAsync(),
            stateRoot);
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < PackagingConstants.ApplicationProbe.StartupAttempts; attempt++)
        {
            if (process.HasExited) break;
            try
            {
                using var response = await Http.GetAsync(PackagingConstants.ApplicationProbe.HealthPath, cancellationToken);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(PackagingConstants.ApplicationProbe.StartupPollMilliseconds, cancellationToken);
        }

        throw new InvalidOperationException("Application health did not become ready within the bounded startup window.");
    }

    public async Task<(string StandardOutput, string StandardError)> StopAsync()
    {
        if (!stopped)
        {
            stopped = true;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        return (await stdout, await stderr);
    }

    public async Task<(int ExitCode, string StandardOutput, string StandardError)> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        var logs = await StopAsync();
        return (process.ExitCode, logs.StandardOutput, logs.StandardError);
    }

    public async Task<InvalidOperationException> FailureAsync(string context, Exception exception)
    {
        var logs = await StopAsync();
        return new InvalidOperationException(
            $"{context}: {exception.Message}{Environment.NewLine}{logs.StandardOutput}{logs.StandardError}", exception);
    }

    public async ValueTask DisposeAsync()
    {
        _ = await StopAsync();
        Http.Dispose();
        process.Dispose();
        try { Directory.Delete(stateRoot, recursive: true); } catch { }
    }

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
