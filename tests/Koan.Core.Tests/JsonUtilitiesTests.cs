using Newtonsoft.Json.Linq;
using Koan.Core.Json;
using Xunit;

namespace Koan.Core.Tests;

public class JsonUtilitiesTests
{


    [Fact]
    public void ToJson_FromJson_Works()
    {
        var obj = new { Name = "Koan", Count = 2, Skip = (string?)null };
        var json = obj.ToJson();
        Assert.Contains("name", json);
        Assert.DoesNotContain("skip", json);
        var back = json.FromJson<dynamic>();
        Assert.NotNull(back);

    Assert.True(json.TryFromJson(out back));
    var bad = "{";
    Assert.False(bad.TryFromJson(out back));
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

    [Fact]
    public void CanonicalJson_Sorts_Object_Properties()
    {
        var json = "{\"b\":2,\"a\":1,\"c\":{\"y\":2,\"x\":1},\"arr\":[{\"k\":2,\"a\":1},{\"k\":1}] }";
        var canon = json.ToCanonicalJson();
        // Basic check: canonical of canonical is stable
        Assert.Equal(canon, canon.ToCanonicalJson());
        // Properties should be ordered by name
        Assert.Matches("^\\{\\\"a\\\":1,\\\"b\\\":2,\\\"c\\\":\\{\\\"x\\\":1,\\\"y\\\":2},\\\"arr\\\":\\[", canon);
    }

    [Fact]
    public void MergeJson_Array_By_Key()
    {
        var strong = "{\"items\":[{\"id\":\"a\",\"v\":1},{\"id\":\"b\",\"v\":2}]}";
        var weak   = "{\"items\":[{\"id\":\"b\",\"v\":9,\"extra\":1},{\"id\":\"c\",\"v\":3}]}";
        var merged = JsonMerge.Merge(new JsonMerge.JsonMergeOptions { ArrayStrategy = ArrayMergeStrategy.Union, ArrayObjectKey = "id" }, strong, weak);
        var arr = (JArray)merged["items"]!;
        // Order preserved from strong; 'b' merged with weak; 'c' appended
        Assert.Equal(new[] { "a", "b", "c" }, arr.Select(o => (string)o!["id"]!));
        Assert.Equal(2, (int)arr[1]!["v"]!); // strong wins on conflict
        Assert.Equal(1, (int)arr[1]!["extra"]!); // merged in from weak
    }
}
