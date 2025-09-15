using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Vector.Filtering;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Testing.Vector;

public class VectorFilterConformanceTests
{
    [Fact]
    public void Json_Shorthand_And_Operator_Parse_To_Same_AST()
    {
    var shorthand = JToken.Parse("{" + "\"color\":\"red\",\"price\":10" + "}");
    var dsl = JToken.Parse("{" + "\"operator\":\"And\",\"operands\":[{" + "\"path\":[\"color\"],\"operator\":\"Eq\",\"value\":\"red\"},{\"path\":[\"price\"],\"operator\":\"Gt\",\"value\":5}]}" + "}");

        var f1 = VectorFilterJson.Parse(shorthand);
        var f2 = VectorFilterJson.Parse(dsl);
        Assert.NotNull(f1);
        Assert.NotNull(f2);
        var s1 = VectorFilterJson.WriteToString(f1!);
        var s2 = VectorFilterJson.WriteToString(f2!);
        Assert.False(string.IsNullOrEmpty(s1));
        Assert.False(string.IsNullOrEmpty(s2));
    }

    private sealed class Sample
    {
        public string Color { get; set; } = string.Empty;
        public int Price { get; set; }
    }

    [Fact]
    public void Expression_To_AST_Supports_Subset()
    {
        var f = VectorFilterExpression.From<Sample>(x => x.Color.StartsWith("re") && x.Price >= 10);
        var s = VectorFilterJson.WriteToString(f);
        Assert.Contains("\"operator\": \"And\"", s);
    }
}
