using System;
using System.Threading.Tasks;
using System.Text;
using Koan.Cache.Abstractions.Primitives;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Primitives;

public sealed class CacheValueSpec
{
    private readonly ITestOutputHelper _output;

    public CacheValueSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task FromString_plaintext_preserves_text_kind_and_bytes()
        => Spec(nameof(FromString_plaintext_preserves_text_kind_and_bytes), () =>
        {
            var value = CacheValue.FromString("hello");

            value.ContentKind.Should().Be(CacheContentKind.String);
            value.Text.Should().Be("hello");
            value.Payload.IsEmpty.Should().BeTrue();
            value.IsEmpty.Should().BeFalse();
            value.RuntimeType.Should().BeNull();
            value.ToText().Should().Be("hello");
            value.ToBytes().Span.SequenceEqual(Encoding.UTF8.GetBytes("hello")).Should().BeTrue();
        });

    [Fact]
    public Task FromString_binary_populates_payload_and_runtime_type()
        => Spec(nameof(FromString_binary_populates_payload_and_runtime_type), () =>
        {
            var value = CacheValue.FromString("payload", asBinary: true, runtimeType: typeof(string));

            value.ContentKind.Should().Be(CacheContentKind.Binary);
            value.Payload.IsEmpty.Should().BeFalse();
            value.Text.Should().Be("payload");
            value.RuntimeType.Should().Be(typeof(string));
            Encoding.UTF8.GetString(value.Payload.Span).Should().Be("payload");
        });

    [Fact]
    public Task FromJson_sets_json_kind_and_text()
        => Spec(nameof(FromJson_sets_json_kind_and_text), () =>
        {
            var json = "{\"value\":42}";
            var value = CacheValue.FromJson(json, typeof(TestDocument));

            value.ContentKind.Should().Be(CacheContentKind.Json);
            value.Text.Should().Be(json);
            value.Payload.Span.SequenceEqual(Encoding.UTF8.GetBytes(json)).Should().BeTrue();
            value.RuntimeType.Should().Be(typeof(TestDocument));
        });

    [Fact]
    public Task FromBytes_creates_binary_without_text()
        => Spec(nameof(FromBytes_creates_binary_without_text), () =>
        {
            var bytes = Encoding.UTF8.GetBytes("raw");
            var value = CacheValue.FromBytes(bytes, typeof(TestDocument));

            value.ContentKind.Should().Be(CacheContentKind.Binary);
            value.Payload.Span.SequenceEqual(bytes).Should().BeTrue();
            value.Text.Should().BeNull();
            value.ToText().Should().Be("raw");
        });

    [Fact]
    public Task IsEmpty_when_payload_and_text_absent()
        => Spec(nameof(IsEmpty_when_payload_and_text_absent), () =>
        {
            var fromEmpty = CacheValue.FromString(string.Empty);
            fromEmpty.IsEmpty.Should().BeTrue();

            var value = CacheValue.FromBytes(ReadOnlyMemory<byte>.Empty);
            value.IsEmpty.Should().BeTrue();
        });

    [Fact]
    public Task Null_inputs_throw_argument_null()
        => Spec(nameof(Null_inputs_throw_argument_null), () =>
        {
            FluentActions.Invoking(() => CacheValue.FromString(null!))
                .Should().Throw<ArgumentNullException>();

            FluentActions.Invoking(() => CacheValue.FromJson(null!))
                .Should().Throw<ArgumentNullException>();
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheValueSpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private sealed record TestDocument(string Id);
}
