using System.Diagnostics;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Audit;
using Koan.Identity.Roles;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

/// <summary>ARCH-0142: <c>Koan:Identity:HashChainAudit</c> reads its head from the store in the transaction it writes in, so a rollback leaves no gap to report, and an
/// append outside a hold takes the store's turn, so it never holds a lock while waiting for the store a hold has.</summary>
public sealed class AuditChainHoldSpec
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Temp(string label) => Path.Combine(Path.GetTempPath(), $"koan-audit-chain-{label}-{Guid.CreateVersion7():n}.db");

    private static Task<IntegrationHost> Start(string path) => KoanIntegrationHost.Configure()
        .WithSetting("Koan:Environment", "Test")
        .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
        .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={path}")
        .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
        .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
        .WithSetting("Koan:Identity:HashChainAudit", "true")
        .ConfigureServices(services => services.AddKoan())
        .StartAsync();

    private static void Remove(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Fact]
    public async Task A_hold_that_rolls_back_after_a_role_change_leaves_no_gap_to_report()
    {
        var path = Temp("rollback");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            var chain = host.Services.GetRequiredService<AuditChain>();
            await roles.Define("role:one", "One", ["global:one"]);

            using (await EntityContext.Hold("rolled-back", Ct))
            {
                await roles.Define("role:never", "Never", ["global:never"]);
                await EntityContext.Rollback(Ct);
            }

            await roles.Define("role:two", "Two", ["global:two"]);
            var verified = await chain.VerifyAsync(Ct);
            verified.Intact.Should().BeTrue(verified.Detail);
            verified.EventsOrBrokenAt.Should().BeGreaterThan(0, "the role changes that stand are chained");
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task An_append_outside_a_hold_waits_for_the_store_without_holding_up_the_hold()
    {
        var path = Temp("contention");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            var chain = host.Services.GetRequiredService<AuditChain>();
            await roles.Define("role:one", "One", ["global:one"]);

            var clock = Stopwatch.StartNew();
            Task outside;
            using (await EntityContext.Hold("contended", Ct))
            {
                using (ExecutionContext.SuppressFlow())
                    outside = Task.Run(async () =>
                    {
                        using var scope = AppHost.PushScope(host.Services);
                        await chain.AppendAsync(new AuditEvent { Action = "outside.append", Subject = "s", Target = "T/1" }, ev => ev.Save(Ct), Ct);
                    }, Ct);
                await Task.Delay(100, Ct);
                outside.IsCompleted.Should().BeFalse("the hold has the store");
                // The hold's own audit row appends without waiting for anything the outside flow holds.
                await roles.Define("role:held", "Held", ["global:held"]);
                await EntityContext.Commit(Ct);
            }

            await outside.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "nothing waited for the hold's limit");
            var verified = await chain.VerifyAsync(Ct);
            verified.Intact.Should().BeTrue(verified.Detail);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task Rows_saved_in_a_scope_that_defers_chain_after_its_commit()
    {
        var path = Temp("deferred");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var chain = host.Services.GetRequiredService<AuditChain>();

            using (EntityContext.Transaction("deferring"))
            {
                await chain.AppendAsync(new AuditEvent { Action = "deferred.one", Subject = "s", Target = "T/1" }, ev => ev.Save(Ct), Ct);
                await chain.AppendAsync(new AuditEvent { Action = "deferred.two", Subject = "s", Target = "T/2" }, ev => ev.Save(Ct), Ct);
                await EntityContext.Commit(Ct);
            }

            var verified = await chain.VerifyAsync(Ct);
            verified.Intact.Should().BeTrue(verified.Detail);
            verified.EventsOrBrokenAt.Should().Be(2);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task A_new_host_continues_the_chain_it_finds()
    {
        var path = Temp("restart");
        try
        {
            await using (var first = await Start(path))
            {
                using var app = AppHost.PushScope(first.Services);
                await first.Services.GetRequiredService<AuditChain>().AppendAsync(new AuditEvent { Action = "first", Subject = "s", Target = "T/1" }, ev => ev.Save(Ct), Ct);
            }

            await using var second = await Start(path);
            using var again = AppHost.PushScope(second.Services);
            var chain = second.Services.GetRequiredService<AuditChain>();
            await chain.AppendAsync(new AuditEvent { Action = "second", Subject = "s", Target = "T/2" }, ev => ev.Save(Ct), Ct);
            var verified = await chain.VerifyAsync(Ct);
            verified.Intact.Should().BeTrue(verified.Detail);
            verified.EventsOrBrokenAt.Should().Be(2);
        }
        finally
        {
            Remove(path);
        }
    }
}
