using AwesomeAssertions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Json;
using Koan.Data.Connector.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// DATA-0104 capability honesty. Each real adapter's <em>announced</em> <see cref="StorageNamingCapability"/>,
/// run through the full <see cref="StorageNameGenerator"/> pipeline, must yield a legal (arity-marker-free) and
/// per-closure-distinct name for a closed generic entity. The resolver matrix covers the styles in the abstract;
/// this covers the adapters' real announcements (the in-proc ones available here — SQLite + JSON). The
/// container-backed adapters (Mongo/Postgres/SqlServer/Redis/Couchbase/vector) extend this oracle in their own
/// surface suites per the DATA-0104 conformance contract.
/// </summary>
public sealed class AdapterCapabilityGenericNameSpec
{
    private sealed class Todo { }
    private sealed class Order { }
    private sealed class Wrap<T> { }

    private static StorageNamingCapability Cap(string adapter)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        if (adapter == "sqlite") services.Configure<SqliteOptions>(_ => { });
        else services.Configure<JsonDataOptions>(_ => { });
        using var sp = services.BuildServiceProvider();
        return adapter == "sqlite"
            ? new SqliteAdapterFactory().GetNamingCapability(sp)
            : new JsonAdapterFactory().GetNamingCapability(sp);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("json")]
    public void Announced_capability_yields_legal_distinct_generic_name(string adapter)
    {
        var cap = Cap(adapter);

        var todo = StorageNameGenerator.Generate(typeof(Wrap<Todo>), null, cap);
        var order = StorageNameGenerator.Generate(typeof(Wrap<Order>), null, cap);

        todo.Should().NotContain("`", $"{adapter} must not leak the CLR arity marker into a physical identifier");
        todo.Should().NotBe(order, $"{adapter} must keep distinct closures in distinct physical stores");
    }
}
