using Koan.Data.Core.Model;
using Koan.Data.Core.Selection;

namespace Koan.Tests.Data.Core.Specs.Selection;

public sealed class EntityCardinalitySpec
{
    [Fact]
    public async Task One_is_lazy_and_yields_the_same_entity_once()
    {
        var entity = new StringEntity { Id = "one" };

        var source = EntityCardinality.One(entity);
        var result = await Collect(source);

        result.Should().ContainSingle().Which.Should().BeSameAs(entity);
    }

    [Fact]
    public async Task Many_preserves_order_multiplicity_and_custom_key_entities()
    {
        var first = new IntEntity { Id = 1 };
        var second = new IntEntity { Id = 2 };

        var result = await Collect(EntityCardinality.Many([first, second, first]));

        result.Should().Equal(first, second, first);
    }

    [Fact]
    public async Task Many_enumerates_the_finite_source_once_and_only_on_demand()
    {
        var source = new TrackingEnumerable<StringEntity>(
            [new StringEntity { Id = "a" }, new StringEntity { Id = "b" }]);
        var normalized = EntityCardinality.Many(source);

        source.EnumerationCount.Should().Be(0);
        var enumerator = normalized.GetAsyncEnumerator();
        try
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            source.EnumerationCount.Should().Be(1);
            source.YieldCount.Should().Be(1);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        source.EnumerationCount.Should().Be(1);
        source.YieldCount.Should().Be(1);
    }

    [Fact]
    public async Task Stream_is_lazy_one_pass_and_disposes_after_an_early_stop()
    {
        var source = new TrackingAsyncEnumerable<StringEntity>(
            [new StringEntity { Id = "a" }, new StringEntity { Id = "b" }]);
        var normalized = EntityCardinality.Stream(source);

        source.EnumerationCount.Should().Be(0);
        var enumerator = normalized.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        await enumerator.DisposeAsync();

        source.EnumerationCount.Should().Be(1);
        source.YieldCount.Should().Be(1);
        source.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_stops_a_finite_source_before_another_item_is_observed()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new TrackingEnumerable<StringEntity>(
            [new StringEntity { Id = "a" }, new StringEntity { Id = "b" }]);
        var enumerator = EntityCardinality.Many(source, cancellation.Token).GetAsyncEnumerator();

        try
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            cancellation.Cancel();

            Func<Task> moveNext = async () => _ = await enumerator.MoveNextAsync();
            await moveNext.Should().ThrowAsync<OperationCanceledException>();
            source.YieldCount.Should().Be(2, "the source may advance once, but the cancelled item is never accepted");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Null_items_fail_with_their_source_ordinal()
    {
        var entities = new StringEntity[] { new() { Id = "a" }, null! };

        Func<Task> enumerate = async () =>
            _ = await Collect(EntityCardinality.Many(entities!));

        await enumerate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ordinal 1*");
    }

    private static async Task<List<TEntity>> Collect<TEntity>(IAsyncEnumerable<TEntity> source)
    {
        var result = new List<TEntity>();
        await foreach (var entity in source)
            result.Add(entity);
        return result;
    }

    private sealed class StringEntity : Entity<StringEntity>;

    private sealed class IntEntity : Entity<IntEntity, int>;

    private sealed class TrackingEnumerable<TEntity>(IReadOnlyList<TEntity> items) : IEnumerable<TEntity>
    {
        public int EnumerationCount { get; private set; }
        public int YieldCount { get; private set; }

        public IEnumerator<TEntity> GetEnumerator()
        {
            EnumerationCount++;
            foreach (var item in items)
            {
                YieldCount++;
                yield return item;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TrackingAsyncEnumerable<TEntity>(IReadOnlyList<TEntity> items) : IAsyncEnumerable<TEntity>
    {
        public int EnumerationCount { get; private set; }
        public int YieldCount { get; private set; }
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerator<TEntity> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            EnumerationCount++;
            try
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    YieldCount++;
                    yield return item;
                    await Task.CompletedTask.ConfigureAwait(false);
                }
            }
            finally
            {
                DisposeCount++;
            }
        }
    }
}
