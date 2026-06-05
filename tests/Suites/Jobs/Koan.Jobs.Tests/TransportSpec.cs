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

/// <summary>Push-dispatch (the +bus seam): the in-process transport signals/coalesces/times out, and a submit
/// notifies the transport so the worker wakes immediately instead of waiting out the poll interval.</summary>
public sealed class TransportSpec
{
    [Fact]
    public async Task in_process_transport_signals_coalesces_and_times_out()
    {
        var t = new InProcessJobTransport();

        // No signal → WaitForWork blocks until the timeout elapses.
        var sw = Stopwatch.StartNew();
        await t.WaitForWork(TimeSpan.FromMilliseconds(60), default);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);

        // A Notify makes the next wait return immediately.
        t.Notify();
        sw.Restart();
        await t.WaitForWork(TimeSpan.FromSeconds(5), default);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);

        // Multiple notifies coalesce to a single pending wake.
        t.Notify();
        t.Notify();
        await t.WaitForWork(TimeSpan.FromSeconds(5), default);          // consumes the one pending wake
        sw.Restart();
        await t.WaitForWork(TimeSpan.FromMilliseconds(60), default);    // nothing left → times out
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task submitting_a_job_notifies_the_transport()
    {
        GreetJob.Reset();
        var spy = new SpyTransport();
        var host = KoanIntegrationHost.Configure()
            .ConfigureServices(s =>
            {
                s.AddSingleton<IJobTransport>(spy);   // registered before AddKoanJobs' TryAdd → wins the election
                s.AddKoan();
                s.Configure<JobsOptions>(o => o.EnableWorker = false);
            })
            .Build();
        await host.StartAsync();
        var prev = AppHost.Current;
        AppHost.Current = host.Services;
        try
        {
            await new GreetJob { Name = "push" }.Job.Submit();
            spy.Notified.Should().BeGreaterThan(0, "a submit must wake the worker via the transport");
        }
        finally { AppHost.Current = prev!; try { await host.StopAsync(); } catch { } await host.DisposeAsync(); }
    }

    private sealed class SpyTransport : IJobTransport
    {
        public int Notified;
        public void Notify() => Interlocked.Increment(ref Notified);
        public Task WaitForWork(TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
    }
}
