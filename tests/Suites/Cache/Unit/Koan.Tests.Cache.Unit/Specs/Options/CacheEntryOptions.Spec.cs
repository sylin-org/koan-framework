using Koan.Cache.Abstractions.Primitives;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Options;

public sealed class CacheEntryOptionsSpec
{
    private readonly ITestOutputHelper _output;

    public CacheEntryOptionsSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task WithTags_deduplicates_and_trims()
        => Spec(nameof(WithTags_deduplicates_and_trims), () =>
        {
            var options = new CacheEntryOptions()
                .WithTags(" alpha ", "Beta", "alpha", null!);

            options.Tags.Should().BeEquivalentTo(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "alpha",
                "Beta"
            });
        });

    [Fact]
    public Task WithMetadata_overwrites_existing_entries()
        => Spec(nameof(WithMetadata_overwrites_existing_entries), () =>
        {
            var options = new CacheEntryOptions()
                .WithMetadata("key", "value1")
                .WithMetadata("key", "value2");

            options.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value2");
        });

    [Fact]
    public Task CalculateAbsoluteExpiration_respects_absolute_ttl()
        => Spec(nameof(CalculateAbsoluteExpiration_respects_absolute_ttl), () =>
        {
            var now = DateTimeOffset.UtcNow;
            var options = new CacheEntryOptions
            {
                AbsoluteTtl = TimeSpan.FromMinutes(5)
            };

            var expiration = options.CalculateAbsoluteExpiration(now);

            expiration.Should().BeCloseTo(now.AddMinutes(5), TimeSpan.FromSeconds(1));
        });

    [Fact]
    public Task CalculateAbsoluteExpiration_returns_null_when_unset()
        => Spec(nameof(CalculateAbsoluteExpiration_returns_null_when_unset), () =>
        {
            var options = new CacheEntryOptions();
            options.CalculateAbsoluteExpiration(DateTimeOffset.UtcNow).Should().BeNull();
        });

    private Task Spec(string scenario, Action body)
        => TestPipeline.For<CacheEntryOptionsSpec>(_output, scenario)
            .Assert(_ =>
            {
                body();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
}
