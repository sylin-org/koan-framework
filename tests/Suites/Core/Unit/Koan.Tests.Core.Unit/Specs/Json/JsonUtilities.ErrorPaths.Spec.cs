using Koan.Core.Json;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace Koan.Tests.Core.Unit.Specs.Json;

public sealed class JsonUtilitiesErrorPathsSpec
{
    private readonly ITestOutputHelper _output;

    public JsonUtilitiesErrorPathsSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed record SamplePayload(string Name);

    [Fact]
    public async Task ToCanonicalJson_throws_when_payload_is_invalid()
    {
        await TestPipeline.For<JsonUtilitiesErrorPathsSpec>(_output, nameof(ToCanonicalJson_throws_when_payload_is_invalid))
            .Assert(_ =>
            {
                Action act = () => "{\"name\":1".ToCanonicalJson();
                act.Should().Throw<JsonReaderException>();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task FromJson_throws_when_payload_is_invalid()
    {
        await TestPipeline.For<JsonUtilitiesErrorPathsSpec>(_output, nameof(FromJson_throws_when_payload_is_invalid))
            .Assert(_ =>
            {
                Action act = () => "{".FromJson<SamplePayload>();
                act.Should().Throw<JsonSerializationException>();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task TryFromJson_with_type_returns_false_on_invalid_payload()
    {
        await TestPipeline.For<JsonUtilitiesErrorPathsSpec>(_output, nameof(TryFromJson_with_type_returns_false_on_invalid_payload))
            .Assert(_ =>
            {
                var success = "{".TryFromJson(typeof(SamplePayload), out var value);
                success.Should().BeFalse();
                value.Should().BeNull();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Merge_throws_when_layers_include_invalid_payload()
    {
        await TestPipeline.For<JsonUtilitiesErrorPathsSpec>(_output, nameof(Merge_throws_when_layers_include_invalid_payload))
            .Assert(_ =>
            {
                Action act = () => JsonMerge.Merge(ArrayMergeStrategy.Union, "{\"name\":\"ok\"}", "{");
                act.Should().Throw<JsonReaderException>();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }
}
