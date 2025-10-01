using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core;
using Koan.Core.Hosting.Runtime;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Events;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Core.Tests;

public sealed class EntityLifecycleEventsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IServiceProvider _serviceProvider;

    public EntityLifecycleEventsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Koan-EntityLifecycleEvents", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Koan:Data:Json:DirectoryPath", _tempDir)
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoan();
        services.AddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.AddJsonAdapter(o => o.DirectoryPath = _tempDir);

        _serviceProvider = services.BuildServiceProvider();

        try { KoanEnv.TryInitialize(_serviceProvider); } catch { }
        Koan.Core.Hosting.App.AppHost.Current = _serviceProvider;

        var runtime = _serviceProvider.GetService<IAppRuntime>();
        runtime?.Discover();
        runtime?.Start();

        ResetEvents();
    }

    public void Dispose()
    {
        ResetEvents();
        Koan.Core.Hosting.App.AppHost.Current = null;
        try
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // best effort to dispose
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static void ResetEvents()
    {
        LifecycleEntity.Events.Reset();
        AggregateConfigs.Reset();
    }

    private sealed class SetScope : IDisposable
    {
        private readonly IDisposable _lease;

        public SetScope(string name)
        {
            Name = name;
            _lease = EntityContext.Partition(name);
        }

        public string Name { get; }

        public void Dispose() => _lease.Dispose();
    }

    private static SetScope UseUniqueSet() => new(Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task BeforeUpsertCancellationPreventsPersistence()
    {
        ResetEvents();

        using var set = UseUniqueSet();

        LifecycleEntity.Events
            .BeforeUpsert(ctx =>
            {
                if (ctx.Current.Title.Contains("blocked", StringComparison.OrdinalIgnoreCase))
                {
                    return new ValueTask<EntityEventResult>(ctx.Cancel("blocked"));
                }

                return new ValueTask<EntityEventResult>(ctx.Proceed());
            });

        var blocked = new LifecycleEntity { Title = "Blocked Draft" };

        await Assert.ThrowsAsync<EntityEventCancelledException>(() => LifecycleEntity.UpsertAsync(blocked));

        var all = await LifecycleEntity.All(set.Name);
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task SetupProtectAllBlocksMutations()
    {
        ResetEvents();

        using var set = UseUniqueSet();

        LifecycleEntity.Events
            .Setup(ctx =>
            {
                ctx.ProtectAll();
                return ValueTask.CompletedTask;
            })
            .AfterUpsert(ctx =>
            {
                ctx.Current.Revision++;
                return ValueTask.CompletedTask;
            });

        var entity = new LifecycleEntity { Title = "Immutable" };

        var act = async () => await LifecycleEntity.UpsertAsync(entity);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*protected and cannot be mutated*");
    }

    [Fact]
    public async Task AllowMutationPermitsWhitelistedChanges()
    {
        ResetEvents();

        using var set = UseUniqueSet();

        LifecycleEntity.Events
            .Setup(ctx =>
            {
                ctx.Protect(nameof(LifecycleEntity.Title));
                ctx.AllowMutation(nameof(LifecycleEntity.Id));
                ctx.AllowMutation(nameof(LifecycleEntity.Title));
                return ValueTask.CompletedTask;
            })
            .BeforeUpsert(ctx =>
            {
                ctx.Current.Title = "mutated";
                return new ValueTask<EntityEventResult>(ctx.Proceed());
            });

        var entity = new LifecycleEntity { Title = "original" };
        var saved = await LifecycleEntity.UpsertAsync(entity);

        saved.Title.Should().Be("mutated");

        var persisted = await LifecycleEntity.Get(saved.Id, set.Name);
        persisted?.Title.Should().Be("mutated");
    }

    [Fact]
    public async Task PriorSnapshotExposesPreviousVersion()
    {
        ResetEvents();

        using var set = UseUniqueSet();

        var priorTitles = new List<string?>();

        LifecycleEntity.Events
            .BeforeUpsert(async ctx =>
            {
                var prior = await ctx.Prior.Get();
                priorTitles.Add(prior?.Title);
                return ctx.Proceed();
            });

        var entity = new LifecycleEntity { Title = "v1" };
        var saved = await LifecycleEntity.UpsertAsync(entity);

        var updated = new LifecycleEntity
        {
            Id = saved.Id,
            Title = "v2",
            Revision = saved.Revision,
            IsPublished = saved.IsPublished
        };

        await LifecycleEntity.UpsertAsync(updated);

        priorTitles.Should().BeEquivalentTo(new[] { null, "v1" }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task AtomicBatchCancellationPreventsPartialRemoves()
    {
        ResetEvents();

        using var set = UseUniqueSet();

        LifecycleEntity.Events
            .BeforeRemove(ctx =>
            {
                if (ctx.Current.Title == "Block")
                {
                    ctx.Operation.RequireAtomic();
                    return new ValueTask<EntityEventResult>(ctx.Cancel("blocked"));
                }

                return new ValueTask<EntityEventResult>(ctx.Proceed());
            });

        var keep = await LifecycleEntity.UpsertAsync(new LifecycleEntity { Title = "Keep" });
        var block = await LifecycleEntity.UpsertAsync(new LifecycleEntity { Title = "Block" });

        var act = async () => await LifecycleEntity.Remove(new[] { keep.Id, block.Id });
        await act.Should().ThrowAsync<EntityEventBatchCancelledException>();

        var keepPersisted = await LifecycleEntity.Get(keep.Id, set.Name);
        keepPersisted.Should().NotBeNull();
        keepPersisted!.Title.Should().Be("Keep");

        var blockPersisted = await LifecycleEntity.Get(block.Id, set.Name);
        blockPersisted.Should().NotBeNull();
        blockPersisted!.Title.Should().Be("Block");
    }

    [Fact]
    public async Task LoadPipelineCanEnrichEntities()
    {
        ResetEvents();

        using var set = UseUniqueSet();

        LifecycleEntity.Events
            .AfterLoad(ctx =>
            {
                ctx.Current.Revision += 10;
                return ValueTask.CompletedTask;
            });

        var saved = await LifecycleEntity.UpsertAsync(new LifecycleEntity { Title = "Load" });

        var originalRevision = saved.Revision;
        var loaded = await LifecycleEntity.Get(saved.Id, set.Name);
        loaded.Should().NotBeNull();
        loaded!.Revision.Should().Be(originalRevision + 10);
    }

    public sealed class LifecycleEntity : Entity<LifecycleEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Revision { get; set; }
        public bool IsPublished { get; set; }
    }
}

