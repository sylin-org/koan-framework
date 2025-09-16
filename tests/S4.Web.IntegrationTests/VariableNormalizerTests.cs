using FluentAssertions;
using Koan.Web.GraphQl.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;

namespace S4.Web.IntegrationTests;

public class VariableNormalizerTests
{
    [Fact]
    public void ToDict_should_return_null_on_non_object_root()
    {
    var el = JToken.Parse("[1,2,3]");
    VariableNormalizer.ToDict(el).Should().BeNull();
    }

    [Fact]
    public void ToDict_should_strip_nulls_and_empty_arrays()
    {
    var json = "{\"a\":null,\"b\":[],\"c\":[1],\"d\":\"x\"}";
    var el = JToken.Parse(json);
    var dict = VariableNormalizer.ToDict(el)!;
        dict.Should().NotBeNull();
        dict.ContainsKey("a").Should().BeFalse();
        dict.ContainsKey("b").Should().BeFalse();
        dict["c"].Should().Be(1);
        dict["d"].Should().Be("x");
    }
}
