using System;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// <see cref="AdapterNaming.GetOrCompute{TEntity,TKey}"/> consumes the host's compiled provider catalog rather
/// than re-walking or re-probing adapter factories. The selected factory remains host-owned while naming stays
/// ambient-aware (adapter routing) and partition-aware.
/// </summary>
public class AdapterNamingSpec
{
    [DataAdapter("fake")]
    public class FakeRouted : Entity<FakeRouted>
    {
        public string Name { get; set; } = "";
    }

    private sealed class CountingFakeFactory : IDataAdapterFactory
    {
        private readonly string _name;
        public CountingFakeFactory(string name) => _name = name;

        public string Provider => "fake";
        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => throw new NotImplementedException();
        public string ResolveStorage(Type entityType, string? partition, IServiceProvider services) => _name;
        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull => throw new NotImplementedException();
    }

    private static ServiceProvider Build(IDataAdapterFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddKoanDataCore();
        services.AddSingleton(factory);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void GetOrCompute_reuses_the_compiled_catalog_without_reprobing_factories()
    {
        var fake = new CountingFakeFactory("fake_name");
        using var sp = Build(fake);

        var n1 = AdapterNaming.GetOrCompute<FakeRouted, string>(sp);
        var n2 = AdapterNaming.GetOrCompute<FakeRouted, string>(sp);

        n1.Should().Be("fake_name");
        n2.Should().Be("fake_name");
    }

    [Fact]
    public void GetOrCompute_does_not_leak_the_factory_across_service_providers()
    {
        // A naive static (provider-string-only) cache would return "alpha" for the second provider — a leak.
        using (var spA = Build(new CountingFakeFactory("alpha")))
        {
            AdapterNaming.GetOrCompute<FakeRouted, string>(spA).Should().Be("alpha");
        }
        using (var spB = Build(new CountingFakeFactory("beta")))
        {
            AdapterNaming.GetOrCompute<FakeRouted, string>(spB).Should().Be("beta");
        }
    }

    [Fact]
    public void GetOrCompute_stays_partition_aware()
    {
        // The factory cache must not freeze the partition: each partition still flows to ResolveStorage.
        var fake = new RecordingFactory();
        using var sp = Build(fake);

        AdapterNaming.GetOrCompute<FakeRouted, string>(sp);
        using (EntityContext.Partition("p1")) AdapterNaming.GetOrCompute<FakeRouted, string>(sp);

        fake.SeenPartitions.Should().Contain(new string?[] { null, "p1" });
    }

    private sealed class RecordingFactory : IDataAdapterFactory
    {
        public readonly System.Collections.Generic.List<string?> SeenPartitions = new();
        public string Provider => "fake";
        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => throw new NotImplementedException();
        public string ResolveStorage(Type entityType, string? partition, IServiceProvider services)
        {
            SeenPartitions.Add(partition);
            return "n";
        }
        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull => throw new NotImplementedException();
    }
}
