using FluentAssertions;
using Koan.Samples.Meridian.Infrastructure;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;

namespace S7.Meridian.Tests;

public sealed class SchemaValueCoercionTests
{
    [Theory]
    [InlineData("123.45", "{\"type\": \"number\"}", 123.45)]
    [InlineData("42", "{\"type\": \"integer\"}", 42L)]
    public void TryCoerce_ShouldNormalizeNumericStrings(string value, string schemaJson, object expected)
    {
        var schema = JSchema.Parse(schemaJson);

        var success = SchemaValueCoercion.TryCoerce(value, schema, out var normalized, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        normalized.Should().NotBeNull();
        normalized!.Type.Should().Be(expected is long ? JTokenType.Integer : JTokenType.Float);

        if (expected is long integer)
        {
            normalized.Value<long>().Should().Be(integer);
        }
        else
        {
            normalized.Value<double>().Should().BeApproximately((double)expected, 1e-6);
        }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    public void TryCoerce_ShouldNormalizeBooleanStrings(string value, bool expected)
    {
        var schema = JSchema.Parse("{\"type\": \"boolean\"}");

        var success = SchemaValueCoercion.TryCoerce(value, schema, out var normalized, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        normalized.Should().NotBeNull();
        normalized!.Type.Should().Be(JTokenType.Boolean);
        normalized.Value<bool>().Should().Be(expected);
    }
}
