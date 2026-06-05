using System.IO;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Support;

/// <summary>
/// Durable test host backed by a real (temp-file) SQLite database — proves the data-backed ledger works on a live
/// relational store (claim query translation, chain persistence, reclaim) with the deterministic FakeTimeProvider
/// driver. The election picks <c>DataJobLedger</c> because a non-in-memory data adapter is present.
/// </summary>
public sealed class DurableHost : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly IServiceProvider? _prev;
    private readonly string _dbPath;

    private DurableHost(IntegrationHost host, IServiceProvider? prev, FakeTimeProvider clock, string dbPath)
    {
        _host = host;
        _prev = prev;
        Clock = clock;
        _dbPath = dbPath;
        Orchestrator = host.Services.GetRequiredService<JobOrchestrator>();
        Scheduler = host.Services.GetRequiredService<JobScheduler>();
        Coordinator = host.Services.GetRequiredService<IJobCoordinator>();
        Ledger = host.Services.GetRequiredService<IJobLedger>();
    }

    public FakeTimeProvider Clock { get; }
    public JobOrchestrator Orchestrator { get; }
    public JobScheduler Scheduler { get; }
    public IJobCoordinator Coordinator { get; }
    public IJobLedger Ledger { get; }

    public static async Task<DurableHost> StartAsync(Action<JobsOptions>? configure = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var dbPath = Path.Combine(Path.GetTempPath(), $"koan-jobs-{Guid.NewGuid():n}.db");
        var host = KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={dbPath}")
            .ConfigureServices(s =>
            {
                s.AddSingleton<TimeProvider>(clock);
                s.AddKoan();
                s.Configure<JobsOptions>(o =>
                {
                    o.EnableWorker = false;
                    o.RescheduleJitter = TimeSpan.Zero;
                    configure?.Invoke(o);
                });
            })
            .Build();
        await host.StartAsync();
        var prev = AppHost.Current;
        AppHost.Current = host.Services;
        return new DurableHost(host, prev, clock, dbPath);
    }

    public void Advance(TimeSpan by) => Clock.Advance(by);
    public Task Drain(CancellationToken ct = default) => Orchestrator.DrainAsync(ct);
    public Task Reap(CancellationToken ct = default) => Scheduler.ReapAsync(ct);

    public Task<JobStatus?> StatusOf<T>(string workId) where T : Entity<T>, IKoanJob<T>
        => Coordinator.StatusAsync(typeof(T).FullName!, workId, default);

    public async ValueTask DisposeAsync()
    {
        AppHost.Current = _prev!;
        try { await _host.StopAsync(); } catch { /* best-effort */ }
        await _host.DisposeAsync();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort temp cleanup */ }
    }
}
