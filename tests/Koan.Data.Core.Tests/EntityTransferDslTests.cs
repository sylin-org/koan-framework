using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transfers;
using Koan.Data.Connector.Json;
using Xunit;

namespace Koan.Data.Core.Tests;

public sealed class EntityTransferDslTests : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _workDir;

    public EntityTransferDslTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "Koan-Transfer-Tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workDir);

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Json:DirectoryPath"] = _workDir
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoan();
        services.AddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.AddJsonAdapter(o => o.DirectoryPath = _workDir);

        _serviceProvider = services.BuildServiceProvider();

        try { KoanEnv.TryInitialize(_serviceProvider); } catch { }
        Koan.Core.Hosting.App.AppHost.Current = _serviceProvider;
        _serviceProvider.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>()?.Discover();
        _serviceProvider.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>()?.Start();
    }

    [Fact]
    public async Task Copy_ToPartition_CopiesFilteredEntities()
    {
        await ResetAsync();

        using (EntityContext.Partition("active"))
        {
            await new TransferTodo { Title = "A", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            await new TransferTodo { Title = "B", Active = false, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var audits = new List<TransferAuditBatch>();
        var result = await TransferTodo.Copy(p => p.Active)
            .From(partition: "active")
            .To(partition: "inactive")
            .Audit(audits.Add)
            .Run();

        result.Kind.Should().Be(TransferKind.Copy);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);
        result.ReadCount.Should().Be(1);
        result.Warnings.Should().BeEmpty();
        audits.Should().NotBeEmpty();
        audits.Last().IsSummary.Should().BeTrue();

        using (EntityContext.Partition("inactive"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(x => x.Title == "A");
        }

        using (EntityContext.Partition("active"))
        {
            var items = await TransferTodo.All();
            items.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Copy_QueryShaper_AppliesFilter()
    {
        await ResetAsync();

        await new TransferTodo { Title = "keep", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        await new TransferTodo { Title = "drop", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Copy(query => query.Where(t => t.Title == "keep"))
            .To(partition: "filtered")
            .Run();

        result.CopiedCount.Should().Be(1);
        using (EntityContext.Partition("filtered"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(t => t.Title == "keep");
        }
    }

    [Fact]
    public async Task Move_DefaultStrategy_RemovesFromSource()
    {
        await ResetAsync();

        using (EntityContext.Partition("hot"))
        {
            for (var i = 0; i < 3; i++)
            {
                await new TransferTodo { Title = $"Item {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move(p => true)
            .From(partition: "hot")
            .To(partition: "archive")
            .Run();

        result.Kind.Should().Be(TransferKind.Move);
        result.CopiedCount.Should().Be(3);
        result.DeletedCount.Should().Be(3);

        using (EntityContext.Partition("hot"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }

        using (EntityContext.Partition("archive"))
        {
            (await TransferTodo.All()).Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task Move_BatchedStrategy_RespectsBatching()
    {
        await ResetAsync();

        using (EntityContext.Partition("batch"))
        {
            for (var i = 0; i < 4; i++)
            {
                await new TransferTodo { Title = $"Batch {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "batch")
            .To(partition: "dest")
            .BatchSize(1)
            .WithDeleteStrategy(DeleteStrategy.Batched)
            .Run();

        result.CopiedCount.Should().Be(4);
        result.DeletedCount.Should().Be(4);

        using (EntityContext.Partition("batch"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Move_SyncedStrategy_RemovesAsItGoes()
    {
        await ResetAsync();

        using (EntityContext.Partition("sync"))
        {
            for (var i = 0; i < 2; i++)
            {
                await new TransferTodo { Title = $"Sync {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "sync")
            .To(partition: "sync-target")
            .BatchSize(1)
            .WithDeleteStrategy(DeleteStrategy.Synced)
            .Run();

        result.CopiedCount.Should().Be(2);
        result.DeletedCount.Should().Be(2);

        using (EntityContext.Partition("sync"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Mirror_Push_SynchronizesToTarget()
    {
        await ResetAsync();

        await new TransferTodo { Title = "primary", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Mirror()
            .To(partition: "mirror")
            .Run();

        result.Kind.Should().Be(TransferKind.Mirror);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);
        result.Audit.Last().IsSummary.Should().BeTrue();

        using (EntityContext.Partition("mirror"))
        {
            (await TransferTodo.All()).Should().ContainSingle(x => x.Title == "primary");
        }
    }

    [Fact]
    public async Task Mirror_Pull_SynchronizesBackToDefault()
    {
        await ResetAsync();

        using (EntityContext.Partition("mirror"))
        {
            await new TransferTodo { Title = "remote", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Pull)
            .To(partition: "mirror")
            .Run();

        result.CopiedCount.Should().Be(1);

        var all = await TransferTodo.All();
        all.Should().ContainSingle(x => x.Title == "remote");
    }

    [Fact]
    public async Task Mirror_Bidirectional_UsesTimestampForResolution()
    {
        await ResetAsync();

        var primary = await new TransferTodo { Title = "v1", Active = true, UpdatedAt = DateTime.UtcNow.AddMinutes(-2) }.Save();

        using (EntityContext.Partition("reporting"))
        {
            await new TransferTodo { Id = primary.Id, Title = "v2", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "reporting")
            .Run();

        result.Conflicts.Should().BeEmpty();
        result.Audit.Last().IsSummary.Should().BeTrue();

        var updated = await TransferTodo.Get(primary.Id);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("v2");

        using (EntityContext.Partition("reporting"))
        {
            var target = await TransferTodo.Get(primary.Id);
            target.Should().NotBeNull();
            target!.Title.Should().Be("v2");
        }
    }

    [Fact]
    public async Task Mirror_Bidirectional_WithoutTimestamp_ReportsConflicts()
    {
        await ResetAsync();

        var baseNote = await new BasicNote { Content = "default" }.Save();

        using (EntityContext.Partition("secondary"))
        {
            await new BasicNote { Id = baseNote.Id, Content = "secondary" }.Save();
        }

        var result = await BasicNote.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "secondary")
            .Run();

        result.Conflicts.Should().NotBeEmpty();
        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("No [Timestamp]"));

        var defaultNote = await BasicNote.Get(baseNote.Id);
        defaultNote!.Content.Should().Be("default");
    }

    [Fact]
    public void To_WithSourceAndAdapter_ShouldThrow()
    {
        Action act = () => TransferTodo.Copy().To(source: "primary", adapter: "sqlite");
        act.Should().Throw<InvalidOperationException>();
    }

    private static async Task ResetAsync()
    {
        await TransferTodo.RemoveAll();
        using (EntityContext.Partition("active")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("inactive")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("hot")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("archive")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("batch")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("dest")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("mirror")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("filtered")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("sync")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("sync-target")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("reporting")) { await TransferTodo.RemoveAll(); }
        using (EntityContext.Partition("secondary")) { await BasicNote.RemoveAll(); }
        await BasicNote.RemoveAll();
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(_workDir))
        {
            try { Directory.Delete(_workDir, recursive: true); } catch { }
        }
    }

    public class TransferTodo : Entity<TransferTodo>
    {
        public string Title { get; set; } = string.Empty;
        public bool Active { get; set; }
        [Timestamp]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BasicNote : Entity<BasicNote>
    {
        public string Content { get; set; } = string.Empty;
    }
}
