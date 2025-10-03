using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Data.Connector.InMemory.Tests;

/// <summary>
/// Tests for multi-tenant partition isolation in InMemory adapter.
/// Validates that data is properly isolated across partitions.
/// </summary>
[Collection("Sequential")]
public sealed class PartitionIsolationTests : IDisposable
{
    private readonly TestScope _scope;

    public PartitionIsolationTests()
    {
        _scope = CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task Partition_SaveInDifferentPartitions_IsolatesData()
    {
        // Save entity in partition "tenant-1"
        using (EntityContext.With(partition: "tenant-1"))
        {
            await new PartitionEntity { Name = "Tenant1Data" }.Save();
        }

        // Save entity in partition "tenant-2"
        using (EntityContext.With(partition: "tenant-2"))
        {
            await new PartitionEntity { Name = "Tenant2Data" }.Save();
        }

        // Query partition "tenant-1"
        using (EntityContext.With(partition: "tenant-1"))
        {
            var entities = await PartitionEntity.All();
            entities.Should().HaveCount(1);
            entities[0].Name.Should().Be("Tenant1Data");
        }

        // Query partition "tenant-2"
        using (EntityContext.With(partition: "tenant-2"))
        {
            var entities = await PartitionEntity.All();
            entities.Should().HaveCount(1);
            entities[0].Name.Should().Be("Tenant2Data");
        }
    }

    [Fact]
    public async Task Partition_DefaultPartition_IsolatedFromNamed()
    {
        // Save in default partition
        await new PartitionEntity { Name = "DefaultData" }.Save();

        // Save in named partition
        using (EntityContext.With(partition: "named"))
        {
            await new PartitionEntity { Name = "NamedData" }.Save();
        }

        // Query default partition
        var defaultEntities = await PartitionEntity.All();
        defaultEntities.Should().HaveCount(1);
        defaultEntities[0].Name.Should().Be("DefaultData");

        // Query named partition
        using (EntityContext.With(partition: "named"))
        {
            var namedEntities = await PartitionEntity.All();
            namedEntities.Should().HaveCount(1);
            namedEntities[0].Name.Should().Be("NamedData");
        }
    }

    [Fact]
    public async Task Partition_GetById_RespectsPartitionContext()
    {
        string id;

        // Create entity in partition "tenant-1"
        using (EntityContext.With(partition: "tenant-1"))
        {
            var entity = new PartitionEntity { Name = "Tenant1Entity" };
            await entity.Save();
            id = entity.Id;
        }

        // Try to get from partition "tenant-2" (should fail)
        using (EntityContext.With(partition: "tenant-2"))
        {
            var loaded = await PartitionEntity.Get(id);
            loaded.Should().BeNull();  // Not in this partition
        }

        // Get from correct partition "tenant-1" (should succeed)
        using (EntityContext.With(partition: "tenant-1"))
        {
            var loaded = await PartitionEntity.Get(id);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Tenant1Entity");
        }
    }

    [Fact]
    public async Task Partition_Delete_OnlyAffectsSpecificPartition()
    {
        string id1, id2;

        // Create entity in partition "tenant-1"
        using (EntityContext.With(partition: "tenant-1"))
        {
            var entity = new PartitionEntity { Name = "Tenant1" };
            await entity.Save();
            id1 = entity.Id;
        }

        // Create entity with same type in partition "tenant-2"
        using (EntityContext.With(partition: "tenant-2"))
        {
            var entity = new PartitionEntity { Name = "Tenant2" };
            await entity.Save();
            id2 = entity.Id;
        }

        // Delete from partition "tenant-1"
        using (EntityContext.With(partition: "tenant-1"))
        {
            var deleted = await Data<PartitionEntity, string>.DeleteAsync(id1);
            deleted.Should().BeTrue();
        }

        // Verify partition "tenant-1" is empty
        using (EntityContext.With(partition: "tenant-1"))
        {
            var entities = await PartitionEntity.All();
            entities.Should().BeEmpty();
        }

        // Verify partition "tenant-2" still has data
        using (EntityContext.With(partition: "tenant-2"))
        {
            var entities = await PartitionEntity.All();
            entities.Should().HaveCount(1);
            entities[0].Id.Should().Be(id2);
        }
    }

    [Fact]
    public async Task Partition_NestedContexts_UsesMostRecent()
    {
        using (EntityContext.With(partition: "outer"))
        {
            await new PartitionEntity { Name = "Outer1" }.Save();

            using (EntityContext.With(partition: "inner"))
            {
                await new PartitionEntity { Name = "Inner1" }.Save();

                var innerEntities = await PartitionEntity.All();
                innerEntities.Should().HaveCount(1);
                innerEntities[0].Name.Should().Be("Inner1");
            }

            var outerEntities = await PartitionEntity.All();
            outerEntities.Should().HaveCount(1);
            outerEntities[0].Name.Should().Be("Outer1");
        }
    }

    [Fact]
    public async Task Partition_Query_OnlyReturnsPartitionData()
    {
        // Add data to multiple partitions
        using (EntityContext.With(partition: "tenant-1"))
        {
            await new PartitionEntity { Name = "Tenant1A", Value = 10 }.Save();
            await new PartitionEntity { Name = "Tenant1B", Value = 20 }.Save();
        }

        using (EntityContext.With(partition: "tenant-2"))
        {
            await new PartitionEntity { Name = "Tenant2A", Value = 15 }.Save();
            await new PartitionEntity { Name = "Tenant2B", Value = 25 }.Save();
        }

        // Query with LINQ in tenant-1
        using (EntityContext.With(partition: "tenant-1"))
        {
            var results = await PartitionEntity.Query(e => e.Value > 5);
            results.Should().HaveCount(2);
            results.Should().AllSatisfy(e => e.Name.Should().StartWith("Tenant1"));
        }

        // Query with LINQ in tenant-2
        using (EntityContext.With(partition: "tenant-2"))
        {
            var results = await PartitionEntity.Query(e => e.Value > 5);
            results.Should().HaveCount(2);
            results.Should().AllSatisfy(e => e.Name.Should().StartWith("Tenant2"));
        }
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

    // ==================== Test Entity ====================

    public sealed class PartitionEntity : Entity<PartitionEntity>
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}
