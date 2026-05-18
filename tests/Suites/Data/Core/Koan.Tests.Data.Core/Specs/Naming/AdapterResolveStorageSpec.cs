using System;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.InMemory;
using Koan.Data.Connector.Json;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// Per-factory specs for <see cref="INamingProvider.ResolveStorage"/> introduced in DATA-0095
/// Phase 1c.2.a. The matrix exercises the happy path; these tests exercise the contract:
/// - Null / whitespace / empty partition all produce the same un-suffixed name
/// - GUID partitions get adapter-specific normalization (Sqlite "N" format)
/// - Named partitions go through adapter sanitization
/// - The per-instance cache returns the same instance on repeated calls
/// - Distinct (Type, partition) keys produce distinct entries
/// </summary>
public class AdapterResolveStorageSpec
{
    public class Widget : Koan.Data.Core.Model.Entity<Widget>
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void InMemory_nullPartition_returnsBaseName()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        factory.ResolveStorage(typeof(Widget), partition: null, sp).Should().Be("Widget");
    }

    [Fact]
    public void InMemory_whitespacePartition_treatedAsNull()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        factory.ResolveStorage(typeof(Widget), partition: "   ", sp).Should().Be("Widget");
        factory.ResolveStorage(typeof(Widget), partition: "", sp).Should().Be("Widget");
    }

    [Fact]
    public void InMemory_namedPartition_appendedAfterSeparator()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        factory.ResolveStorage(typeof(Widget), partition: "alpha", sp).Should().Be("Widget#alpha");
    }

    [Fact]
    public void InMemory_partitionWhitespaceTrimmed()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        factory.ResolveStorage(typeof(Widget), partition: "  alpha  ", sp).Should().Be("Widget#alpha");
    }

    [Fact]
    public void Json_namedPartition_appendedAfterSeparator()
    {
        var factory = new JsonAdapterFactory();
        var sp = BuildSpWithOptions(s => s.Configure<JsonDataOptions>(_ => { }));

        factory.ResolveStorage(typeof(Widget), partition: "tenant-7", sp).Should().Be("Widget#tenant-7");
    }

    [Fact]
    public void Sqlite_guidPartition_normalizedToNFormat()
    {
        var factory = new SqliteAdapterFactory();
        var sp = BuildSpWithSqlite();

        var guid = new Guid("019a5aff-79cb-7815-8dae-3700a698f840");
        var name = factory.ResolveStorage(typeof(Widget), guid.ToString("D"), sp);

        // SQLite normalizes GUIDs to lowercase, no hyphens.
        name.Should().EndWith("#019a5aff79cb78158dae3700a698f840");
    }

    [Fact]
    public void Sqlite_namedPartition_sanitizedForTableName()
    {
        var factory = new SqliteAdapterFactory();
        var sp = BuildSpWithSqlite();

        // SQLite SanitizeForSqlite keeps letters/digits/hyphen/dot/underscore.
        // Pipes, spaces, and slashes get replaced with underscore.
        var name = factory.ResolveStorage(typeof(Widget), "alpha/beta gamma|delta", sp);
        name.Should().EndWith("#alpha_beta_gamma_delta");
    }

    [Fact]
    public void Cache_returnsSameInstanceOnRepeatedCalls()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        var first = factory.ResolveStorage(typeof(Widget), "alpha", sp);
        var second = factory.ResolveStorage(typeof(Widget), "alpha", sp);

        // ConcurrentDictionary returns the cached instance — string equality plus reference identity.
        ReferenceEquals(first, second).Should().BeTrue(
            "the per-factory cache should not recompose the name on repeated calls");
    }

    [Fact]
    public void Cache_distinguishesByPartitionKey()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        var alpha = factory.ResolveStorage(typeof(Widget), "alpha", sp);
        var beta = factory.ResolveStorage(typeof(Widget), "beta", sp);
        var none = factory.ResolveStorage(typeof(Widget), null, sp);

        alpha.Should().NotBe(beta);
        alpha.Should().NotBe(none);
        beta.Should().NotBe(none);
    }

    [Fact]
    public void Cache_distinguishesByEntityType()
    {
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        factory.ResolveStorage(typeof(Widget), "alpha", sp).Should().Be("Widget#alpha");
        factory.ResolveStorage(typeof(OtherEntity), "alpha", sp).Should().Be("OtherEntity#alpha");
    }

    [Fact]
    public void Cache_treatsWhitespacePartitions_asSameKey()
    {
        // Per the implementation, "  ", "", and null all canonicalize to null in the cache key.
        var factory = new InMemoryAdapterFactory();
        var sp = BuildEmptySp();

        var fromNull = factory.ResolveStorage(typeof(Widget), null, sp);
        var fromEmpty = factory.ResolveStorage(typeof(Widget), "", sp);
        var fromWs = factory.ResolveStorage(typeof(Widget), "   ", sp);

        ReferenceEquals(fromNull, fromEmpty).Should().BeTrue();
        ReferenceEquals(fromNull, fromWs).Should().BeTrue();
    }

    public class OtherEntity : Koan.Data.Core.Model.Entity<OtherEntity>
    {
        public string Label { get; set; } = "";
    }

    private static IServiceProvider BuildEmptySp()
    {
        return new ServiceCollection().BuildServiceProvider();
    }

    private static IServiceProvider BuildSpWithOptions(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildSpWithSqlite()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<SqliteOptions>(_ => { });
        return services.BuildServiceProvider();
    }
}
