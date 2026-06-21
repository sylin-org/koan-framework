using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Naming;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// DATA-0104 — the recursive generic-entity storage-name grammar at the single chokepoint
/// (<see cref="StorageNameResolver.Resolve"/>). A closed generic over its type arguments — e.g. the AI pillar's
/// <c>EmbeddingState&lt;Todo&gt;</c> — must anchor on its arguments and append the generic's bare simple name:
/// <c>"-"</c> joins the spine (single-arg wrapper), <c>"--"</c> the branch (sibling args), the entity is the
/// leftmost anchor, and casing is uniform per the adapter's announced convention. Before this, the resolver fell
/// into the namespace branches and produced <c>Foo`1</c> (arg dropped → all closures collide on one physical
/// store → silent cross-entity corruption) or the assembly-qualified <c>Foo`1[[…]]</c> (illegal / version-fragile).
///
/// The resolver is type-agnostic (it inspects <see cref="System.Type"/> only), so plain classes exercise the
/// identical code path; the real <c>Entity&lt;EmbeddingState&lt;T&gt;&gt;</c> round-trip is the ARCH-0079 repro.
/// </summary>
public sealed class StorageNameResolverGenericSpec
{
    private sealed class Todo { }
    private sealed class Order { }

    [StorageName("custom_todo")]
    private sealed class NamedTodo { }

    private sealed class Wrap<T> { }          // single-arg wrapper (the EmbeddingState<T> shape)
    private sealed class Inner<T> { }         // for nesting
    private sealed class Pair<TA, TB> { }     // two-arg branch

    private static string Resolve(System.Type t, StorageNamingStyle style, NameCasing casing)
        => StorageNameResolver.Resolve(t, new StorageNameResolver.Convention(style, "_", casing));

    // ---- Grammar shape (EntityType style, AsIs casing → exact strings) ----

    [Fact]
    public void Single_arg_anchors_on_entity_and_appends_wrapper()
        => Resolve(typeof(Wrap<Todo>), StorageNamingStyle.EntityType, NameCasing.AsIs)
            .Should().Be("Todo-Wrap");

    [Fact]
    public void Nested_generic_composes_inside_out_via_recursion()
        => Resolve(typeof(Wrap<Inner<Todo>>), StorageNamingStyle.EntityType, NameCasing.AsIs)
            .Should().Be("Todo-Inner-Wrap");

    [Fact]
    public void Multi_arg_branch_uses_double_dash_between_siblings()
        => Resolve(typeof(Pair<Todo, Order>), StorageNamingStyle.EntityType, NameCasing.AsIs)
            .Should().Be("Todo--Order-Pair");

    // ---- Casing is uniform per the convention (DATA-0104 fork-1 decision) ----

    [Theory]
    [InlineData(typeof(Wrap<Todo>), "todo-wrap")]
    [InlineData(typeof(Wrap<Inner<Todo>>), "todo-inner-wrap")]
    [InlineData(typeof(Pair<Todo, Order>), "todo--order-pair")]
    public void Lower_casing_applies_to_the_whole_composed_name(System.Type t, string expected)
        => Resolve(t, StorageNamingStyle.EntityType, NameCasing.Lower).Should().Be(expected);

    // ---- The recursion honors the inner entity's own explicit name ----

    [Fact]
    public void Recursion_honors_inner_entity_storage_name_attribute()
        => Resolve(typeof(Wrap<NamedTodo>), StorageNamingStyle.EntityType, NameCasing.AsIs)
            .Should().Be("custom_todo-Wrap");

    // ---- The corruption-killer: distinct closures never collide, on EVERY style ----

    [Theory]
    [InlineData(StorageNamingStyle.EntityType)]
    [InlineData(StorageNamingStyle.FullNamespace)]
    [InlineData(StorageNamingStyle.HashedNamespace)]
    public void Distinct_closures_resolve_to_distinct_names(StorageNamingStyle style)
    {
        var todo = Resolve(typeof(Wrap<Todo>), style, NameCasing.AsIs);
        var order = Resolve(typeof(Wrap<Order>), style, NameCasing.AsIs);
        todo.Should().NotBe(order,
            "every EmbeddingState<T> sharing one physical store is the silent cross-entity corruption this fixes");
    }

    // ---- Legality: the CLR arity marker never leaks into an identifier ----

    [Theory]
    [InlineData(StorageNamingStyle.EntityType)]
    [InlineData(StorageNamingStyle.FullNamespace)]
    [InlineData(StorageNamingStyle.HashedNamespace)]
    public void No_backtick_arity_marker_in_any_style(StorageNamingStyle style)
    {
        foreach (var t in new[] { typeof(Wrap<Todo>), typeof(Wrap<Inner<Todo>>), typeof(Pair<Todo, Order>) })
            Resolve(t, style, NameCasing.AsIs).Should().NotContain("`",
                "the assembly mangled name (Foo`1[[…]]) must never reach a physical identifier");
    }

    // ---- The grammar base composes with the downstream partition append unchanged ----

    [Fact]
    public void Generic_base_composes_with_partition_suffix()
    {
        var cap = new StorageNamingCapability
        {
            Style = StorageNamingStyle.EntityType,
            Separator = "_",
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
        };

        StorageNameGenerator.Generate(typeof(Wrap<Todo>), null, cap).Should().Be("Todo-Wrap");
        StorageNameGenerator.Generate(typeof(Wrap<Todo>), "backup", cap).Should().StartWith("Todo-Wrap#",
            "partition composes downstream of the base name, so the generic grammar carries through untouched");
    }
}
