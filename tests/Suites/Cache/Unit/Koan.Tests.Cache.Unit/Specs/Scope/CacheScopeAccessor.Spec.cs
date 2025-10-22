using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Scope;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Scope;

public sealed class CacheScopeAccessorSpec
{
    private readonly ITestOutputHelper _output;

    public CacheScopeAccessorSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Current_returns_empty_when_no_scope()
        => Spec(nameof(Current_returns_empty_when_no_scope), () =>
        {
            var accessor = new CacheScopeAccessor();
            accessor.Current.Should().Be(CacheScopeContext.Empty);
        });

    [Fact]
    public Task Push_and_pop_restore_previous_scope()
        => Spec(nameof(Push_and_pop_restore_previous_scope), () =>
        {
            var accessor = new CacheScopeAccessor();
            var first = accessor.Push("tenant-a", "region-a");
            accessor.Current.ScopeId.Should().Be("tenant-a");

            var second = accessor.Push("tenant-b", null);
            accessor.Current.ScopeId.Should().Be("tenant-b");

            accessor.Pop(second);
            accessor.Current.ScopeId.Should().Be("tenant-a");

            accessor.Pop(first);
            accessor.Current.Should().Be(CacheScopeContext.Empty);
        });

    [Fact]
    public Task Pop_with_non_current_context_throws()
        => Spec(nameof(Pop_with_non_current_context_throws), () =>
        {
            var accessor = new CacheScopeAccessor();
            var first = accessor.Push("tenant-a", null);
            var second = accessor.Push("tenant-b", null);

            var act = () => accessor.Pop(first);
            act.Should().Throw<InvalidOperationException>();

            accessor.Pop(second);
            accessor.Pop(first);
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheScopeAccessorSpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
}
