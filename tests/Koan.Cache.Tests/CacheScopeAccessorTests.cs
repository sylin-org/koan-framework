using System;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Scope;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CacheScopeAccessorTests
{
    [Fact]
    public void Current_ReturnsEmptyWhenNoScope()
    {
        var accessor = new CacheScopeAccessor();
        accessor.Current.Should().Be(CacheScopeContext.Empty);
    }

    [Fact]
    public void PushAndPop_RestoresPreviousScope()
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
    }

    [Fact]
    public void Pop_WithNonCurrentContext_Throws()
    {
        var accessor = new CacheScopeAccessor();
        var first = accessor.Push("tenant-a", null);
        var second = accessor.Push("tenant-b", null);

        Action act = () => accessor.Pop(first);
        act.Should().Throw<InvalidOperationException>();

        accessor.Pop(second);
        accessor.Pop(first);
    }
}
