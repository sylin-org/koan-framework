using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Connector.InMemory.Tests.Support;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Sorting;

/// <summary>
/// Integration spec for sort pushdown contract (DATA-0092 + DATA-0093) against a real Koan runtime.
/// Goes through AddKoan() reflective discovery and exercises the orchestrator + InMemory adapter
/// end-to-end per ARCH-0079 canon.
/// </summary>
public sealed class InMemorySortingSpec
{
    private readonly ITestOutputHelper _output;

    public InMemorySortingSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public Task Sort_string_grammar_pushes_down_to_inmemory_adapter()
        => TestPipeline.For<InMemorySortingSpec>(_output, nameof(Sort_string_grammar_pushes_down_to_inmemory_adapter))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<Widget, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await SeedAsync();

                // Sort ascending
                var asc = await Widget.All(new DataQueryOptions().WithSort<Widget>("Name"));
                asc.Select(w => w.Name).Should().Equal("Alpha", "Bravo", "Charlie");

                // Sort descending
                var desc = await Widget.All(new DataQueryOptions().WithSort<Widget>("-Name"));
                desc.Select(w => w.Name).Should().Equal("Charlie", "Bravo", "Alpha");

                // Multi-field
                await Widget.Upsert(new Widget { Id = "x1", Name = "Bravo", Priority = 1 });
                await Widget.Upsert(new Widget { Id = "x2", Name = "Bravo", Priority = 5 });
                var multi = await Widget.All(new DataQueryOptions().WithSort<Widget>("Name,-Priority"));
                var bravo = multi.Where(w => w.Name == "Bravo").ToList();
                bravo[0].Priority.Should().BeGreaterThan(bravo[1].Priority);
            })
            .Run();

    [Fact]
    public Task Sort_lambda_builder_normalises_to_same_specs()
        => TestPipeline.For<InMemorySortingSpec>(_output, nameof(Sort_lambda_builder_normalises_to_same_specs))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<Widget, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await SeedAsync();

                var lambda = await Widget.All(b => b.OrderByDescending(w => w.Name));
                lambda.Select(w => w.Name).Should().Equal("Charlie", "Bravo", "Alpha");
            })
            .Run();

    [Fact]
    public Task Sort_with_pagination_inverts_fetch_when_adapter_handles_both()
        => TestPipeline.For<InMemorySortingSpec>(_output, nameof(Sort_with_pagination_inverts_fetch_when_adapter_handles_both))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<Widget, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                // Insert 10 widgets with reverse-alphabetic names so natural order != sorted order.
                for (var i = 9; i >= 0; i--)
                {
                    await Widget.Upsert(new Widget { Id = $"w{i:D2}", Name = $"W{(char)('A' + i)}", Priority = i });
                }

                // Page 1, size 3, sort asc → should return W A, W B, W C (NOT page 1 of insertion order
                // which would be W J, W I, W H). This is the core regression test for the
                // "sort-after-paginate" bug fixed by DATA-0092.
                var page1 = await Widget.Page(1, 3, b => b.OrderBy(w => w.Name));
                page1.Should().HaveCount(3);
                page1.Select(w => w.Name).Should().Equal("WA", "WB", "WC");

                // Page 2 continues sorted order.
                var page2 = await Widget.Page(2, 3, b => b.OrderBy(w => w.Name));
                page2.Select(w => w.Name).Should().Equal("WD", "WE", "WF");
            })
            .Run();

    [Fact]
    public Task Sort_collection_traversal_aggregates_max_for_desc()
        => TestPipeline.For<InMemorySortingSpec>(_output, nameof(Sort_collection_traversal_aggregates_max_for_desc))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<Widget, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await Widget.Upsert(new Widget
                {
                    Id = "a", Name = "A",
                    Sightings = new List<Sighting>
                    {
                        new() { LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                        new() { LastChangedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero) }
                    }
                });
                await Widget.Upsert(new Widget
                {
                    Id = "b", Name = "B",
                    Sightings = new List<Sighting>
                    {
                        new() { LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                    }
                });
                await Widget.Upsert(new Widget
                {
                    Id = "c", Name = "C",
                    Sightings = new List<Sighting>
                    {
                        new() { LastChangedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) }
                    }
                });

                // -Sightings.LastChangedAt → MAX aggregation, descending. Expected order: b, c, a.
                var ordered = await Widget.All(new DataQueryOptions().WithSort<Widget>("-Sightings.LastChangedAt"));
                ordered.Select(w => w.Id).Should().Equal("b", "c", "a");
            })
            .Run();

    [Fact]
    public void Strict_parser_throws_on_unresolvable_field()
    {
        var act = () => SortSpecParser.ParseStrict<Widget>("NonexistentField");
        act.Should().Throw<InvalidSortFieldException>()
            .Where(ex => ex.Field == "NonexistentField");
    }

    private static async Task SeedAsync()
    {
        await Widget.Upsert(new Widget { Id = "id1", Name = "Charlie", Priority = 1 });
        await Widget.Upsert(new Widget { Id = "id2", Name = "Alpha", Priority = 3 });
        await Widget.Upsert(new Widget { Id = "id3", Name = "Bravo", Priority = 2 });
    }

    public sealed class Widget : Entity<Widget>
    {
        public string Name { get; set; } = "";
        public int Priority { get; set; }
        public List<Sighting> Sightings { get; set; } = new();
    }

    public sealed class Sighting
    {
        public DateTimeOffset LastChangedAt { get; set; }
    }
}
