using System;
using System.Collections.Generic;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class CacheEntryOptionsTests
{
    [Fact]
    public void WithTags_DeduplicatesAndTrims()
    {
        var options = new CacheEntryOptions()
            .WithTags(" alpha ", "Beta", "alpha", null!);

        options.Tags.Should().BeEquivalentTo(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alpha",
            "Beta"
        });
    }

    [Fact]
    public void WithMetadata_AddsEntriesAndOverwrites()
    {
        var options = new CacheEntryOptions()
            .WithMetadata("key", "value1")
            .WithMetadata("key", "value2");

        options.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value2");
    }

    [Fact]
    public void CalculateAbsoluteExpiration_RespectsAbsoluteTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new CacheEntryOptions
        {
            AbsoluteTtl = TimeSpan.FromMinutes(5)
        };

        var expiration = options.CalculateAbsoluteExpiration(now);

        expiration.Should().BeCloseTo(now.AddMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CalculateAbsoluteExpiration_ReturnsNullWhenUnset()
    {
        var options = new CacheEntryOptions();
        options.CalculateAbsoluteExpiration(DateTimeOffset.UtcNow).Should().BeNull();
    }
}
