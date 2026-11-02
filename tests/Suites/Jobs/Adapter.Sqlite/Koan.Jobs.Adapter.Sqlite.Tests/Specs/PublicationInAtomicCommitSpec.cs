using System.Collections.Concurrent;
using Koan.Communication;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>ARCH-0142: an event or a snapshot published from a lifecycle hook while an atomic commit dispatches waits for that commit, and its rollback takes it away. An
/// atomic commit needs a store that declares one, and this is the suite that composes Koan.Communication with SQLite.</summary>
public sealed class PublicationInAtomicCommitSpec
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Claimed(string Note);

    public sealed class HookedClaim : Entity<HookedClaim>, IAmbientExempt
    {
        [Index(Unique = true)] public string Code { get; set; } = "";
    }

    public static readonly ConcurrentQueue<string> Seen = new();

    public sealed class Handler : IHandleEntityEvent<HookedClaim, Claimed>
    {
        public Task Handle(HookedClaim entity, EventOccurrence<Claimed> occurrence, CancellationToken ct)
        {
            Seen.Enqueue("event:" + entity.Code);
            return Task.CompletedTask;
        }
    }

    public sealed class Receiver : IReceiveEntity<HookedClaim>
    {
        public Task Receive(HookedClaim entity, CancellationToken ct)
        {
            Seen.Enqueue("snapshot:" + entity.Code);
            return Task.CompletedTask;
        }
    }

    private static Task<IntegrationHost> Start(string path) => KoanIntegrationHost.Configure()
        .WithSetting("Koan:Environment", "Test")
        .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
        .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={path}")
        .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
        .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
        .ConfigureServices(services => services.AddKoan(() => HookedClaim.Lifecycle.AfterUpsert(async context =>
        {
            await context.Current.Events.Raise(new Claimed("hook"), context.CancellationToken);
            await context.Current.Transport.Send(context.CancellationToken);
        })))
        .StartAsync();

    private static void Remove(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"koan-jobs-atomic-{Guid.NewGuid():n}.db");

    [Fact]
    public async Task A_rollback_of_the_commit_takes_away_what_its_hook_published()
    {
        var path = NewPath();
        Seen.Clear();
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var failing = async () =>
            {
                using (EntityContext.Transaction("collides", TransactionGuarantee.Atomic))
                {
                    await new HookedClaim { Code = "same" }.Save(Ct);
                    await new HookedClaim { Code = "same" }.Save(Ct);
                    await EntityContext.Commit(Ct);
                }
            };
            await failing.Should().ThrowAsync<TransactionException>();

            await Task.Delay(300, Ct);
            (await HookedClaim.Query(claim => claim.Code == "same", Ct)).Should().BeEmpty();
            Seen.Should().BeEmpty("nothing landed, so nothing happened");
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task A_commit_publishes_what_its_hook_published_after_it_lands()
    {
        var path = NewPath();
        Seen.Clear();
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            using (EntityContext.Transaction("lands", TransactionGuarantee.Atomic))
            {
                await new HookedClaim { Code = "landed" }.Save(Ct);
                await EntityContext.Commit(Ct);
            }

            for (var wait = 0; wait < 100 && Seen.Count < 2; wait++) await Task.Delay(50, Ct);
            Seen.Should().BeEquivalentTo("event:landed", "snapshot:landed");
        }
        finally
        {
            Remove(path);
        }
    }
}
