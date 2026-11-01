using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// DATA-0166: a held transaction's rows are visible to its own flow alone and may yet roll back, so the shared Entity
/// cache must neither answer for them nor learn of them before the commit. Through a real <c>AddKoan()</c> boot on SQLite,
/// the one suite that composes Koan.Cache with that route.
/// </summary>
public sealed class CacheHoldSpec
{
    [HostScoped]
    [Cacheable(300)]
    public sealed class HeldCachedNote : Entity<HeldCachedNote>
    {
        public string Title { get; set; } = "";
    }

    private static IReadOnlyDictionary<string, string?> Posture => new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" };

    /// <summary>A read from a flow of its own: it does not inherit the hold, and it goes through the shared cache.</summary>
    private static Task<HeldCachedNote?> ReadOutside(string id, string partition)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(async () =>
            {
                using (EntityContext.Partition(partition)) return await HeldCachedNote.Get(id);
            });
    }

    [Fact]
    public async Task Inside_a_hold_a_read_sees_its_own_write_while_other_flows_read_the_committed_value_and_the_commit_retires_it()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture);
        runtime.ResetEntityCaches();
        var partition = "p" + Guid.CreateVersion7().ToString("n");
        using var _ = EntityContext.Partition(partition);
        var note = await new HeldCachedNote { Title = "committed" }.Save();
        (await HeldCachedNote.Get(note.Id))!.Title.Should().Be("committed", "this read primes the shared cache");

        using (await EntityContext.Hold("cached"))
        {
            note.Title = "held";
            await note.Save();
            (await HeldCachedNote.Get(note.Id))!.Title.Should().Be("held", "a cache hit must not answer for the hold's own write");
            (await ReadOutside(note.Id, partition))!.Title.Should().Be("committed", "the hold's write is not committed");
            await EntityContext.Commit();
        }

        (await HeldCachedNote.Get(note.Id))!.Title.Should().Be("held", "the commit retired the cached entry");
        (await ReadOutside(note.Id, partition))!.Title.Should().Be("held");
    }

    [Fact]
    public async Task A_rolled_back_hold_leaves_no_phantom_in_the_cache()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture);
        runtime.ResetEntityCaches();
        var partition = "p" + Guid.CreateVersion7().ToString("n");
        using var _ = EntityContext.Partition(partition);
        var existing = await new HeldCachedNote { Title = "committed" }.Save();
        var created = new HeldCachedNote { Title = "never committed" };

        using (await EntityContext.Hold("rolled back"))
        {
            await created.Save();
            (await HeldCachedNote.Get(created.Id)).Should().NotBeNull();
            existing.Title = "never committed";
            await existing.Save();
            (await HeldCachedNote.Get(existing.Id))!.Title.Should().Be("never committed");
            await EntityContext.Rollback();
        }

        (await HeldCachedNote.Get(created.Id)).Should().BeNull("a read inside the hold must not have seeded the cache");
        (await HeldCachedNote.Get(existing.Id))!.Title.Should().Be("committed", "a write inside the hold must not have seeded the cache");
    }
}
