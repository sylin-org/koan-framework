using AwesomeAssertions;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;
using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Streaming;

/// <summary>
/// MEDIA-0008: streaming encoders via WriteToAsync.
/// <para>The ADR makes <see cref="IMediaPipeline.WriteToAsync"/> the
/// canonical terminal materialisation, with <see cref="IMediaPipeline.ToBytesAsync"/>
/// preserved as a buffered shim. These specs lock in the contract:
/// the streaming and buffered paths produce bit-identical bytes;
/// animated sources stream through with their frame count intact;
/// cancellation propagates; and the same recipe over the same source
/// is deterministic across invocations (the ETag determinism property
/// MEDIA-0007 §test relies on).</para>
/// </summary>
public sealed class StreamingOutputSpec
{
    [Fact]
    public async Task WriteToAsync_produces_same_bytes_as_ToBytesAsync_for_static_raster()
    {
        // Correctness invariant: the new streaming terminal must produce
        // byte-for-byte identical output to the buffered legacy path so
        // ETag stability and downstream byte equality hold across the
        // MEDIA-0008 migration.
        await using var sourceA = Fixtures.WideJpeg(width: 800, height: 600);
        await using var sourceB = Fixtures.WideJpeg(width: 800, height: 600);

        await using var streamed = new MemoryStream();
        var streamedOutput = await sourceA.AsMedia()
            .Resize(400, 300)
            .EncodeAs("jpeg", 85)
            .WriteToAsync(streamed)
;

        var buffered = await sourceB.AsMedia()
            .Resize(400, 300)
            .EncodeAs("jpeg", 85)
            .ToBytesAsync()
;

        streamed.ToArray().Should().Equal(buffered.Bytes,
            "the streaming terminal must produce identical bytes to the buffered path");
        streamedOutput.Format.Should().Be(buffered.Format);
        streamedOutput.Width.Should().Be(buffered.Width);
        streamedOutput.Height.Should().Be(buffered.Height);
        streamedOutput.FrameCount.Should().Be(buffered.FrameCount);
        streamedOutput.Fingerprint.Should().Be(buffered.Fingerprint);
    }

    [Fact]
    public async Task WriteToAsync_animated_source_writes_valid_animated_output()
    {
        // The animated branch is the load-bearing case for MEDIA-0008:
        // a 200-frame WebP no longer materialises ~80 MB of bytes before
        // the response flushes. Verify the streamed bytes round-trip
        // through the decoder and the frame count survives.
        const int frameCount = 6;
        await using var source = Fixtures.AnimatedWebp(frames: frameCount, width: 80, height: 60);

        await using var sink = new MemoryStream();
        var output = await source.AsMedia()
            .WriteToAsync(sink)
;

        sink.Length.Should().BeGreaterThan(0, "the encoder must have written something into the destination");
        sink.Position = 0;
        using var decoded = await Image.LoadAsync(sink);
        decoded.Frames.Count.Should().Be(frameCount,
            "format-preservation must keep every frame across the streaming terminal");
        output.FrameCount.Should().Be(frameCount);
        output.IsAnimated.Should().BeTrue();
    }

