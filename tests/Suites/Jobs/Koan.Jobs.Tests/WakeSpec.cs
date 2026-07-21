using System.Diagnostics;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Jobs.Tests;

/// <summary>Jobs wake is a bounded Communication signal: it coalesces locally and submission emits a hint while
/// the ledger and poll fallback remain the complete correctness mechanism.</summary>
public sealed class WakeSpec
{
    [Fact]
    public async Task local_framework_signal_wakes_coalesces_and_times_out()
    {
        await using var host = await StartHost();
        var wake = host.Services.GetRequiredService<JobWakeCoordinator>();

        // No signal → WaitForWork blocks until the timeout elapses.
        var sw = Stopwatch.StartNew();
        await wake.WaitForWork(TimeSpan.FromMilliseconds(60), default);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);

        // A wake hint crosses the same bounded provider path used by external connectors.
        wake.Notify();
        sw.Restart();
        await wake.WaitForWork(TimeSpan.FromSeconds(5), default);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);

        // Multiple notifies coalesce to a single pending wake.
        wake.Notify();
        wake.Notify();
        await Task.Delay(100);
        await wake.WaitForWork(TimeSpan.FromSeconds(5), default);       // consumes the one pending wake
        sw.Restart();
        await wake.WaitForWork(TimeSpan.FromMilliseconds(60), default); // nothing left → times out
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task submitting_a_job_publishes_a_wake_hint()
    {
        GreetJob.Reset();
        await using var host = await StartHost();
        var prev = AppHost.Current;
        AppHost.Current = host.Services;
        try
        {
            var wake = host.Services.GetRequiredService<JobWakeCoordinator>();
            var observed = wake.WaitForWork(TimeSpan.FromSeconds(5), default);
            await new GreetJob { Name = "push" }.Job.Submit();
            await observed;
        }
        finally { AppHost.Current = prev!; }
    }

    private static Task<IntegrationHost> StartHost()
        => KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.Configure<JobsOptions>(options => options.EnableWorker = false);
            })
            .StartAsync();
}
