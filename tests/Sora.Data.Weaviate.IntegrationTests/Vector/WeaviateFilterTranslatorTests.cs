using Sora.Data.Abstractions;
using Sora.Data.Weaviate;
using Xunit;

namespace Sora.Data.Weaviate.IntegrationTests.Vector;

public class WeaviateFilterTranslatorTests
{
    [Fact]
    public void And_Or_Not_Compose()
    {
        var f1 = VectorFilter.Eq("type", "doc");
        var f2 = VectorFilter.Gte("score", 10);
        var f3 = VectorFilter.Like("title", "%hello%");
        var ast = VectorFilter.And(VectorFilter.Or(f1, f2), VectorFilter.Not(f3));

        var where = WeaviateFilterTranslator.Translate(ast)!;

        Assert.Contains("operator: And", where);
        Assert.Contains("operator: Or", where);
        Assert.Contains("operator: Not", where);
        Assert.Contains("\"type\"", where);
        Assert.Contains("\"title\"", where);
    }

    [Theory]
    [InlineData("type", "doc", "Equal", "valueText")]
    [InlineData("active", true, "Equal", "valueBoolean")]
    [InlineData("age", 42, "Equal", "valueInt")]
    [InlineData("ratio", 0.75, "Equal", "valueNumber")]
    public void Comparison_Value_Field_Mapping(string path, object value, string op, string field)
    {
        var ast = VectorFilter.Eq(path, value);
        var where = WeaviateFilterTranslator.Translate(ast)!;
        Assert.Contains($"operator: {op}", where);
        Assert.Contains(field + ":", where);
    }
}