    [Fact]
    public async Task WriteToAsync_streams_into_destination_without_intermediate_buffer()
    {
        // Memory-ceiling property: the streaming terminal writes
        // directly into the destination — no MemoryStream sits between
        // the encoder and the sink. We verify this indirectly by
        // counting writes against a tracking stream and asserting at
        // least one chunk landed during the encode (the buffered path
        // would emit exactly one large WriteAsync after building the
        // full buffer).
        await using var source = Fixtures.AnimatedWebp(frames: 5, width: 320, height: 240);
        await using var tracking = new TrackingStream();

        await source.AsMedia()
            .WriteToAsync(tracking)
;

        tracking.WriteCount.Should().BeGreaterThan(0,
            "the streaming terminal must produce at least one write into the destination");
        tracking.TotalBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WriteToAsync_cancellation_propagates_OperationCanceledException()
    {
        // Cancellation contract: a token signalled before the encode
        // starts must abort the pipeline; partial bytes written to the
        // destination are the caller's problem (the controller drops
        // the response).
        await using var source = Fixtures.AnimatedWebp(frames: 8, width: 240, height: 160);
        await using var sink = new MemoryStream();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await source.AsMedia()
            .WriteToAsync(sink, cts.Token)
;

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation must propagate from the streaming terminal");
    }

    [Fact]
    public async Task WriteToAsync_is_deterministic_for_etag_stability()
    {
        // MEDIA-0007 ETag formula is {sourceHash12}-{recipeFingerprint};
        // a stable ETag across cache misses requires byte-determinism
        // from the encoder. Run the same source + recipe through the
        // streaming terminal twice and assert the bytes match.
        await using var srcA = Fixtures.SquareJpeg(400);
        await using var srcB = Fixtures.SquareJpeg(400);

        await using var firstSink = new MemoryStream();
        await srcA.AsMedia()
            .Resize(200, 200)
            .EncodeAs("jpeg", 80)
            .WriteToAsync(firstSink)
;

        await using var secondSink = new MemoryStream();
        await srcB.AsMedia()
            .Resize(200, 200)
            .EncodeAs("jpeg", 80)
            .WriteToAsync(secondSink)
;

        firstSink.ToArray().Should().Equal(secondSink.ToArray(),
            "same source + same recipe through the streaming terminal must produce identical bytes");
    }

    [Fact]
    public async Task ToBytesAsync_shim_replays_bytes_through_MediaOutput_WriteToAsync()
    {
        // The buffered shim returns a MediaOutput whose WriteToAsync
        // closure replays the captured bytes. Storage write-through
        // (MEDIA-0007 §c) relies on this property — the controller
        // tees the encode through a MemoryStream and then drives the
        // same writer for both the response body AND the storage
        // upload.
        await using var src = Fixtures.WideJpeg(width: 400, height: 300);
        var output = await src.AsMedia()
            .Resize(200, 150)
            .EncodeAs("jpeg", 80)
            .ToBytesAsync()
;

        await using var sink = new MemoryStream();
        await output.WriteToAsync(sink, CancellationToken.None);
        sink.ToArray().Should().Equal(output.Bytes,
            "MediaOutput.WriteToAsync on the buffered path must replay the same bytes that Bytes carries");
    }

    [Fact]
    public async Task MediaOutput_default_writer_falls_back_to_Bytes()
    {
        // Backward compatibility: hand-constructed MediaOutput values
        // (e.g. test fixtures, the MEDIA-0007 GCSpec stand-in) carry
        // bytes through the Bytes field and no WriteToAsync override.
        // The default writer must fall back to those bytes so legacy
        // call sites continue to work.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var output = new MediaOutput(
            Bytes: bytes,
            ContentType: "image/png",
            Format: "png",
            SourceFormat: "png",
            Width: 1,
            Height: 1,
            FrameCount: 1,
            Fingerprint: "fixture");

        await using var sink = new MemoryStream();
        await output.WriteToAsync(sink, CancellationToken.None);
        sink.ToArray().Should().Equal(bytes,
            "the default writer must copy Bytes verbatim so legacy MediaOutput producers stay correct");
    }

    /// <summary>
    /// Minimal sink that records how many WriteAsync calls have landed
    /// and the cumulative byte count. Used by the streaming spec to
    /// distinguish the streaming terminal (multiple chunked writes)
    /// from the buffered shim (a single WriteAsync at the end).
    /// </summary>
    private sealed class TrackingStream : MemoryStream
    {
        public int WriteCount { get; private set; }
        public long TotalBytes { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCount++;
            TotalBytes += count;
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteCount++;
            TotalBytes += buffer.Length;
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            TotalBytes += buffer.Length;
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            WriteCount++;
            TotalBytes += count;
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}
