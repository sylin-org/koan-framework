using Koan.Core.Diagnostics;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Relationships;

[Collection(nameof(RelationshipPointwiseSpec))]
[CollectionDefinition(nameof(RelationshipPointwiseSpec), DisableParallelization = true)]
public sealed class RelationshipPointwiseSpec : IAsyncLifetime
{
    private DataCoreRuntimeFixture _fixture = null!;

    public async ValueTask InitializeAsync()
        => _fixture = await DataCoreRuntimeFixture.CreateAsync();

    public async ValueTask DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public async Task Relatives_preserves_finite_source_order_and_multiplicity_across_batched_edges()
    {
        _fixture.BindHost();
        var first = new MemoryParent { Name = "first" };
        var second = new MemoryParent { Name = "second" };
        await first.Save();
        await second.Save();
        var firstChild = new MemoryChild { ParentId = first.Id, Name = "first-child" };
        var secondChild = new MemoryChild { ParentId = second.Id, Name = "second-child" };
        await firstChild.Save();
        await secondChild.Save();

        var scalarGraph = await first.Relatives();
        Children(scalarGraph).Should().ContainSingle().Which.Name.Should().Be("first-child");

        var parentGraphs = await new[] { second, first, second }.Relatives();
        parentGraphs.Select(static graph => graph.Entity.Id).Should().Equal(second.Id, first.Id, second.Id);
        Children(parentGraphs[0]).Should().ContainSingle().Which.Name.Should().Be("second-child");
        Children(parentGraphs[1]).Should().ContainSingle().Which.Name.Should().Be("first-child");
        Children(parentGraphs[2]).Should().ContainSingle().Which.Name.Should().Be("second-child");

        var childGraphs = await new[] { secondChild, firstChild, secondChild }.Relatives();
        childGraphs.Select(static graph => graph.Entity.Id).Should().Equal(secondChild.Id, firstChild.Id, secondChild.Id);
        childGraphs.Select(graph => ((MemoryParent)graph.Parents[nameof(MemoryChild.ParentId)]!).Name)
            .Should().Equal("second", "first", "second");
    }

    [Fact]
    public async Task Relatives_infers_custom_keys_and_keeps_async_sources_lazy_and_single_pass()
    {
        _fixture.BindHost();
        var enumerations = 0;

        async IAsyncEnumerable<CustomKeyEntity> Source()
        {
            enumerations++;
            yield return new CustomKeyEntity { Id = 2, Name = "second" };
            yield return new CustomKeyEntity { Id = 1, Name = "first" };
            await Task.CompletedTask;
        }

        var enriched = Source().Relatives();
        enumerations.Should().Be(0);

        var graphs = new List<RelationshipGraph<CustomKeyEntity>>();
        await foreach (var graph in enriched)
        {
            graphs.Add(graph);
        }

        enumerations.Should().Be(1);
        graphs.Select(static graph => graph.Entity.Id).Should().Equal(2, 1);
    }

    [Fact]
    public async Task Relatives_preserves_strict_and_explicitly_bounded_child_negotiation_and_facts()
    {
        _fixture.BindHost();
        var parent = new JsonParent();
        await parent.Save();
        await new JsonChild { ParentId = parent.Id, Name = "bounded" }.Save();

        await Assert.ThrowsAsync<RelationshipQueryRejectedException>(() =>
            new[] { parent }.Relatives());

        var graphs = await new[] { parent }.Relatives(RelationshipQueryPolicy.Bounded(10, 5));

        Children(graphs.Single()).Should().ContainSingle().Which.Name.Should().Be("bounded");
        Facts.Current.Facts.Should().Contain(fact =>
            fact.Code == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Codes.RelationshipExecution
            && fact.State == KoanFactState.Selected
            && fact.ReasonCode == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Reasons.BoundedScan);
    }

    [Fact]
    public async Task Relatives_rejects_cross_key_edges_with_a_corrective_contract_error()
    {
        _fixture.BindHost();

        var action = () => new CrossKeyParent().Relatives();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(action);
        failure.Message.Should().Contain("same key type on both sides");
    }

    private IKoanRuntimeFacts Facts
        => _fixture.Services.GetRequiredService<IKoanRuntimeFacts>();

    private static IReadOnlyList<MemoryChild> Children(RelationshipGraph<MemoryParent> graph)
        => graph.Children[nameof(MemoryChild)][nameof(MemoryChild.ParentId)].Cast<MemoryChild>().ToArray();

    private static IReadOnlyList<JsonChild> Children(RelationshipGraph<JsonParent> graph)
        => graph.Children[nameof(JsonChild)][nameof(JsonChild.ParentId)].Cast<JsonChild>().ToArray();

    [DataAdapter("inmemory")]
    private sealed class MemoryParent : Entity<MemoryParent>
    {
        public string Name { get; set; } = "";
    }

    [DataAdapter("inmemory")]
    private sealed class MemoryChild : Entity<MemoryChild>
    {
        [Parent(typeof(MemoryParent))]
        public string ParentId { get; set; } = "";

        public string Name { get; set; } = "";
    }

    [DataAdapter("json")]
    private sealed class JsonParent : Entity<JsonParent>;

    [DataAdapter("json")]
    private sealed class JsonChild : Entity<JsonChild>
    {
        [Parent(typeof(JsonParent))]
        public string ParentId { get; set; } = "";

        public string Name { get; set; } = "";
    }

    private sealed class CustomKeyEntity : Entity<CustomKeyEntity, int>
    {
        public string Name { get; set; } = "";
    }

    [DataAdapter("inmemory")]
    private sealed class CrossKeyParent : Entity<CrossKeyParent>;

    [DataAdapter("inmemory")]
    private sealed class CrossKeyChild : Entity<CrossKeyChild, int>
    {
        [Parent(typeof(CrossKeyParent))]
        public string ParentId { get; set; } = "";
    }
}
