using System.IO;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Koan.Jobs.TestKit;

/// <summary>
/// One deterministic test harness for every tier. A real Koan app (AddKoan reflective discovery) with the data
/// adapter the consuming project references, a <see cref="FakeTimeProvider"/> as the clock, and the background
/// worker disabled so the test drives the orchestrator/scheduler explicitly. The same harness API powers the
/// shared <see cref="JobBehaviorSuite"/> on in-memory and on each durable adapter.
/// </summary>
public sealed class JobsHarness : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly IServiceProvider? _prev;
    private readonly string? _dbPath;

    private JobsHarness(IntegrationHost host, IServiceProvider? prev, FakeTimeProvider clock, string? dbPath)
    {
        _host = host;
        _prev = prev;
        _dbPath = dbPath;
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

    /// <summary>In-memory tier — uses whatever (non-durable) data adapter the consuming project references.</summary>
    public static Task<JobsHarness> StartInMemoryAsync(Action<JobsOptions>? configure = null)
        => StartCore(configure, null, null);

    /// <summary>SQLite durable tier — a temp-file database (the consuming project references the SQLite connector).</summary>
    public static Task<JobsHarness> StartSqliteAsync(Action<JobsOptions>? configure = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"koan-jobs-{Guid.NewGuid():n}.db");
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={dbPath}",
        };
        return StartCore(configure, settings, dbPath);
    }

    /// <summary>Generic durable tier — caller supplies the data-source settings (Mongo/Postgres/SqlServer).</summary>
    public static Task<JobsHarness> StartWithSettingsAsync(IReadOnlyDictionary<string, string?> settings, Action<JobsOptions>? configure = null)
        => StartCore(configure, settings, null);

    private static async Task<JobsHarness> StartCore(Action<JobsOptions>? configure, IReadOnlyDictionary<string, string?>? settings, string? dbPath)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var builder = KoanIntegrationHost.Configure();
        if (settings is not null) builder = builder.WithSettings(settings);
        var host = builder
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
        // Clean slate: a durable store may be reused in-process across tests; clear the Jobs-owned sets.
        await JobRecord.RemoveAll();
        await JobGateRecord.RemoveAll();
        await JobClaimTicket.RemoveAll();
        return new JobsHarness(host, prev, clock, dbPath);
    }

    public void Advance(TimeSpan by) => Clock.Advance(by);
    public Task Drain(CancellationToken ct = default) => Orchestrator.DrainAsync(ct);
    public Task TriggerDue(CancellationToken ct = default) => Scheduler.TriggerDueAsync(ct);
    public Task Boot(CancellationToken ct = default) => Scheduler.SubmitBootActionsAsync(ct);
    public Task Reap(CancellationToken ct = default) => Scheduler.ReapAsync(ct);
    public Task<int> Archive(CancellationToken ct = default) => Orchestrator.ArchiveAsync(ct);

    public Task<JobStatus?> StatusOf<T>(string workId) where T : Entity<T>, IKoanJob<T>
        => Coordinator.StatusAsync(typeof(T).FullName!, workId, default);

    public async Task<JobRecord?> JobFor<T>(string workId) where T : Entity<T>, IKoanJob<T>
    {
        var jobs = await Ledger.Query(new JobQuery(WorkType: typeof(T).FullName!, WorkId: workId), default);
        return jobs.OrderByDescending(j => j.FirstSubmittedAt).FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        AppHost.Current = _prev!;
        try { await _host.StopAsync(); } catch { /* best-effort */ }
        await _host.DisposeAsync();
        if (_dbPath is not null)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
        }
    }
}
