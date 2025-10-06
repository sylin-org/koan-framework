using System;
using System.Text;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CacheValueTests
{
    [Fact]
    public void FromString_CreatesTextPayload()
    {
        var value = CacheValue.FromString("hello");
        value.ContentKind.Should().Be(CacheContentKind.String);
        value.ToText().Should().Be("hello");
    }

    [Fact]
    public void FromString_AsBinaryProducesBytes()
    {
        var value = CacheValue.FromString("hello", asBinary: true);
        value.ContentKind.Should().Be(CacheContentKind.Binary);
        value.ToBytes().Span.SequenceEqual(Encoding.UTF8.GetBytes("hello")).Should().BeTrue();
    }

    [Fact]
    public void ToBytes_ForJsonReturnsUtf8()
    {
        var value = CacheValue.FromJson("{\"x\":1}");
        var bytes = value.ToBytes();
        Encoding.UTF8.GetString(bytes.Span).Should().Be("{\"x\":1}");
    }
}
