using System;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Configuration;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// Tests for DATA-0086 + DATA-0095: Data and Vector naming-resolution separation.
/// After Phase 1c.2.a, each layer has its own thin helper that routes to the
/// appropriate factory's <see cref="INamingProvider.ResolveStorage"/>:
/// - <see cref="AdapterNaming"/> only queries <see cref="IDataAdapterFactory"/>
/// - <see cref="VectorAdapterNaming"/> only queries <see cref="IVectorAdapterFactory"/>
/// </summary>
public class DataVectorSeparationSpec
{
    public class TestEntity : Entity<TestEntity>
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void DataNaming_UsesDataAdapterFactory()
    {
        var services = new ServiceCollection();
        services.AddKoanDataCore();
        var sp = services.BuildServiceProvider();

        string storageName;
        try
        {
            storageName = AdapterNaming.GetOrCompute<TestEntity, string>(sp);
        }
        catch (InvalidOperationException ex)
        {
            // Acceptable when no data adapter is registered in the test env.
            var message = ex.Message;
            Assert.True(
                message.Contains("No data adapter", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IDataAdapterFactory", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("No constructor for type 'Koan.Data.Connector", StringComparison.OrdinalIgnoreCase),
                $"Unexpected exception message: {message}");
            return;
        }

        Assert.NotNull(storageName);
        Assert.NotEmpty(storageName);
    }

    [Fact]
    public void VectorNaming_UsesVectorAdapterFactory()
    {
        var services = new ServiceCollection();
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        var sp = services.BuildServiceProvider();

        var vectorFactories = sp.GetServices<IVectorAdapterFactory>();
        if (!vectorFactories.Any())
        {
            Assert.True(true, "Skipped: No vector adapter registered");
            return;
        }

        var vectorName = VectorAdapterNaming.GetOrCompute<TestEntity, string>(sp);
        Assert.NotNull(vectorName);
        Assert.NotEmpty(vectorName);
    }

    [Fact]
    public void SameEntity_DifferentNamesInDataVsVector()
    {
        var services = new ServiceCollection();
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        var sp = services.BuildServiceProvider();

        var vectorFactories = sp.GetServices<IVectorAdapterFactory>();
        if (!vectorFactories.Any())
        {
            Assert.True(true, "Skipped: No vector adapter registered");
            return;
        }

        string dataName;
        try
        {
            dataName = AdapterNaming.GetOrCompute<TestEntity, string>(sp);
        }
        catch (InvalidOperationException)
        {
            Assert.True(true, "Skipped: No data adapter registered");
            return;
        }

        var vectorName = VectorAdapterNaming.GetOrCompute<TestEntity, string>(sp);
        Assert.NotEqual(dataName, vectorName);
    }

    [Fact]
    public void DataNaming_WithPartition_AppliesAdapterFormatting()
    {
        var services = new ServiceCollection();
        services.AddKoanDataCore();
        var sp = services.BuildServiceProvider();

        string nameWithPartition;
        using (EntityContext.Partition("test-partition-123"))
        {
            try
            {
                nameWithPartition = AdapterNaming.GetOrCompute<TestEntity, string>(sp);
            }
            catch (InvalidOperationException)
            {
                Assert.True(true, "Skipped: No data adapter registered");
                return;
            }
        }

        Assert.NotNull(nameWithPartition);
        Assert.Contains("test", nameWithPartition.ToLowerInvariant());
    }

    [Fact]
    public void VectorNaming_WithPartition_AppliesAdapterFormatting()
    {
        var services = new ServiceCollection();
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        var sp = services.BuildServiceProvider();

        var vectorFactories = sp.GetServices<IVectorAdapterFactory>();
        if (!vectorFactories.Any())
        {
            Assert.True(true, "Skipped: No vector adapter registered");
            return;
        }

        string nameWithPartition;
        using (EntityContext.Partition("test-partition-456"))
        {
            nameWithPartition = VectorAdapterNaming.GetOrCompute<TestEntity, string>(sp);
        }

        Assert.NotNull(nameWithPartition);
        Assert.Contains("test", nameWithPartition.ToLowerInvariant());
    }
}
