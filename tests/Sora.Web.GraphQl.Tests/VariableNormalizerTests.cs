using System.Text.Json;
using FluentAssertions;
using Sora.Web.GraphQl.Infrastructure;
using Xunit;

namespace Sora.Web.GraphQl.Tests;

public class VariableNormalizerTests
{
    [Fact]
    public void ToDict_should_return_null_on_non_object_root()
    {
        var el = JsonDocument.Parse("[1,2,3]").RootElement;
        VariableNormalizer.ToDict(el).Should().BeNull();
    }

    [Fact]
    public void ToDict_should_strip_nulls_and_empty_arrays()
    {
        var json = "{\"a\":null,\"b\":[],\"c\":[1],\"d\":\"x\"}";
        var el = JsonDocument.Parse(json).RootElement;
        var dict = VariableNormalizer.ToDict(el)!;
        dict.Should().NotBeNull();
        dict.ContainsKey("a").Should().BeFalse();
        dict.ContainsKey("b").Should().BeFalse();
        dict["c"].Should().Be(1);
        dict["d"].Should().Be("x");
    }
}
