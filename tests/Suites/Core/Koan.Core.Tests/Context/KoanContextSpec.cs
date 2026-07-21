using AwesomeAssertions;
using Koan.Core.Context;
using Xunit;

namespace Koan.Core.Tests.Context;

public sealed class KoanContextSpec
{
    private sealed record FirstContext(string Value);
    private sealed record SecondContext(int Value);

    [Fact]
    public void Empty_context_reads_null_and_uses_no_allocation_for_reads_or_suppression()
    {
        KoanContext.Get<FirstContext>().Should().BeNull();
        KoanContext.Suppress<FirstContext>().Dispose(); // JIT/warm the empty path.

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            KoanContext.Get<FirstContext>();
            KoanContext.Suppress<FirstContext>().Dispose();
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    [Fact]
    public void Push_rejects_null_values()
    {
        var act = () => KoanContext.Push<FirstContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Typed_values_coexist_and_scopes_restore_the_exact_prior_snapshot()
    {
        using (KoanContext.Push(new FirstContext("outer")))
        {
            using (KoanContext.Push(new SecondContext(7)))
            {
                KoanContext.Get<FirstContext>().Should().Be(new FirstContext("outer"));
                KoanContext.Get<SecondContext>().Should().Be(new SecondContext(7));

                using (KoanContext.Push(new FirstContext("inner")))
                    KoanContext.Get<FirstContext>().Should().Be(new FirstContext("inner"));

                KoanContext.Get<FirstContext>().Should().Be(new FirstContext("outer"));
                KoanContext.Get<SecondContext>().Should().Be(new SecondContext(7));
            }

            KoanContext.Get<SecondContext>().Should().BeNull();
        }

        KoanContext.Get<FirstContext>().Should().BeNull();
    }

    [Fact]
    public void Suppress_masks_one_type_then_restores_it_without_clobbering_other_types()
    {
        using (KoanContext.Push(new FirstContext("outer")))
        using (KoanContext.Push(new SecondContext(7)))
        {
            using (KoanContext.Suppress<FirstContext>())
            {
                KoanContext.Get<FirstContext>().Should().BeNull();
                KoanContext.Get<SecondContext>().Should().Be(new SecondContext(7));
            }

            KoanContext.Get<FirstContext>().Should().Be(new FirstContext("outer"));
        }
    }

    [Fact]
    public void Scope_disposal_is_idempotent()
    {
        var scope = KoanContext.Push(new FirstContext("value"));

        scope.Dispose();
        var disposeAgain = scope.Dispose;

        disposeAgain.Should().NotThrow();
        KoanContext.Get<FirstContext>().Should().BeNull();
    }

    [Fact]
    public async Task Context_flows_through_await_and_into_child_work()
    {
        using (KoanContext.Push(new FirstContext("flow")))
        {
            await Task.Yield();
            KoanContext.Get<FirstContext>().Should().Be(new FirstContext("flow"));

            var childValue = await Task.Run(
                () => KoanContext.Get<FirstContext>()?.Value,
                TestContext.Current.CancellationToken);

            childValue.Should().Be("flow");
        }
    }

    [Fact]
    public async Task Parallel_flows_do_not_clobber_each_other()
    {
        static async Task<string?> Observe(string value)
        {
            using (KoanContext.Push(new FirstContext(value)))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                return KoanContext.Get<FirstContext>()?.Value;
            }
        }

        var values = await Task.WhenAll(Observe("a"), Observe("b"), Observe("c"));

        values.OrderBy(static value => value).Should().Equal("a", "b", "c");
        KoanContext.Get<FirstContext>().Should().BeNull();
    }

    [Fact]
    public async Task Child_suppression_does_not_mutate_the_parent_flow()
    {
        using (KoanContext.Push(new FirstContext("parent")))
        {
            var childValue = await Task.Run(() =>
            {
                using (KoanContext.Suppress<FirstContext>())
                    return KoanContext.Get<FirstContext>()?.Value;
            }, TestContext.Current.CancellationToken);

            childValue.Should().BeNull();
            KoanContext.Get<FirstContext>().Should().Be(new FirstContext("parent"));
        }
    }
}
