using AwesomeAssertions;
using Koan.Media.Web.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Media.Web.Tests;

public sealed class MediaSourceSelectionSpec
{
    [Fact]
    public void One_media_entity_becomes_the_default_source()
    {
        var services = new ServiceCollection();

        var selection = MediaSourceDiscovery.RegisterDefault(services, [typeof(ScopedMedia)]);

        selection.CandidateCount.Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMediaSource>().Should().BeOfType<MediaEntitySource<ScopedMedia>>();
    }

    [Fact]
    public void Explicit_source_dominates_automatic_discovery()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediaSource, TestMediaSource>();

        MediaSourceDiscovery.RegisterDefault(services, [typeof(ScopedMedia)]);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMediaSource>().Should().BeOfType<TestMediaSource>();
    }

    [Fact]
    public void Ambiguous_discovery_gives_the_exact_correction()
    {
        var services = new ServiceCollection();
        MediaSourceDiscovery.RegisterDefault(services, [typeof(string), typeof(int)]);
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IMediaSource>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*2 concrete MediaEntity candidate(s)*AddMediaSource<T>()*");
    }

    private sealed class TestMediaSource : IMediaSource
    {
        public Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default)
            => Task.FromResult<MediaSourceHandle?>(null);
    }
}
