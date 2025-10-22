using FluentAssertions;
using Newtonsoft.Json.Linq;
using Koan.Samples.Meridian.Services;
using Xunit;

namespace S7.Meridian.Tests;

public sealed class MergeTransformsTests
{
    [Fact]
    public void NormalizeToUsd_ScalesCurrencySuffix()
    {
        var result = MergeTransforms.Apply("normalizeToUsd", new JValue("$47.2M"));
        result.Type.Should().Be(JTokenType.Float);
        Math.Abs(result.Value<double>() - 47_200_000d).Should().BeLessThan(0.001);
    }

    [Theory]
    [InlineData("Oct 15, 2024", "2024-10-15")]
    [InlineData("2025/01/03", "2025-01-03")]
    public void NormalizeDateIso_ReturnsIso8601(string input, string expected)
    {
        var result = MergeTransforms.Apply("normalizeDateISO", new JValue(input));
        result.Type.Should().Be(JTokenType.String);
        result.Value<string>().Should().Be(expected);
    }

    [Fact]
    public void NormalizePercent_ProducesFraction()
    {
        var result = MergeTransforms.Apply("normalizePercent", new JValue("87%"));
        result.Type.Should().Be(JTokenType.Float);
        Math.Abs(result.Value<double>() - 0.87d).Should().BeLessThan(0.0001);
    }

    [Fact]
    public void DedupeFuzzy_RemovesNearDuplicates()
    {
        var array = new JArray("Analytics", "analytics", "Security", "Securi ty");
        var result = MergeTransforms.Apply("dedupeFuzzy", array);
        result.Type.Should().Be(JTokenType.Array);
        var values = result.Values<string>().ToList();
        values.Should().Contain("Analytics");
        values.Should().Contain("Security");
        values.Should().HaveCount(2);
    }

    [Fact]
    public void StringToEnum_NormalizesString()
    {
        var result = MergeTransforms.Apply("stringToEnum", new JValue("High Risk"));
        result.Value<string>().Should().Be("HIGH_RISK");
    }

    [Fact]
    public void NumberRounding_RoundsToArgument()
    {
        var result = MergeTransforms.Apply("numberRounding:2", new JValue("123.4567"));
        result.Value<decimal>().Should().Be(123.46m);
    }
}
