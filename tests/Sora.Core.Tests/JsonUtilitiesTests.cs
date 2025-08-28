using Newtonsoft.Json.Linq;
using Sora.Core.Json;
using Sora.Core.Utilities.Ids;
using Xunit;

namespace Sora.Core.Tests;

public class JsonUtilitiesTests
{
    [Fact]
    public void ShortId_RoundTrip_Guid()
    {
        var g = Guid.NewGuid();
        var s = ShortId.From(g);
        Assert.Equal(22, s.Length);
        var g2 = ShortId.ToGuid(s);
        Assert.Equal(g, g2);
    }

    [Fact]
    public void Ulid_New_HasLength26()
    {
        var u = UlidId.New();
        Assert.Equal(26, u.Length);
    }

    [Fact]
    public void ToJson_FromJson_Works()
    {
        var obj = new { Name = "Sora", Count = 2, Skip = (string?)null };
        var json = obj.ToJson();
        Assert.Contains("name", json);
        Assert.DoesNotContain("skip", json);
        var back = json.FromJson<dynamic>();
        Assert.NotNull(back);
    }

    [Fact]
    public void MergeJson_DefaultUnion()
    {
        var strong = "{\"arr\":[1,2],\"name\":\"a\",\"obj\":{\"x\":1}}";
        var weak   = "{\"arr\":[9,8,7],\"name\":\"b\",\"obj\":{\"x\":2,\"y\":3}}";
        var merged = JsonMerge.Merge(ArrayMergeStrategy.Union, strong, weak);
        var arr = (JArray)merged["arr"]!;
        Assert.Equal(new[] {1,2,7}, arr.Select(v => (int)v));
        Assert.Equal("a", (string?)merged["name"]);
        Assert.Equal(1, (int)merged["obj"]!["x"]!);
        Assert.Equal(3, (int)merged["obj"]!["y"]!);
    }

    [Fact]
    public void MergeJson_ReplaceArray()
    {
        var merged = JsonMerge.Merge(ArrayMergeStrategy.Replace, "{\"a\":[1,2]}", "{\"a\":[9]}");
        var arr = (JArray)merged["a"]!;
        Assert.Equal(new[] {1,2}, arr.Select(v => (int)v));
    }

    [Fact]
    public void MergeJson_ConcatArray()
    {
        var merged = JsonMerge.Merge(ArrayMergeStrategy.Concat, "{\"a\":[1,2]}", "{\"a\":[9]}");
        var arr = (JArray)merged["a"]!;
        Assert.Equal(new[] {1,2,9}, arr.Select(v => (int)v));
    }

    [Fact]
    public void JsonPath_Flatten_Expand_RoundTrip()
    {
        var token = JToken.Parse("{\n  \"a\": { \"b.c\": 1 },\n  \"list\": [ { \"x\": 1 }, 2 ]\n}");
        var flat = JsonPathMapper.Flatten(token);
        // Expect escaped key for b.c
        Assert.Contains("a.b\\u002ec", flat.Keys);
        var back = JsonPathMapper.Expand(flat);
        Assert.Equal(token.ToString(Newtonsoft.Json.Formatting.None), back.ToString(Newtonsoft.Json.Formatting.None));
    }
}
