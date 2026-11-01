using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Source-side limits applied before the full decode allocates memory.
/// Per MEDIA-0004 §13: <c>MaxSourceMegapixels</c> + <c>MaxFrameCount</c>,
/// plus the byte limit on what the pipeline buffers to decode.
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
        // Megapixel check runs first per the header check's ordering. Source
        // is under the MP cap, so the FRAME cap is what fires.
        ex.Which.LimitName.Should().Be("maxFrameCount");
    }

    [Fact]
    public async Task Limits_apply_to_a_non_seekable_source_before_it_decodes()
    {
        await using var src = new ForwardOnlyStream(Fixtures.WideJpeg(width: 2000, height: 2000));
        var limits = new MediaPipelineLimits { MaxSourceMegapixels = 2 };
        var act = async () => await src.AsMedia(limits: limits).EncodeAs("png").ToBytesAsync();
        var ex = await act.Should().ThrowAsync<MediaSourceLimitException>();
        ex.Which.LimitName.Should().Be("maxSourceMegapixels");
    }

    [Fact]
    public async Task A_non_seekable_source_renders()
    {
        await using var src = new ForwardOnlyStream(Fixtures.AnimatedWebp(frames: 3));
        var output = await src.AsMedia().ToBytesAsync();
        output.FrameCount.Should().Be(3);
    }

    [Fact]
    public async Task A_header_declaring_an_enormous_picture_is_refused_without_allocating_it()
    {
        // A GIF's logical screen and first frame claim 65535x65535 in 24 bytes.
        byte[] gif =
        [
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0,
            0x2C, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 2, 1, 0, 0, 0x3B,
        ];
        await using var src = new MemoryStream(gif);
        var act = async () => await src.AsMedia(limits: MediaPipelineLimits.Unlimited).ToBytesAsync();
        var ex = await act.Should().ThrowAsync<MediaSourceLimitException>();
        ex.Which.LimitName.Should().Be("maxDecodedMegapixels");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MaxSourceBytes_refuses_a_longer_source(bool seekable)
    {
        Stream src = Fixtures.WideJpeg(width: 400, height: 400);
        if (!seekable) src = new ForwardOnlyStream(src);
        await using (src)
        {
            var limits = new MediaPipelineLimits { MaxSourceBytes = 64 };
            var act = async () => await src.AsMedia(limits: limits).ToBytesAsync();
            var ex = await act.Should().ThrowAsync<MediaSourceLimitException>();
            ex.Which.LimitName.Should().Be("maxSourceBytes");
            ex.Which.Cap.Should().Be(64);
        }
    }

    [Fact]
    public async Task A_truncated_source_is_refused_rather_than_served_partially()
    {
        var whole = await Fixtures.Snapshot(Fixtures.WideJpeg(width: 400, height: 400));
        await using var truncated = new MemoryStream(whole[..(whole.Length / 2)]);
        var act = async () => await truncated.AsMedia().ToBytesAsync();
        await act.Should().ThrowAsync<MediaDecodeException>();
    }

    /// <summary>
    /// Source bytes with a valid header and a corrupt body (corrupt PNG IDAT
    /// chunk, truncated JPEG segment, etc.) surface as the typed
    /// <see cref="MediaDecodeException"/> the controller maps to 422.
    ///
    /// <para>Regression for the downstream consumer home-LAN incident where a
    /// stored thumbnail's PNG IDAT chunk got corrupted at rest and every
    /// request for it 500'd against the package detail page.</para>
    /// </summary>
    [Fact]
    public async Task Corrupt_source_with_limits_configured_surfaces_typed_decode_error()
    {
        // Synthesise a "valid-header, busted-body" PNG: take a real PNG and
        // flip a byte inside its IDAT chunk so the CRC mismatches but the
        // signature + IHDR still parse cleanly.
        var bytes = await Fixtures.Snapshot(Fixtures.SolidPng(64, 64, new Rgba(10, 200, 30)));
        // Find the IDAT marker (bytes 'I','D','A','T'), then corrupt a byte
        // a few past it so the chunk content (and its trailing CRC32) no
        // longer match.
        var idat = FindIdatOffset(bytes);
        idat.Should().BeGreaterThan(0, "the synthesised PNG should contain at least one IDAT chunk");
        bytes[idat + 12] ^= 0xFF;
        await using var corrupt = new MemoryStream(bytes, writable: false);

        var limits = new MediaPipelineLimits { MaxSourceMegapixels = 100, MaxFrameCount = 100 };
        var act = async () => await corrupt.AsMedia(limits: limits).ToBytesAsync();
        await act.Should().ThrowAsync<MediaDecodeException>();
    }

    /// <summary>A stream that reads forward only, like a network body.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private static int FindIdatOffset(byte[] bytes)
    {
        // Walk past the PNG signature (8 bytes) and scan for "IDAT".
        for (var i = 8; i < bytes.Length - 4; i++)
        {
            if (bytes[i] == (byte)'I' && bytes[i + 1] == (byte)'D'
                && bytes[i + 2] == (byte)'A' && bytes[i + 3] == (byte)'T')
            {
                return i;
            }
        }
        return -1;
    }
}
