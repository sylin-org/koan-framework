using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Jobs.Semantics;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Koan.Jobs.Tests;

/// <summary>JOBS-0010: the production worker runs each duty on its own timer. A job that runs longer than the roster
/// death timeout keeps its node alive in the roster, the scheduler keeps firing while it runs, and the job completes.</summary>
public sealed class WorkerLoopSpec
{
    [Fact]
    public async Task a_job_longer_than_the_death_timeout_completes_while_the_node_heartbeats_and_schedules()
    {
        HeldJob.Reset();
        Reconciled.Reset();
        var clock = new FakeTimeProvider(new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero));
        await using var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddKoan();
                services.Configure<JobsOptions>(o =>
                {
                    o.EnableWorker = true;
                    o.PollInterval = TimeSpan.FromMilliseconds(50);
                    o.RescheduleJitter = TimeSpan.Zero;
                    o.WorkerConcurrency = 4;
                });
            })
            .StartAsync();
        var previous = AppHost.Current;
        AppHost.Current = host.Services;
        try
        {
            var options = host.Services.GetRequiredService<IOptions<JobsOptions>>().Value;
            var node = host.Services.GetRequiredService<JobOrchestrator>();
            var ledger = host.Services.GetRequiredService<IJobLedger>();

            await Wait.Until(() => Reconciled.Executions >= 1);   // the scheduler's first tick fires at start

            var held = new HeldJob();
            var id = held.Id;
            await held.Job.Submit();
            await Wait.Until(() => HeldJob.Executions == 1);

            // Run well past the death timeout, and past one Reconciled interval, one heartbeat at a time.
            var end = clock.GetUtcNow() + options.WorkerDeathTimeout * 2 + TimeSpan.FromMinutes(10);
            while (clock.GetUtcNow() < end)
            {
                clock.Advance(options.WorkerHeartbeatInterval);
                var beat = clock.GetUtcNow();
                await Wait.Until(() => LastSeen(node.Owner) >= beat);
            }

            await Wait.Until(() => Reconciled.Executions >= 2);   // a schedule fired while the long job ran

            // A peer's reaper sees a live node, so it reclaims nothing.
            await Peer(host, clock).ReapAsync();
            var running = await ledger.Get(JobOf(ledger, id), default);
            running!.Status.Should().Be(JobStatus.Running);
            running.Owner.Should().Be(node.Owner);
            running.Attempt.Should().Be(1);

            HeldJob.Release();
            await Wait.Until(() => ledger.Get(running.Id, default).GetAwaiter().GetResult()?.Status == JobStatus.Completed);
            HeldJob.Executions.Should().Be(1, "the job ran once, never reclaimed by a peer");
        }
        finally
        {
            HeldJob.Release();
            AppHost.Current = previous!;
        }
    }

    [Fact]
    public async Task a_duty_interval_longer_than_a_timer_accepts_still_runs_its_duty()
    {
        // JOBS-0010: a timer refuses a wait over about 49.7 days; the worker waits longer intervals in parts, so a reaper
        // set to sixty days reaps on day sixty, and an archival sweep set to TimeSpan.MaxValue (never) faults nothing.
        GreetJob.Reset();
        var clock = new FakeTimeProvider(new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero));
        var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddKoan();
                services.Configure<JobsOptions>(o =>
                {
                    o.EnableWorker = true;
                    o.PollInterval = TimeSpan.FromMilliseconds(50);
                    o.ReaperInterval = TimeSpan.FromDays(60);
                    o.ArchiveInterval = TimeSpan.MaxValue;
                    o.ReclaimFromConfirmedDead = false;
                });
            })
            .StartAsync();
        var previous = AppHost.Current;
        AppHost.Current = host.Services;
        try
        {
            var worker = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<JobWorkerService>().Single();
            var ledger = host.Services.GetRequiredService<IJobLedger>();
            var item = new GreetJob { Name = "reaped on day sixty" };
            await GreetJob.Upsert(item);
            var now = clock.GetUtcNow();
            await ledger.Append(new JobRecord
            {
                WorkType = typeof(GreetJob).FullName!, WorkId = item.Id, Action = "", Lane = "default",
                Status = JobStatus.Running, Attempt = 1, Owner = "a-node-that-stopped",
                FirstSubmittedAt = now, VisibleAt = now, LeaseUntil = now + TimeSpan.FromDays(1),
            }, default);

            for (var day = 0; day < 200 && GreetJob.Executions == 0; day++)
            {
                clock.Advance(TimeSpan.FromDays(1));
                await Task.Delay(20);
            }

            GreetJob.Executions.Should().Be(1, "the sixty-day reaper reclaimed the lapsed job and the worker ran it");
            worker.ExecuteTask!.IsFaulted.Should().BeFalse();
            await host.StopAsync();
            worker.ExecuteTask.IsFaulted.Should().BeFalse("no duty faulted on its interval");
        }
        finally
        {
            AppHost.Current = previous!;
            await host.DisposeAsync();
        }
    }

    private static DateTimeOffset LastSeen(string owner)
        => WorkerNode.Get(owner).GetAwaiter().GetResult()?.LastSeenAt ?? DateTimeOffset.MinValue;

    private static string JobOf(IJobLedger ledger, string workId)
        => ledger.Query(new JobQuery(WorkType: typeof(HeldJob).FullName!, WorkId: workId), default)
            .GetAwaiter().GetResult().Single().Id;

    private static JobOrchestrator Peer(IntegrationHost host, TimeProvider clock)
        => new(
            host.Services.GetRequiredService<IJobLedger>(),
            host.Services.GetRequiredService<JobTypeRegistry>(),
            host.Services.GetRequiredService<IOptions<JobsOptions>>(),
            clock,
            NullLogger<JobOrchestrator>.Instance,
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            Enumerable.Empty<IJobPoolResolver>(),
            host.Services.GetRequiredService<JobsContextPlan>());
}
