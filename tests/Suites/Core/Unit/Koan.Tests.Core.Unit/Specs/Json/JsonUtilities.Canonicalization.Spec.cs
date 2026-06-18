using Koan.Core.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Tests.Core.Unit.Specs.Json;

public sealed class JsonUtilitiesCanonicalizationSpec
{
    [Fact]
    public void Round_trips_anonymous_object_to_json_and_back()
    {
        var obj = new { Name = "Koan", Count = 2, Skip = (string?)null };
        var json = obj.ToJson();
        json.Should().Contain("name");
        json.Should().NotContain("skip");

        var back = json.FromJson<JObject>();
        back.Should().NotBeNull();

        json.TryFromJson(out JObject? parsed).Should().BeTrue();
        parsed.Should().NotBeNull();

        var bad = "{";
        bad.TryFromJson(out parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void Merge_union_preserves_strong_values_and_appends_unique_items()
    {
        var strong = "{\"arr\":[1,2],\"name\":\"a\",\"obj\":{\"x\":1}}";
        var weak = "{\"arr\":[9,8,7],\"name\":\"b\",\"obj\":{\"x\":2,\"y\":3}}";
        var merged = JsonMerge.Merge(ArrayMergeStrategy.Union, strong, weak);
        var arr = (JArray)merged["arr"]!;
        arr.Select(v => (int)v).Should().Equal(1, 2, 7);
        ((string?)merged["name"]).Should().Be("a");
        ((int)merged["obj"]!["x"]!).Should().Be(1);
        ((int)merged["obj"]!["y"]!).Should().Be(3);
    }

    [Fact]
    public void Merge_replace_keeps_primary_array()
    {
        var merged = JsonMerge.Merge(ArrayMergeStrategy.Replace, "{\"a\":[1,2]}", "{\"a\":[9]}");
        var arr = (JArray)merged["a"]!;
        arr.Select(v => (int)v).Should().Equal(1, 2);
    }

    [Fact]
    public void Merge_concat_combines_arrays()
    {
        var merged = JsonMerge.Merge(ArrayMergeStrategy.Concat, "{\"a\":[1,2]}", "{\"a\":[9]}");
        var arr = (JArray)merged["a"]!;
        arr.Select(v => (int)v).Should().Equal(1, 2, 9);
    }

    [Fact]
    public void Flatten_and_expand_round_trip()
    {
        var token = JToken.Parse("""
        {
            "a": { "b.c": 1 },
            "list": [ { "x": 1 }, 2 ]
        }
        """);
        var flat = JsonPathMapper.Flatten(token);
        flat.Keys.Should().Contain("a.b\\u002ec");
        var back = JsonPathMapper.Expand(flat);
        back.ToString(Newtonsoft.Json.Formatting.None)
            .Should().Be(token.ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void Canonicalization_sorts_properties_predictably()
    {
        var json = "{\"b\":2,\"a\":1,\"c\":{\"y\":2,\"x\":1},\"arr\":[{\"k\":2,\"a\":1},{\"k\":1}] }";
        var canon = json.ToCanonicalJson();
        canon.ToCanonicalJson().Should().Be(canon);
        canon.Should().MatchRegex("^\\{\\\"a\\\":1,\\\"b\\\":2,\\\"c\\\":\\{\\\"x\\\":1,\\\"y\\\":2},\\\"arr\\\":\\[");
    }

    [Fact]
    public void Merge_array_by_key_uses_primary_order_and_combines_objects()
    {
        var strong = "{\"items\":[{\"id\":\"a\",\"v\":1},{\"id\":\"b\",\"v\":2}]}";
        var weak = "{\"items\":[{\"id\":\"b\",\"v\":9,\"extra\":1},{\"id\":\"c\",\"v\":3}]}";
        var merged = JsonMerge.Merge(new JsonMerge.JsonMergeOptions
        {
            ArrayStrategy = ArrayMergeStrategy.Union,
            ArrayObjectKey = "id"
        }, strong, weak);

        var arr = (JArray)merged["items"]!;
        arr.Select(o => (string)o!["id"]!).Should().Equal("a", "b", "c");
        ((int)arr[1]!["v"]!).Should().Be(2);
        ((int)arr[1]!["extra"]!).Should().Be(1);
    }
}
