using Koan.Core.Json;
using Newtonsoft.Json;

namespace Koan.Tests.Core.Unit.Specs.Json;

public sealed class JsonUtilitiesErrorPathsSpec
{
    private sealed record SamplePayload(string Name);

    [Fact]
    public void ToCanonicalJson_throws_when_payload_is_invalid()
    {
        Action act = () => "{\"name\":1".ToCanonicalJson();
        act.Should().Throw<JsonReaderException>();
    }

    [Fact]
    public void FromJson_throws_when_payload_is_invalid()
    {
        Action act = () => "{".FromJson<SamplePayload>();
        act.Should().Throw<JsonSerializationException>();
    }

    [Fact]
    public void TryFromJson_with_type_returns_false_on_invalid_payload()
    {
        var success = "{".TryFromJson(typeof(SamplePayload), out var value);
        success.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Merge_throws_when_layers_include_invalid_payload()
    {
        Action act = () => JsonMerge.Merge(ArrayMergeStrategy.Union, "{\"name\":\"ok\"}", "{");
        act.Should().Throw<JsonReaderException>();
    }
}
