using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Source-side limits applied before the full decode allocates memory.
/// Per MEDIA-0004 §13: <c>MaxSourceMegapixels</c> + <c>MaxFrameCount</c>.
/// </summary>
public sealed class SourceLimitsSpec
{
    [Fact]
    public async Task No_limits_passes_huge_sources()
    {
        // A 4000x4000 PNG would trip a 10MP cap but Unlimited lets it through.
        await using var src = Fixtures.WideJpeg(width: 2000, height: 2000);
        var output = await src.AsMedia(limits: MediaPipelineLimits.Unlimited)
            .EncodeAs("png")
            .ToBytesAsync();
        output.Width.Should().Be(2000);
    }

    [Fact]
    public async Task MaxSourceMegapixels_zero_disables_check()
    {
        await using var src = Fixtures.WideJpeg(width: 2000, height: 2000);
        var limits = new MediaPipelineLimits { MaxSourceMegapixels = 0 };
        var output = await src.AsMedia(limits: limits).EncodeAs("png").ToBytesAsync();
        output.Width.Should().Be(2000);
    }

    [Fact]
    public async Task MaxSourceMegapixels_under_cap_passes()
    {
        // 2000x2000 = 4MP, well under a 10MP cap.
        await using var src = Fixtures.WideJpeg(width: 2000, height: 2000);
        var limits = new MediaPipelineLimits { MaxSourceMegapixels = 10 };
        var output = await src.AsMedia(limits: limits).EncodeAs("png").ToBytesAsync();
        output.Width.Should().Be(2000);
    }

    [Fact]
    public async Task MaxSourceMegapixels_over_cap_throws_typed_exception()
    {
        // 2000x2000 = 4MP, over a 2MP cap.
        await using var src = Fixtures.WideJpeg(width: 2000, height: 2000);
        var limits = new MediaPipelineLimits { MaxSourceMegapixels = 2 };
        var act = async () => await src.AsMedia(limits: limits).EncodeAs("png").ToBytesAsync();
        var ex = await act.Should().ThrowAsync<MediaSourceLimitException>();
        ex.Which.LimitName.Should().Be("maxSourceMegapixels");
        ex.Which.Value.Should().Be(4);
        ex.Which.Cap.Should().Be(2);
    }

    [Fact]
    public async Task MaxFrameCount_under_cap_passes()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        var limits = new MediaPipelineLimits { MaxFrameCount = 10 };
        var output = await src.AsMedia(limits: limits).ToBytesAsync();
        output.FrameCount.Should().Be(3);
    }

    [Fact]
    public async Task MaxFrameCount_over_cap_throws_typed_exception()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 5);
        var limits = new MediaPipelineLimits { MaxFrameCount = 2 };
        var act = async () => await src.AsMedia(limits: limits).ToBytesAsync();
        var ex = await act.Should().ThrowAsync<MediaSourceLimitException>();
        ex.Which.LimitName.Should().Be("maxFrameCount");
        ex.Which.Value.Should().Be(5);
        ex.Which.Cap.Should().Be(2);
    }

    [Fact]
    public async Task MaxFrameCount_unlimited_lets_animated_through()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 5);
        var limits = new MediaPipelineLimits { MaxFrameCount = 0 };
        var output = await src.AsMedia(limits: limits).ToBytesAsync();
        output.FrameCount.Should().Be(5);
    }

    [Fact]
    public async Task Both_caps_set_picks_first_offender()
    {
        // Sized big, with animation. The pixel cap is checked first.
        await using var src = Fixtures.AnimatedWebp(frames: 5, width: 200, height: 200);
        // 200x200 = 0.04MP, frames=5. Cap 1MP + frame cap 2.
        var limits = new MediaPipelineLimits { MaxSourceMegapixels = 1, MaxFrameCount = 2 };
        var act = async () => await src.AsMedia(limits: limits).ToBytesAsync();
        var ex = await act.Should().ThrowAsync<MediaSourceLimitException>();
        // Megapixel check runs first per LoadOrThrowAsync ordering. Source
        // is under the MP cap, so the FRAME cap is what fires.
        ex.Which.LimitName.Should().Be("maxFrameCount");
    }
}
