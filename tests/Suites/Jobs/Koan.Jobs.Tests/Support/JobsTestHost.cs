using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Koan.Jobs.Tests.Support;

/// <summary>
/// Deterministic in-memory test host: a real Koan app (AddKoan reflective discovery) with the in-memory data
/// connector for work-items, a <see cref="FakeTimeProvider"/> as the clock, and the background worker disabled so
/// the test drives the orchestrator/scheduler explicitly. Advancing the clock controls schedules, timeouts, leases,
/// and deferrals with no real waits.
/// </summary>
public sealed class JobsTestHost : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly IServiceProvider? _previousAmbient;

    private JobsTestHost(IntegrationHost host, IServiceProvider? previousAmbient, FakeTimeProvider clock)
    {
        _host = host;
        _previousAmbient = previousAmbient;
        Clock = clock;
        Orchestrator = host.Services.GetRequiredService<JobOrchestrator>();
        Scheduler = host.Services.GetRequiredService<JobScheduler>();
        Coordinator = host.Services.GetRequiredService<IJobCoordinator>();
        Ledger = host.Services.GetRequiredService<IJobLedger>();
        Registry = host.Services.GetRequiredService<JobTypeRegistry>();
    }

    public FakeTimeProvider Clock { get; }
    public IServiceProvider Services => _host.Services;
    public JobOrchestrator Orchestrator { get; }
    public JobScheduler Scheduler { get; }
    public IJobCoordinator Coordinator { get; }
    public IJobLedger Ledger { get; }
    public JobTypeRegistry Registry { get; }

    public static async Task<JobsTestHost> StartAsync(Action<JobsOptions>? configure = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var host = KoanIntegrationHost.Configure()
            .ConfigureServices(s =>
            {
                s.AddSingleton<TimeProvider>(clock);
                s.AddKoan();
                s.Configure<JobsOptions>(o =>
                {
                    o.EnableWorker = false;          // drive Drain/Reap/ReleaseScheduled explicitly
                    o.RescheduleJitter = TimeSpan.Zero;
                    configure?.Invoke(o);
                });
            })
            .Build();
        await host.StartAsync();
        // Set the ambient host globally (the .Job/.Jobs accessor reads AppHost.Current); tests run serially.
        var previous = AppHost.Current;
        AppHost.Current = host.Services;
        return new JobsTestHost(host, previous, clock);
    }

    public void Advance(TimeSpan by) => Clock.Advance(by);
    public Task Drain(CancellationToken ct = default) => Orchestrator.DrainAsync(ct);
    public Task ReleaseScheduled(CancellationToken ct = default) => Scheduler.ReleaseScheduledAsync(ct);
    public Task Reap(CancellationToken ct = default) => Scheduler.ReapAsync(ct);

    public Task<JobStatus?> StatusOf<T>(string workId) where T : Entity<T>, IKoanJob<T>
        => Coordinator.StatusAsync(typeof(T).FullName!, workId, default);

    public Task<JobRecord?> JobFor<T>(string workId) where T : Entity<T>, IKoanJob<T>
        => GetLatest(typeof(T).FullName!, workId);

    private async Task<JobRecord?> GetLatest(string workType, string workId)
    {
        var jobs = await Ledger.Query(new JobQuery(WorkType: workType, WorkId: workId), default);
        return jobs.OrderByDescending(j => j.FirstSubmittedAt).FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        AppHost.Current = _previousAmbient!;
        try { await _host.StopAsync(); } catch { /* best-effort */ }
        await _host.DisposeAsync();
    }
}
