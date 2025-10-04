using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Data.Connector.InMemory.Tests;

/// <summary>
/// Comprehensive tests for InMemory data adapter.
/// Validates DATA-0081: InMemory adapter implementation.
/// </summary>
[Collection("Sequential")]
public sealed class InMemoryAdapterTests : IDisposable
{
    private readonly TestScope _scope;

    public InMemoryAdapterTests()
    {
        _scope = CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    // ==================== Basic CRUD Tests ====================

    [Fact]
    public async Task Get_ExistingEntity_ReturnsEntity()
    {
        var entity = new TestEntity { Name = "Test1" };
        await entity.Save();

        var loaded = await TestEntity.Get(entity.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(entity.Id);
        loaded.Name.Should().Be("Test1");
    }

    [Fact]
    public async Task Get_NonExistentEntity_ReturnsNull()
    {
        var loaded = await TestEntity.Get("non-existent-id");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Save_NewEntity_AssignsGuidV7Id()
    {
        var entity = new TestEntity { Name = "Test" };
        // Entity<T> auto-generates GUID v7 on construction
        entity.Id.Should().NotBeNullOrEmpty();

        var idBeforeSave = entity.Id;
        await entity.Save();

        // ID should remain the same after save
        entity.Id.Should().Be(idBeforeSave);
        // GUID v7 should be a valid GUID format
        Guid.TryParse(entity.Id, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Save_UpdateExisting_PreservesId()
    {
        var entity = new TestEntity { Name = "Original" };
        await entity.Save();
        var originalId = entity.Id;

        entity.Name = "Updated";
        await entity.Save();

        entity.Id.Should().Be(originalId);
        var loaded = await TestEntity.Get(originalId);
        loaded!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task Delete_ExistingEntity_RemovesFromStore()
    {
        var entity = new TestEntity { Name = "ToDelete" };
        await entity.Save();
        var id = entity.Id;

        var deleted = await entity.Delete();

        deleted.Should().BeTrue();
        var loaded = await TestEntity.Get(id);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistentEntity_ReturnsFalse()
    {
        var result = await Data<TestEntity, string>.DeleteAsync("non-existent-id");

        result.Should().BeFalse();
    }

    // ==================== LINQ Query Tests ====================

    [Fact]
    public async Task Query_WithLinqPredicate_ReturnsMatchingEntities()
    {
        await new TestEntity { Name = "Alpha" }.Save();
        await new TestEntity { Name = "Beta" }.Save();
        await new TestEntity { Name = "Gamma" }.Save();

        var results = await TestEntity.Query(e => e.Name.StartsWith("B"));

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Beta");
    }

    [Fact]
    public async Task Query_ComplexLinq_WorksCorrectly()
    {
        await new TestEntity { Name = "Test1", Value = 10 }.Save();
        await new TestEntity { Name = "Test2", Value = 20 }.Save();
        await new TestEntity { Name = "Test3", Value = 30 }.Save();

        var results = await TestEntity.Query(e => e.Value > 15 && e.Name.Contains("Test"));

        results.Should().HaveCount(2);
        results.Should().Contain(e => e.Name == "Test2");
        results.Should().Contain(e => e.Name == "Test3");
    }

    [Fact]
    public async Task Count_WithPredicate_ReturnsCorrectCount()
    {
        await new TestEntity { Name = "Match1" }.Save();
        await new TestEntity { Name = "Match2" }.Save();
        await new TestEntity { Name = "NoMatch" }.Save();

        var count = await TestEntity.Count.Where(e => e.Name.StartsWith("Match"));

        count.Should().Be(2);
    }

    [Fact]
    public async Task All_ReturnsAllEntities()
    {
        await new TestEntity { Name = "Entity1" }.Save();
        await new TestEntity { Name = "Entity2" }.Save();
        await new TestEntity { Name = "Entity3" }.Save();

        var all = await TestEntity.All();

        all.Should().HaveCount(3);
    }

    // ==================== Batch Operations Tests ====================

    [Fact]
    public async Task Batch_AddMultipleEntities_InsertsAll()
    {
        var entities = new[]
        {
            new TestEntity { Name = "Batch1" },
            new TestEntity { Name = "Batch2" },
            new TestEntity { Name = "Batch3" }
        };

        var count = await entities.Save();

        count.Should().Be(3);
        var all = await TestEntity.All();
        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task Batch_UpdateMultiple_UpdatesAll()
    {
        var e1 = new TestEntity { Name = "Original1" };
        var e2 = new TestEntity { Name = "Original2" };
        await new[] { e1, e2 }.Save();

        e1.Name = "Updated1";
        e2.Name = "Updated2";
        await new[] { e1, e2 }.Save();

        var loaded1 = await TestEntity.Get(e1.Id);
        var loaded2 = await TestEntity.Get(e2.Id);
        loaded1!.Name.Should().Be("Updated1");
        loaded2!.Name.Should().Be("Updated2");
    }

    [Fact]
    public async Task Batch_DeleteMultiple_RemovesAll()
    {
        var e1 = new TestEntity { Name = "Delete1" };
        var e2 = new TestEntity { Name = "Delete2" };
        await new[] { e1, e2 }.Save();

        var deleted = await Data<TestEntity, string>.DeleteManyAsync(new[] { e1.Id, e2.Id });

        deleted.Should().Be(2);
        var all = await TestEntity.All();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBatch_MixedOperations_ExecutesAtomically()
    {
        var existing = new TestEntity { Name = "Existing" };
        await existing.Save();

        var batch = Data<TestEntity, string>.Batch();
        batch.Add(new TestEntity { Name = "New1" });
        batch.Add(new TestEntity { Name = "New2" });
        batch.Update(existing.Id, e => e.Name = "Modified");

        var result = await batch.SaveAsync();

        result.Added.Should().Be(2);
        result.Updated.Should().Be(1);

        var loaded = await TestEntity.Get(existing.Id);
        loaded!.Name.Should().Be("Modified");
    }

    // ==================== [Timestamp] Integration Tests ====================

    [Fact]
    public async Task TimestampAttribute_OnSave_AutoUpdates()
    {
        var entity = new TimestampedEntity { Name = "Test" };
        var beforeSave = DateTimeOffset.UtcNow.AddMilliseconds(-100);

        await entity.Save();

        var afterSave = DateTimeOffset.UtcNow.AddMilliseconds(100);
        entity.LastModified.Should().BeAfter(beforeSave);
        entity.LastModified.Should().BeBefore(afterSave);
    }

    [Fact]
    public async Task TimestampAttribute_MultipleUpdates_TimestampIncreases()
    {
        var entity = new TimestampedEntity { Name = "Original" };
        await entity.Save();
        var timestamp1 = entity.LastModified;

        await Task.Delay(50);
        entity.Name = "Updated";
        await entity.Save();
        var timestamp2 = entity.LastModified;

        timestamp2.Should().BeAfter(timestamp1);
    }

    [Fact]
    public async Task TimestampAttribute_BulkUpsert_UpdatesAll()
    {
        var entities = new[]
        {
            new TimestampedEntity { Name = "Entity1" },
            new TimestampedEntity { Name = "Entity2" }
        };

        var beforeSave = DateTimeOffset.UtcNow.AddMilliseconds(-100);
        await entities.Save();
        var afterSave = DateTimeOffset.UtcNow.AddMilliseconds(100);

        foreach (var entity in entities)
        {
            entity.LastModified.Should().BeAfter(beforeSave);
            entity.LastModified.Should().BeBefore(afterSave);
        }
    }

    // ==================== Capability Reporting Tests ====================

    [Fact]
    public void QueryCapabilities_ReportsLinqSupport()
    {
        var caps = Data<TestEntity, string>.QueryCaps;

        caps.Capabilities.Should().HaveFlag(QueryCapabilities.Linq);
    }

    [Fact]
    public void WriteCapabilities_ReportsAllSupport()
    {
        var caps = Data<TestEntity, string>.WriteCaps;

        caps.Writes.Should().HaveFlag(WriteCapabilities.BulkUpsert);
        caps.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
        caps.Writes.Should().HaveFlag(WriteCapabilities.AtomicBatch);
    }

    // ==================== Helper Methods ====================

    private static TestScope CreateScope()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        services.AddKoan();

        var provider = services.BuildServiceProvider();
        return new TestScope(provider);
    }

    private sealed class TestScope : IDisposable
    {
        private readonly IServiceProvider? _previousAppHost;
        public ServiceProvider Provider { get; }

        public TestScope(ServiceProvider provider)
        {
            Provider = provider;
            _previousAppHost = AppHost.Current;
            AppHost.Current = provider;
        }

        public void Dispose()
        {
            AppHost.Current = _previousAppHost;
            Provider.Dispose();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Koan.Data.Connector.InMemory.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ==================== Test Entities ====================

    public sealed class TestEntity : Entity<TestEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    public sealed class TimestampedEntity : Entity<TimestampedEntity>
    {
        public string Name { get; set; } = "";

        [Timestamp]
        public DateTimeOffset LastModified { get; set; }
    }
}
